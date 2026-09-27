using System.Data.Common;

using FakeItEasy;
using FakeItEasy.Core;

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

    /// <summary>
    /// How long a test watches a paused loop for something it must not do. A loop that does it does so
    /// within milliseconds of being let go; this is a margin that no loaded agent eats.
    /// </summary>
    private static readonly TimeSpan pausedObservation = TimeSpan.FromSeconds(3);

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
        A.CallTo(() => threadPool.TryRunWithState(A<Func<object, ValueTask>>.Ignored, A<object>.Ignored, A<CancellationToken>.Ignored))
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

        A.CallTo(() => threadPool.TryRunWithState(A<Func<object, ValueTask>>.Ignored, A<object>.Ignored, A<CancellationToken>.Ignored))
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

        A.CallTo(() => threadPool.TryRunWithState(A<Func<object, ValueTask>>.Ignored, A<object>.Ignored, A<CancellationToken>.Ignored))
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

    /// <summary>
    /// The other half of the ledger: what is taken when a firing is dispatched has to come back when it
    /// ends, and since #3802 it is the run shell's own outermost finally that returns it rather than a
    /// lambda the loop wrapped around the call.
    /// </summary>
    [Test]
    public async Task AFiringThatHasRunGivesItsExecutionGroupSlotBack()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create().ForGroup("batch", 2).Build());
        await GivenScheduledJobs(1, executionGroup: "batch");

        // Runs the work rather than merely accepting it, which is the difference from the test above:
        // the shell reaches its finally before the loop asks for its next batch.
        A.CallTo(() => threadPool.TryRunWithState(A<Func<object, ValueTask>>.Ignored, A<object>.Ignored, A<CancellationToken>.Ignored))
            .ReturnsLazily(RunTheWork);

        StartLoop();

        await ShouldObserve(store.Completions.Reaches(1),
            "the run shell completes the firing it ran");
        await ShouldObserve(store.Acquisitions.Reaches(2),
            "the loop acquires again as soon as it has dispatched a batch");

        LimitFor(store.Acquisitions.Entries[1], "batch").Should().Be(2,
            "the firing finished before the next acquisition, so the slot it held is back - nothing else gives it back now that the loop no longer wraps the shell in a lambda");

        static async ValueTask<bool> RunTheWork(IFakeObjectCall call)
        {
            Func<object, ValueTask> action = (Func<object, ValueTask>) call.Arguments[0];
            await action(call.Arguments[1]).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// The ungrouped bucket's ledger entry is resident rather than removed at zero, and a resident zero
    /// has to be invisible to the limits every acquisition is given (#3802).
    /// </summary>
    [Test]
    public async Task TheUngroupedBucketsResidentZeroChangesNoLimit()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create()
            .ForDefaultGroup(2)
            .ForGroup("batch", 2)
            .Build());
        await GivenScheduledJobs(1, executionGroup: "batch");

        StartLoop();

        await ShouldObserve(store.Acquisitions.Reaches(2),
            "the loop acquires again as soon as it has dispatched a batch");

        TriggerAcquisitionRequest second = store.Acquisitions.Entries[1];
        second.ExecutionLimits.TryGetLimit(ExecutionGroupScope.Default, out int? ungrouped)
            .Should().BeTrue("the default group is configured, so its limit travels with every request");
        ungrouped.Should().Be(2,
            "nothing ungrouped is in flight, so the whole quota is on offer - the resident entry the loop keeps for that bucket counts zero and must subtract nothing");
        LimitFor(second, "batch").Should().Be(1,
            "the named group's firing is in flight and is still subtracted");
    }

    /// <summary>
    /// The mirror image, and the bug a cluster-wide ceiling invites: a cluster-scoped limit must reach
    /// the store as configured.
    /// </summary>
    /// <remarks>
    /// This node's dispatched firing is already a reservation in the store's own ledger — a
    /// FIRED_TRIGGERS row for the ADO store — and the store subtracts that when it builds the ledger for
    /// the acquisition. Taking it off here as well charges the group twice and halves the quota on the
    /// busiest node, which is exactly the node that would notice least: with one node the store's count
    /// and this loop's count are the same firings, so nothing single-node ever disagrees.
    /// </remarks>
    [Test]
    public async Task DispatchedWorkIsNotSubtractedFromAClusterScopedLimit()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 2, ExecutionLimitScope.Cluster)
            .Build());
        await GivenScheduledJobs(1, executionGroup: "batch");

        StartLoop();

        await ShouldObserve(store.Acquisitions.Reaches(2),
            "the loop acquires again as soon as it has dispatched a batch");

        LimitFor(store.Acquisitions.Entries[0], "batch").Should().Be(2,
            "nothing is in flight yet, so the whole quota is on offer");
        LimitFor(store.Acquisitions.Entries[1], "batch").Should().Be(2,
            "the firing in flight is already a reservation the store counts, so subtracting it here as well would leave the group a slot short of its own quota");

        store.Acquisitions.Entries[1].ExecutionLimits.Groups.Should().ContainSingle()
            .Which.Scope.Should().Be(ExecutionLimitScope.Cluster,
                "the scope travels with the remaining-capacity snapshot, or the store could not tell which limits it still has to lower");
    }

    /// <summary>
    /// One set of limits can hold both kinds, and each is treated on its own terms.
    /// </summary>
    [Test]
    public async Task NodeScopedAndClusterScopedLimitsAreSubtractedIndependently()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 2)
            .ForGroup("tenant", 2, ExecutionLimitScope.Cluster)
            .Build());
        await GivenScheduledJobs(1, executionGroup: "batch");
        await GivenScheduledJobs(1, executionGroup: "tenant", namePrefix: "tenant");

        StartLoop();

        await ShouldObserve(store.Acquisitions.Reaches(2),
            "the loop acquires again as soon as it has dispatched a batch");

        TriggerAcquisitionRequest second = store.Acquisitions.Entries[1];
        LimitFor(second, "batch").Should().Be(1,
            "the node-scoped group has one of this node's firings in flight");
        LimitFor(second, "tenant").Should().Be(2,
            "the cluster-scoped group's firing is the store's to count, not this loop's");
    }

    /// <summary>
    /// A pause signals a scheduling change so the loop leaves whatever it is doing, and the round the
    /// loop is starting drains that signal. A pause landing between the loop's pause check and the
    /// drain was lost, and the paused loop went on to ask the store for triggers.
    /// </summary>
    /// <remarks>
    /// The thread pool is asked for free threads between those two points, so that is where the test
    /// pauses the loop. A loop that loses the pause asks the store within milliseconds; the seconds the
    /// test gives it are margin, not a measurement.
    /// </remarks>
    [Test]
    public async Task APauseThatLandsAsARoundStartsStopsTheRound()
    {
        int threadRequests = 0;
        A.CallTo(() => threadPool.WaitForAvailableThreads(A<CancellationToken>.Ignored))
            .ReturnsLazily(() =>
            {
                if (Interlocked.Increment(ref threadRequests) == 2)
                {
                    thread.TogglePause(pause: true);
                }

                return new ValueTask<int>(AvailableThreads);
            });

        StartLoop();
        await ShouldObserve(store.Acquisitions.Reaches(1), "the first round asks the store");

        thread.SignalSchedulingChange(candidateNewNextFireTimeUtc: null);

        Task secondRound = store.Acquisitions.Reaches(2);
        (await Task.WhenAny(secondRound, Task.Delay(pausedObservation))).Should().NotBeSameAs(secondRound,
            "the loop was paused as its next round started, so a paused loop must not ask the store for triggers");

        thread.TogglePause(pause: false);
        await ShouldObserve(secondRound, "resumed, the loop asks the store again, so it was paused rather than stuck");
    }

    /// <summary>
    /// What losing the pause cost a deployment: <c>Standby()</c> landing while the loop waited for a free
    /// worker let the round that followed fire a trigger that was due, after <c>Standby()</c> had
    /// returned.
    /// </summary>
    [Test]
    public async Task AStandbyThatLandsWhileTheLoopWaitsForAWorkerFiresNothing()
    {
        await GivenScheduledJobs(1);

        int threadRequests = 0;
        A.CallTo(() => threadPool.WaitForAvailableThreads(A<CancellationToken>.Ignored))
            .ReturnsLazily(() =>
            {
                // Past the loop's pause check and short of its drain, which is where a saturated
                // scheduler waits for a worker to come free.
                if (Interlocked.Increment(ref threadRequests) == 1)
                {
                    thread.TogglePause(pause: true);
                }

                return new ValueTask<int>(AvailableThreads);
            });

        StartLoop();

        Task fired = shellFactory.Created.Reaches(1);
        (await Task.WhenAny(fired, Task.Delay(pausedObservation))).Should().NotBeSameAs(fired,
            "the scheduler was in standby before the round acquired anything, so the due trigger must wait for it to start again");
        store.Acquisitions.Count.Should().Be(0, "a paused loop does not ask the store for triggers");

        thread.TogglePause(pause: false);
        await ShouldObserve(fired, "started again, the scheduler fires the trigger that was due");
    }

    /// <summary>
    /// Signals that arrive while the loop is busy wait for it together, and what it is told when it
    /// looks is the earliest of their candidates. A later one used to replace an earlier one — the
    /// sentinel a pause sends included.
    /// </summary>
    /// <remarks>
    /// The loop is held in its store call, after its round has drained the signal, so every signal the
    /// test sends is one it has not read yet.
    /// </remarks>
    [Test]
    public async Task SignalsTheLoopHasNotReadKeepTheEarliestCandidate()
    {
        TaskCompletionSource storeCallReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnAcquireNextTriggers = async (call, _, callThrough) =>
        {
            if (call == 2)
            {
                await storeCallReleased.Task;
            }

            return await callThrough();
        };

        try
        {
            StartLoop();
            await ShouldObserve(store.Acquisitions.Reaches(1), "the first round asks the store");
            thread.SignalSchedulingChange(candidateNewNextFireTimeUtc: null);
            await ShouldObserve(store.Acquisitions.Reaches(2), "a signal starts a round, which the store holds");

            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            thread.SignalSchedulingChange(now.AddMinutes(1));
            thread.SignalSchedulingChange(now.AddMinutes(5));
            thread.GetSignaledNextFireTimeUtc().Should().Be(now.AddMinutes(1),
                "a later schedule must not hide an earlier one the loop has yet to act on");

            thread.SignalSchedulingChange(SchedulerConstants.SchedulingSignalDateTime);
            thread.SignalSchedulingChange(now.AddMinutes(2));
            thread.GetSignaledNextFireTimeUtc().Should().Be(SchedulerConstants.SchedulingSignalDateTime,
                "nor may it hide the sentinel a pause sends, which is what makes the loop let go of what it holds");

            thread.SignalSchedulingChange(candidateNewNextFireTimeUtc: null);
            thread.SignalSchedulingChange(now.AddMinutes(3));
            thread.GetSignaledNextFireTimeUtc().Should().BeNull(
                "a change that names no time could be about anything, which is earlier than any time");
        }
        finally
        {
            storeCallReleased.TrySetResult();
        }
    }

    /// <summary>
    /// What the overwrite cost a deployment: a trigger due now, scheduled just before one due tomorrow,
    /// was passed over for the trigger the loop was already holding, and fired only once that one had.
    /// </summary>
    /// <remarks>
    /// The loop holds its trigger in the store call that acquired it while both schedules are made, so
    /// neither signal has been read when it looks; that is the order two quick schedule calls reach it in.
    /// </remarks>
    [Test]
    public async Task ATriggerDueNowIsNotPassedOverBecauseALaterOneWasScheduledAfterIt()
    {
        resources.IdleWaitTime = TimeSpan.FromSeconds(10);
        await GivenScheduledJob("held", TimeProvider.System.GetUtcNow().AddSeconds(3));
        TaskCompletionSource release = HoldTheFirstAcquisition(out Task acquired);

        StartLoop();
        await ShouldObserve(acquired, "the loop acquires the trigger it will hold");

        IOperableTrigger urgent = await GivenScheduledJob("urgent", TimeProvider.System.GetUtcNow());
        thread.SignalSchedulingChange(urgent.NextFireTimeUtc);
        IOperableTrigger tomorrow = await GivenScheduledJob("tomorrow", TimeProvider.System.GetUtcNow().AddDays(1));
        thread.SignalSchedulingChange(tomorrow.NextFireTimeUtc);
        release.SetResult();

        await ShouldObserve(shellFactory.Created.Reaches(1), "the loop fires something");
        shellFactory.Created.Entries[0].Name.Should().Be("urgent",
            "the trigger due now is earlier than the one the loop holds, so it fires first rather than three seconds later");
    }

    /// <summary>
    /// What the overwrite cost a deployment in standby: a schedule made just after <c>Standby()</c>
    /// replaced the pause's signal before the loop read it, and the loop fired the trigger it was
    /// holding while the scheduler was in standby.
    /// </summary>
    [Test]
    public async Task AScheduleMadeJustAfterStandbyDoesNotLetTheHeldTriggerFire()
    {
        resources.IdleWaitTime = TimeSpan.FromSeconds(10);
        await GivenScheduledJob("held", TimeProvider.System.GetUtcNow().AddSeconds(2));
        TaskCompletionSource release = HoldTheFirstAcquisition(out Task acquired);

        StartLoop();
        await ShouldObserve(acquired, "the loop acquires the trigger it will hold");

        thread.TogglePause(pause: true);
        IOperableTrigger later = await GivenScheduledJob("later", TimeProvider.System.GetUtcNow().AddHours(1));
        thread.SignalSchedulingChange(later.NextFireTimeUtc);
        release.SetResult();

        await ShouldObserve(store.Releases.Reaches(1),
            "the scheduler is in standby, so the loop has to hand back the trigger it was holding");
        store.Releases.Entries.Should().Equal([new TriggerKey("held", "loop")]);
        shellFactory.Created.Count.Should().Be(0, "nothing fires while the scheduler is in standby");
    }

    /// <summary>
    /// Makes the loop's first acquisition wait, once the store has answered it, until the returned source
    /// is completed; <paramref name="acquired" /> completes when it has got that far.
    /// </summary>
    private TaskCompletionSource HoldTheFirstAcquisition(out Task acquired)
    {
        TaskCompletionSource answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnAcquireNextTriggers = async (call, _, callThrough) =>
        {
            List<IOperableTrigger> result = await callThrough();
            if (call == 1)
            {
                answered.TrySetResult();
                await release.Task;
            }

            return result;
        };

        acquired = answered.Task;
        return release;
    }

    private async Task<IOperableTrigger> GivenScheduledJob(string name, DateTimeOffset fireTimeUtc)
    {
        IJobDetail job = JobBuilder.Create<LoopTestJob>().WithIdentity(name, "loop").Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "loop")
            .ForJob(job)
            .StartAt(fireTimeUtc)
            .Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);

        await store.ScheduleJob(job, trigger);
        return trigger;
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
    private async Task<IReadOnlyList<TriggerKey>> GivenScheduledJobs(int count, string executionGroup = null, string namePrefix = "")
    {
        List<TriggerKey> keys = new(count);
        for (int i = 0; i < count; i++)
        {
            IJobDetail job = JobBuilder.Create<LoopTestJob>()
                .WithIdentity($"{namePrefix}job{i}", "loop")
                .Build();

            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity($"{namePrefix}trigger{i}", "loop")
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
