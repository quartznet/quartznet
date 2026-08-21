using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A record of the calls one collaborator member has received, which a test can await instead of
/// poll.
/// </summary>
/// <remarks>
/// This is what keeps the scheduler-loop tests off the wall clock. The assertions are about which
/// calls the loop made; awaiting a call only decides when it is safe to look at them, so the waits
/// carry a generous deadline and never a timing expectation.
/// </remarks>
public sealed class CallLog<T>
{
    private readonly Lock gate = new();
    private readonly List<T> entries = new();
    private readonly List<(int Count, TaskCompletionSource Source)> waiters = new();

    /// <summary>
    /// The calls recorded so far, oldest first.
    /// </summary>
    public IReadOnlyList<T> Entries
    {
        get
        {
            lock (gate)
            {
                return entries.ToArray();
            }
        }
    }

    /// <summary>
    /// How many calls have been recorded so far.
    /// </summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    /// <summary>
    /// Records one call, releasing everyone waiting for the count it brings the log to.
    /// </summary>
    public void Record(T entry)
    {
        List<TaskCompletionSource> ready = null;
        lock (gate)
        {
            entries.Add(entry);
            for (int i = waiters.Count - 1; i >= 0; i--)
            {
                if (waiters[i].Count <= entries.Count)
                {
                    ready ??= new List<TaskCompletionSource>();
                    ready.Add(waiters[i].Source);
                    waiters.RemoveAt(i);
                }
            }
        }

        if (ready is null)
        {
            return;
        }

        // Completed outside the lock: a continuation that records another call would otherwise
        // re-enter it on this very thread.
        foreach (TaskCompletionSource source in ready)
        {
            source.TrySetResult();
        }
    }

    /// <summary>
    /// A task that completes once this member has been called <paramref name="count" /> times.
    /// </summary>
    public Task Reaches(int count)
    {
        TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (entries.Count >= count)
            {
                return Task.CompletedTask;
            }

            waiters.Add((count, source));
        }

        return source.Task;
    }
}

/// <summary>
/// One <see cref="IJobStore.TriggeredJobComplete" /> call, as the scheduler thread made it.
/// </summary>
public sealed record CompletedFiring(TriggerKey Trigger, JobKey Job, SchedulerInstruction Instruction);

/// <summary>
/// Scripts <see cref="IJobStore.AcquireNextTriggers" />: called with the 1-based number of the call,
/// the request the scheduler thread built, and a continuation that runs the real store. Throw from it
/// to fail acquisition, or answer the call yourself.
/// </summary>
public delegate ValueTask<List<IOperableTrigger>> AcquireNextTriggersScript(
    int call,
    TriggerAcquisitionRequest request,
    Func<ValueTask<List<IOperableTrigger>>> callThrough);

/// <summary>
/// Scripts <see cref="IJobStore.TriggersFired" />: called with the 1-based number of the call, the
/// triggers being fired, and a continuation that runs the real store. Throw from it to fail the whole
/// batch, or edit the results the real store produced to fail one firing.
/// </summary>
public delegate ValueTask<List<TriggerFiredResult>> TriggersFiredScript(
    int call,
    IReadOnlyCollection<IOperableTrigger> triggers,
    Func<ValueTask<List<TriggerFiredResult>>> callThrough);

/// <summary>
/// A job store that wraps <see cref="RAMJobStore" />, lets a test script individual members to throw
/// or answer on the Nth call, and records the calls <see cref="QuartzSchedulerThread" /> makes on the
/// way past.
/// </summary>
/// <remarks>
/// The scheduler thread's error handling is only visible as store traffic — which trigger was
/// released, which completion instruction a failed dispatch wrote. Wrapping the real store rather than
/// faking one keeps everything the loop is not being tested on behaving as it does in production.
/// </remarks>
public sealed class FaultInjectingJobStore : DelegatingJobStore
{
    private int acquireCalls;
    private int triggersFiredCalls;

    public FaultInjectingJobStore(
        ISchedulerSignaler signaler = null,
        TimeProvider timeProvider = null,
        ILoggerFactory loggerFactory = null)
        : base(new RAMJobStore(
            loggerFactory ?? TestJobStores.LoggerFactory(),
            signaler ?? TestJobStores.Signaler(),
            timeProvider ?? TimeProvider.System))
    {
    }

    /// <summary>
    /// What each <see cref="AcquireNextTriggers" /> call should do; <see langword="null" /> lets the
    /// real store answer every call.
    /// </summary>
    public AcquireNextTriggersScript OnAcquireNextTriggers { get; set; }

    /// <summary>
    /// What each <see cref="TriggersFired" /> call should do; <see langword="null" /> lets the real
    /// store answer every call.
    /// </summary>
    public TriggersFiredScript OnTriggersFired { get; set; }

    /// <summary>The requests the scheduler thread built, recorded before the call is answered.</summary>
    public CallLog<TriggerAcquisitionRequest> Acquisitions { get; } = new();

    /// <summary>The triggers handed back through <see cref="ReleaseAcquiredTrigger" />.</summary>
    public CallLog<TriggerKey> Releases { get; } = new();

    /// <summary>The completions the loop reported, instruction included.</summary>
    public CallLog<CompletedFiring> Completions { get; } = new();

    /// <summary>The failure counts <see cref="GetAcquireRetryDelay" /> was consulted with.</summary>
    public CallLog<int> AcquireRetryDelays { get; } = new();

    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
    {
        Acquisitions.Record(request);
        int call = Interlocked.Increment(ref acquireCalls);

        AcquireNextTriggersScript script = OnAcquireNextTriggers;
        if (script is null)
        {
            return base.AcquireNextTriggers(request, cancellationToken);
        }

        return script(call, request, () => base.AcquireNextTriggers(request, cancellationToken));
    }

    public override ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        Releases.Record(trigger.Key);
        return base.ReleaseAcquiredTrigger(trigger, cancellationToken);
    }

    public override ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        int call = Interlocked.Increment(ref triggersFiredCalls);

        TriggersFiredScript script = OnTriggersFired;
        if (script is null)
        {
            return base.TriggersFired(triggers, cancellationToken);
        }

        return script(call, triggers, () => base.TriggersFired(triggers, cancellationToken));
    }

    public override ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        Completions.Record(new CompletedFiring(trigger.Key, jobDetail.Key, triggerInstructionCode));
        return base.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken);
    }

    public override TimeSpan GetAcquireRetryDelay(int failureCount)
    {
        AcquireRetryDelays.Record(failureCount);
        return base.GetAcquireRetryDelay(failureCount);
    }
}
