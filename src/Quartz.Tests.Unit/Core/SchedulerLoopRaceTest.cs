using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Two races in how the scheduler loop reads a pause and a scheduling signal, each of which let a
/// trigger fire at the wrong time on the system clock. Found on 4.x by #3901 and present here in the
/// same shape.
/// </summary>
/// <remarks>
/// The store and the thread pool are doubles named by type, as <c>quartz.jobStore.type</c> and
/// <c>quartz.threadPool.type</c> are, so the fixture reaches the instances the factory built through
/// <see cref="LoopRaceJobStore.LastInstance" /> and <see cref="LoopRaceThreadPool.LastInstance" />;
/// that is why it is not parallelizable. Every wait for the scheduler is bounded, so a regression fails
/// rather than hangs.
/// </remarks>
[NonParallelizable]
public class SchedulerLoopRaceTest
{
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a test watches a paused loop for something it must not do. A loop that does it does so
    /// within milliseconds of being let go; this is a margin that no loaded agent eats.
    /// </summary>
    private static readonly TimeSpan pausedObservation = TimeSpan.FromSeconds(3);

    private IScheduler scheduler;
    private LoopRaceJobStore store;
    private LoopRaceThreadPool threadPool;

    [SetUp]
    public async Task SetUp()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "LoopRace_" + Guid.NewGuid().ToString("N"),
            ["quartz.jobStore.type"] = typeof(LoopRaceJobStore).AssemblyQualifiedName,
            ["quartz.threadPool.type"] = typeof(LoopRaceThreadPool).AssemblyQualifiedName,
        };

        scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        store = LoopRaceJobStore.LastInstance;
        threadPool = LoopRaceThreadPool.LastInstance;
    }

    [TearDown]
    public async Task TearDown()
    {
        store.ReleaseHeldAcquisition();
        await scheduler.Shutdown(false);
    }

    /// <summary>
    /// A pause signals a scheduling change so the loop leaves whatever it is doing, and the round the
    /// loop is starting drains that signal. A pause landing between the loop's pause check and the
    /// drain was lost, and the paused loop went on to ask the store for triggers.
    /// </summary>
    /// <remarks>
    /// The thread pool is asked for free threads between those two points, so that is where the test
    /// stands the scheduler by.
    /// </remarks>
    [Test]
    public async Task APauseThatLandsAsARoundStartsStopsTheRound()
    {
        threadPool.OnBlockForAvailableThreads = request =>
        {
            if (request == 2)
            {
                StandBy();
            }
        };

        await scheduler.Start();
        await ShouldObserve(store.Acquisitions.Reaches(1), "the first round asks the store");

        // Anything that signals starts the next round; a trigger an hour out fires nothing on the way.
        await scheduler.ScheduleJob(Job("later"), Trigger("later", DateTimeOffset.UtcNow.AddHours(1)));

        Task secondRound = store.Acquisitions.Reaches(2);
        (await Task.WhenAny(secondRound, Task.Delay(pausedObservation))).Should().NotBeSameAs(secondRound,
            "the loop was paused as its next round started, so a paused loop must not ask the store for triggers");

        await scheduler.Start();
        await ShouldObserve(secondRound, "started again, the loop asks the store again, so it was paused rather than stuck");
    }

    /// <summary>
    /// What losing the pause cost a deployment: <c>Standby()</c> landing while the loop waited for a free
    /// worker let the round that followed fire a trigger that was due, after <c>Standby()</c> had
    /// returned.
    /// </summary>
    [Test]
    public async Task AStandbyThatLandsWhileTheLoopWaitsForAWorkerFiresNothing()
    {
        await scheduler.ScheduleJob(Job("due"), Trigger("due", DateTimeOffset.UtcNow));

        // Past the loop's pause check and short of its drain, which is where a saturated scheduler
        // blocks for a worker to come free.
        threadPool.OnBlockForAvailableThreads = request =>
        {
            if (request == 1)
            {
                StandBy();
            }
        };

        await scheduler.Start();

        Task fired = store.Fired.Reaches(1);
        (await Task.WhenAny(fired, Task.Delay(pausedObservation))).Should().NotBeSameAs(fired,
            "the scheduler was in standby before the round acquired anything, so the due trigger must wait for it to start again");
        store.Acquisitions.Count.Should().Be(0, "a paused loop does not ask the store for triggers");

        await scheduler.Start();
        await ShouldObserve(fired, "started again, the scheduler fires the trigger that was due");
    }

    /// <summary>
    /// Stands the scheduler by from the loop's own thread, as a <c>Standby()</c> from anywhere else lands
    /// while the loop is blocked there.
    /// </summary>
    private void StandBy()
    {
        Task.Run(() => scheduler.Standby()).GetAwaiter().GetResult();
    }

    private static IJobDetail Job(string name)
    {
        return JobBuilder.Create<NoOpJob>().WithIdentity(name, "loopRace").Build();
    }

    private static ITrigger Trigger(string name, DateTimeOffset fireTimeUtc)
    {
        return TriggerBuilder.Create()
            .WithIdentity(name, "loopRace")
            .StartAt(fireTimeUtc)
            .Build();
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    public class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}

/// <summary>
/// A record of calls a test can await instead of poll.
/// </summary>
public sealed class ObservedCalls<T>
{
    private readonly object gate = new object();
    private readonly List<T> entries = new List<T>();
    private readonly List<KeyValuePair<int, TaskCompletionSource<bool>>> waiters = new List<KeyValuePair<int, TaskCompletionSource<bool>>>();

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

    public void Record(T entry)
    {
        List<TaskCompletionSource<bool>> ready = new List<TaskCompletionSource<bool>>();
        lock (gate)
        {
            entries.Add(entry);
            for (int i = waiters.Count - 1; i >= 0; i--)
            {
                if (waiters[i].Key <= entries.Count)
                {
                    ready.Add(waiters[i].Value);
                    waiters.RemoveAt(i);
                }
            }
        }

        foreach (TaskCompletionSource<bool> source in ready)
        {
            source.TrySetResult(true);
        }
    }

    /// <summary>A task that completes once <paramref name="count" /> calls have been recorded.</summary>
    public Task Reaches(int count)
    {
        TaskCompletionSource<bool> source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (entries.Count >= count)
            {
                return Task.CompletedTask;
            }

            waiters.Add(new KeyValuePair<int, TaskCompletionSource<bool>>(count, source));
        }

        return source.Task;
    }
}

/// <summary>
/// The in-memory store, recording what the scheduler loop asks of it and able to hold the loop in the
/// store call that acquires its first trigger.
/// </summary>
public class LoopRaceJobStore : RAMJobStore
{
    private readonly TaskCompletionSource<bool> heldAcquisitionReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> acquisitionHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private int acquisitionsWithTriggers;

    public LoopRaceJobStore()
    {
        LastInstance = this;
    }

    public static LoopRaceJobStore LastInstance { get; private set; }

    /// <summary>Every acquisition the loop asked for, recorded before it is answered.</summary>
    public ObservedCalls<DateTimeOffset> Acquisitions { get; } = new ObservedCalls<DateTimeOffset>();

    /// <summary>The triggers the loop handed back without firing.</summary>
    public ObservedCalls<TriggerKey> Released { get; } = new ObservedCalls<TriggerKey>();

    /// <summary>The triggers the loop fired.</summary>
    public ObservedCalls<TriggerKey> Fired { get; } = new ObservedCalls<TriggerKey>();

    /// <summary>
    /// When set, the first acquisition that returns a trigger waits, once the store has answered it,
    /// until <see cref="ReleaseHeldAcquisition" />.
    /// </summary>
    public bool HoldFirstAcquisitionWithTriggers { get; set; }

    /// <summary>
    /// When set, the acquisition with this 1-based number waits, before the store answers it, until
    /// <see cref="ReleaseHeldAcquisition" />.
    /// </summary>
    public int HoldAcquisitionBeforeAnswering { get; set; }

    /// <summary>Completes once the loop is being held in an acquisition.</summary>
    public Task AcquisitionHeld => acquisitionHeld.Task;

    public void ReleaseHeldAcquisition() => heldAcquisitionReleased.TrySetResult(true);

    public override async Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(
        DateTimeOffset noLaterThan,
        int maxCount,
        TimeSpan timeWindow,
        CancellationToken cancellationToken = default)
    {
        Acquisitions.Record(noLaterThan);

        if (Acquisitions.Count == HoldAcquisitionBeforeAnswering)
        {
            acquisitionHeld.TrySetResult(true);
            await heldAcquisitionReleased.Task.ConfigureAwait(false);
        }

        IReadOnlyCollection<IOperableTrigger> acquired = await base.AcquireNextTriggers(noLaterThan, maxCount, timeWindow, cancellationToken).ConfigureAwait(false);

        if (HoldFirstAcquisitionWithTriggers && acquired.Count > 0 && Interlocked.Increment(ref acquisitionsWithTriggers) == 1)
        {
            acquisitionHeld.TrySetResult(true);
            await heldAcquisitionReleased.Task.ConfigureAwait(false);
        }

        return acquired;
    }

    public override Task ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        Released.Record(trigger.Key);
        return base.ReleaseAcquiredTrigger(trigger, cancellationToken);
    }

    public override async Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(
        IReadOnlyCollection<IOperableTrigger> triggers,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<TriggerFiredResult> results = await base.TriggersFired(triggers, cancellationToken).ConfigureAwait(false);

        foreach (TriggerFiredResult result in results)
        {
            if (result.TriggerFiredBundle != null)
            {
                Fired.Record(result.TriggerFiredBundle.Trigger.Key);
            }
        }

        return results;
    }
}

/// <summary>
/// The default thread pool, with a hook where the scheduler loop asks it for free threads: past the
/// loop's pause check and short of the drain that starts its round.
/// </summary>
public class LoopRaceThreadPool : IThreadPool
{
    private readonly DefaultThreadPool inner = new DefaultThreadPool();
    private int requests;

    public LoopRaceThreadPool()
    {
        LastInstance = this;
    }

    public static LoopRaceThreadPool LastInstance { get; private set; }

    /// <summary>Called with the 1-based number of the request, before the pool answers it.</summary>
    public Action<int> OnBlockForAvailableThreads { get; set; }

    public int MaxConcurrency
    {
        get => inner.MaxConcurrency;
        set => inner.MaxConcurrency = value;
    }

    public int PoolSize => inner.PoolSize;

    public string InstanceId
    {
        set => ((IThreadPool) inner).InstanceId = value;
    }

    public string InstanceName
    {
        set => ((IThreadPool) inner).InstanceName = value;
    }

    public int BlockForAvailableThreads()
    {
        OnBlockForAvailableThreads?.Invoke(Interlocked.Increment(ref requests));
        return inner.BlockForAvailableThreads();
    }

    public void Initialize() => inner.Initialize();

    public bool RunInThread(Func<Task> runnable) => inner.RunInThread(runnable);

    public void Shutdown(bool waitForJobsToComplete = true) => inner.Shutdown(waitForJobsToComplete);
}
