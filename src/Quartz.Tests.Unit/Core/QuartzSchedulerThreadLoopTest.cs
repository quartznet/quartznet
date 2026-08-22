using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Covers the failure handling in <see cref="QuartzSchedulerThread" />'s main loop: what it does when
/// the job store refuses to hand over triggers, when firing them fails wholesale or one at a time,
/// when a run shell cannot be built, and when the thread pool refuses the work.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is about a call — which store member the loop reached and with what — never
/// about how long anything took. The tests wait for those calls through <see cref="CallLog{T}" />
/// rather than sleeping, so the deadline on each wait is only a way of failing instead of hanging.
/// </para>
/// <para>
/// The thread is constructed directly on a scheduler whose loop is never started, so the only loop
/// running is the one under test.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class QuartzSchedulerThreadLoopTest
{
    /// <summary>
    /// How long a test is willing to wait for a call before declaring the loop stuck. Long enough that
    /// a loaded build agent never trips it, and never used as a measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    private const int AvailableThreads = 4;

    private FaultInjectingJobStore store;
    private IThreadPool threadPool;
    private ScriptedJobRunShellFactory shellFactory;
    private QuartzSchedulerResources resources;
    private QuartzScheduler scheduler;
    private QuartzSchedulerThread thread;

    [SetUp]
    public async Task SetUp()
    {
        store = new FaultInjectingJobStore();
        await store.Initialize(TestJobStores.Identity());

        threadPool = A.Fake<IThreadPool>();
        A.CallTo(() => threadPool.PoolSize).Returns(AvailableThreads);
        A.CallTo(() => threadPool.WaitForAvailableThreads(A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(AvailableThreads));

        // Accepted but never run, so a dispatched firing stays in flight for the rest of the test and
        // the loop's bookkeeping can be read from the request it builds next.
        A.CallTo(() => threadPool.TryRun(A<Func<ValueTask>>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        shellFactory = new ScriptedJobRunShellFactory();

        resources = new QuartzSchedulerResources
        {
            Name = "loopTest",
            InstanceId = "loopTestInstance",
            IdleWaitTime = TimeSpan.FromSeconds(1),
            MaxBatchSize = 5,
            JobStore = store,
            ThreadPool = threadPool,
            JobRunShellFactory = shellFactory,
        };

        scheduler = new QuartzScheduler(resources);
        shellFactory.Initialize(A.Fake<IScheduler>());
        thread = new QuartzSchedulerThread(scheduler, resources);
    }

    [TearDown]
    public async Task TearDown()
    {
        await thread.Halt(wait: true);
        await thread.Shutdown();
        await store.Shutdown();
    }

    [Test]
    public async Task AnAcquisitionFailureIsRetriedAfterAskingTheStoreHowLongToBackOff()
    {
        store.OnAcquireNextTriggers = (call, _, callThrough) =>
        {
            if (call <= 3)
            {
                throw new JobPersistenceException("the database is gone");
            }

            return callThrough();
        };

        StartLoop();

        await ShouldObserve(store.Acquisitions.Reaches(4),
            "a store that fails to hand over triggers must not stop the loop asking again");

        store.AcquireRetryDelays.Entries.Should().Equal([2, 3],
            "the loop rides out a single failure, and from the second one on it asks the store itself how long to back off");
    }

    /// <summary>
    /// The loop has two arms for a failed acquisition: one for <see cref="JobPersistenceException" />,
    /// which notifies scheduler listeners, and one for anything else, which only logs. Both count the
    /// failure and retry, and this pins the second.
    /// </summary>
    [Test]
    public async Task AnAcquisitionFailureThatIsNotAPersistenceProblemIsRetriedTheSameWay()
    {
        store.OnAcquireNextTriggers = (call, _, callThrough) =>
        {
            if (call <= 3)
            {
                throw new InvalidOperationException("the store is confused");
            }

            return callThrough();
        };

        StartLoop();

        await ShouldObserve(store.Acquisitions.Reaches(4),
            "an unexpected exception from the store is survived exactly like a persistence one");

        store.AcquireRetryDelays.Entries.Should().Equal([2, 3],
            "the back-off is driven by the failure count, not by the kind of exception that produced it");
    }

    [Test]
    public async Task AFiringFailureReleasesEveryTriggerTheBatchAcquired()
    {
        IReadOnlyList<TriggerKey> scheduled = await GivenScheduledJobs(3);

        // Failing every call, not just the first, so that "nothing was dispatched" stays true for as
        // long as the loop keeps trying rather than only until it retries.
        store.OnTriggersFired = (_, _, _) => throw new SchedulerException("the fired-trigger write failed");

        StartLoop();

        await ShouldObserve(store.Releases.Reaches(3),
            "a batch that could not be fired has to go back, or its triggers stay stuck in the acquired state");

        store.Releases.Entries.Take(3).Should().BeEquivalentTo(scheduled,
            "every trigger of the failed batch is released, not just the one that was being fired");
        shellFactory.Created.Count.Should().Be(0,
            "nothing was dispatched, so no run shell should have been built");
    }

    [Test]
    public async Task APerTriggerFiringFailureReleasesThatTriggerAndDispatchesTheRest()
    {
        await GivenScheduledJobs(3);

        TriggerKey failed = null;
        store.OnTriggersFired = async (call, triggers, callThrough) =>
        {
            List<TriggerFiredResult> results = await callThrough();
            if (call == 1)
            {
                // Results are index-aligned with the triggers handed in, which is how the loop pairs
                // a failure back to the trigger it belongs to.
                failed = triggers.First().Key;
                results[0] = TriggerFiredResult.Failed(new FiredTriggerWriteException());
            }

            return results;
        };

        StartLoop();

        await ShouldObserve(shellFactory.Created.Reaches(2),
            "one failed firing must not cost the rest of the batch their dispatch");
        await ShouldObserve(store.Releases.Reaches(1),
            "the trigger whose firing came back with a database error has to be released");

        store.Releases.Entries.Should().Equal([failed],
            "only the trigger the failure belongs to is released");
        shellFactory.Created.Entries.Should().NotContain(failed,
            "a trigger that failed to fire is not handed to a run shell");
    }

    [Test]
    public async Task AFailedRunShellPutsAllOfTheJobsTriggersInError()
    {
        await GivenScheduledJobs(1);
        shellFactory.OnCreate = (_, _) => throw new SchedulerException("no run shell for you");

        StartLoop();

        await ShouldObserve(store.Completions.Reaches(1),
            "a firing that can never be run still has to be completed, or its job stays blocked");

        store.Completions.Entries[0].Instruction.Should().Be(SchedulerInstruction.SetAllJobTriggersError,
            "the loop treats a run shell it cannot build as permanent - the job would fail the same way next time");
    }

    /// <summary>
    /// The same catch arm, taking the other branch: an <see cref="ObjectDisposedException" /> under the
    /// <see cref="SchedulerException" /> means the scheduler is going away rather than that the job is
    /// broken, so the firing is completed with no instruction instead of being marked in error.
    /// </summary>
    [Test]
    public async Task ARunShellRefusedBecauseTheSchedulerIsGoingAwayCompletesWithNoInstruction()
    {
        await GivenScheduledJobs(1);
        shellFactory.OnCreate = (_, _) => throw new SchedulerException(
            "the scheduler is shutting down",
            new ObjectDisposedException(nameof(QuartzScheduler)));

        StartLoop();

        await ShouldObserve(store.Completions.Reaches(1),
            "completion is what unblocks the siblings of a DisallowConcurrentExecution job, so it runs even on the shutdown path");

        store.Completions.Entries[0].Instruction.Should().Be(SchedulerInstruction.NoInstruction,
            "a shell refused because the scheduler is disposing says nothing about the job, so its triggers must not be marked in error");
    }

    [Test]
    public async Task AThreadPoolThatRefusesTheWorkPutsTheJobsTriggersInErrorAndGivesTheSlotBack()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create().ForGroup("batch", 2).Build());
        await GivenScheduledJobs(1, executionGroup: "batch");

        A.CallTo(() => threadPool.TryRun(A<Func<ValueTask>>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(false));

        StartLoop();

        await ShouldObserve(store.Completions.Reaches(1),
            "work the pool refused is still a firing the store has committed, so it has to be completed");
        await ShouldObserve(store.Acquisitions.Reaches(2),
            "the loop carries straight on to the next acquisition after a refused dispatch");

        store.Completions.Entries[0].Instruction.Should().Be(SchedulerInstruction.SetAllJobTriggersError,
            "a pool that refuses work while the scheduler is running is a bug, and the loop reports it as an error on the job");
        LimitFor(store.Acquisitions.Entries[1], "batch").Should().Be(2,
            "the slot taken before the dispatch is given back when the pool refuses, so the group is fully available again");
    }

    [Test]
    public async Task AThreadPoolThatRefusesTheWorkDuringShutdownCompletesWithNoInstruction()
    {
        await GivenScheduledJobs(1);

        A.CallTo(() => threadPool.TryRun(A<Func<ValueTask>>.Ignored, A<CancellationToken>.Ignored))
            .ReturnsLazily(() => HaltThenRefuse());

        StartLoop();

        await ShouldObserve(store.Completions.Reaches(1),
            "a firing the pool refused on the way down still has to be completed");

        store.Completions.Entries[0].Instruction.Should().Be(SchedulerInstruction.NoInstruction,
            "a pool refusing work because the scheduler is halting says nothing about the job, so its triggers must not be marked in error");

        async ValueTask<bool> HaltThenRefuse()
        {
            await thread.Halt(wait: false);
            return false;
        }
    }

    [Test]
    public async Task DispatchedWorkIsSubtractedFromTheLimitsTheNextAcquisitionAsksFor()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create().ForGroup("batch", 2).Build());
        await GivenScheduledJobs(1, executionGroup: "batch");

        StartLoop();

        await ShouldObserve(store.Acquisitions.Reaches(2),
            "the loop acquires again as soon as it has dispatched a batch");

        TriggerAcquisitionRequest first = store.Acquisitions.Entries[0];
        first.MaxCount.Should().Be(Math.Min(AvailableThreads, resources.MaxBatchSize),
            "a batch is capped by whichever of the free threads and the configured batch size is smaller");
        LimitFor(first, "batch").Should().Be(2,
            "nothing of this node's is running yet, so the whole configured limit is available");

        LimitFor(store.Acquisitions.Entries[1], "batch").Should().Be(1,
            "the firing dispatched from the first pass is still in flight and holds one of the group's two slots");
    }

    private void StartLoop()
    {
        thread.Start();
        thread.TogglePause(pause: false);
    }

    /// <summary>
    /// Waits for a call the loop is expected to make, failing with <paramref name="because" /> rather
    /// than hanging when it never comes.
    /// </summary>
    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    /// <summary>
    /// Reads back the slots the loop said were available for one execution group, asserting that the
    /// group is in the request at all.
    /// </summary>
    private static int? LimitFor(TriggerAcquisitionRequest request, string group)
    {
        request.ExecutionLimits.Should().NotBeNull(
            "limits are configured, so every acquisition should carry the ones this pass may use");
        request.ExecutionLimits.TryGetLimit(ExecutionGroupScope.Named(group), out int? limit)
            .Should().BeTrue($"execution group '{group}' is configured, so the request should carry a limit for it");
        return limit;
    }

    /// <summary>
    /// Puts <paramref name="count" /> one-shot jobs into the store, ready to fire immediately.
    /// </summary>
    private async Task<IReadOnlyList<TriggerKey>> GivenScheduledJobs(int count, string executionGroup = null)
    {
        List<TriggerKey> keys = new(count);
        for (int i = 0; i < count; i++)
        {
            IJobDetail job = JobBuilder.Create<LoopTestJob>()
                .WithIdentity($"job{i}", "loop")
                .Build();

            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity($"trigger{i}", "loop")
                .ForJob(job)
                .WithExecutionGroup(executionGroup)
                .StartNow()
                .Build();

            // The store keeps what it is given; working out when a trigger first fires is the
            // scheduler's job, and nothing is acquirable until it has been done.
            trigger.ComputeFirstFireTimeUtc(calendar: null);

            await store.ScheduleJob(job, trigger);
            keys.Add(trigger.Key);
        }

        return keys;
    }

    private sealed class LoopTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Stands in for the database error the loop looks for on a per-trigger firing result.
    /// </summary>
    private sealed class FiredTriggerWriteException : DbException
    {
        public FiredTriggerWriteException() : base("the fired-trigger row could not be written")
        {
        }
    }
}

/// <summary>
/// The real run shell factory with a hook in front of it, so a test can decide that building the shell
/// for a firing fails.
/// </summary>
/// <remarks>
/// <see cref="JobRunShell.Initialize" /> does not throw and the shell is sealed, so the only way the
/// loop's shell-initialization arm is reachable is through the factory - which is also the only place
/// the loop's own documentation says an exception can come from.
/// </remarks>
internal sealed class ScriptedJobRunShellFactory : IJobRunShellFactory
{
    private readonly StdJobRunShellFactory inner = new(NullLogger<JobRunShell>.Instance);
    private int calls;

    /// <summary>
    /// Consulted before each shell is built, with the 1-based number of the call and the firing it is
    /// for. Throw from it to fail creation.
    /// </summary>
    public Action<int, TriggerFiredBundle> OnCreate { get; set; }

    /// <summary>The triggers whose firings were handed a run shell.</summary>
    public CallLog<TriggerKey> Created { get; } = new();

    public void Initialize(IScheduler scheduler) => inner.Initialize(scheduler);

    public JobRunShell CreateJobRunShell(TriggerFiredBundle bndle)
    {
        OnCreate?.Invoke(Interlocked.Increment(ref calls), bndle);
        JobRunShell shell = inner.CreateJobRunShell(bndle);
        Created.Record(bndle.Trigger.Key);
        return shell;
    }
}
