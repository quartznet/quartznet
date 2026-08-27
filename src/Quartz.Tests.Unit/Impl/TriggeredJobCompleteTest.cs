using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;
using Quartz.Tests.Unit.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Walks every <see cref="SchedulerInstruction" /> through <c>TriggeredJobComplete</c> on both stores,
/// from the same table of cases.
/// </summary>
/// <remarks>
/// <para>
/// This is the branch ladder that decides what a finished job leaves behind: whether its trigger is
/// deleted, parked as complete or parked in error, whether the verdict reaches the job's other
/// triggers, and whether the siblings a <see cref="DisallowConcurrentExecutionAttribute" /> job blocked
/// are let go. The two stores implement it independently, so the cases are shared and the assertions
/// are per store: the in-memory store is asked what state it now reports, and the ADO store is watched
/// through its driver delegate.
/// </para>
/// <para>
/// Two instructions are deliberately no-ops here. <see cref="SchedulerInstruction.NoInstruction" />
/// says the firing settled nothing, and <see cref="SchedulerInstruction.ReExecuteJob" /> is handled by
/// the run shell looping before it ever completes the firing, so neither reaches a branch of the
/// ladder. Both still have to release the fired trigger's own bookkeeping, which is why they are in
/// the table rather than left out of it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TriggeredJobCompleteTest
{
    /// <summary>
    /// One instruction and what completing a firing with it should leave behind, in the vocabulary of
    /// each store.
    /// </summary>
    public sealed record CompletionCase(SchedulerInstruction Instruction)
    {
        /// <summary>What an in-memory store reports for the trigger that fired, afterwards.</summary>
        public TriggerState FiredTriggerState { get; init; } = TriggerState.Normal;

        /// <summary>What it reports for the job's other trigger, which never fired.</summary>
        public TriggerState SiblingState { get; init; } = TriggerState.Normal;

        /// <summary>The state an ADO store writes for the fired trigger alone, if any.</summary>
        public StoredTriggerState? TriggerStateWritten { get; init; }

        /// <summary>The state an ADO store writes across every trigger of the job, if any.</summary>
        public StoredTriggerState? JobTriggerStatesWritten { get; init; }

        /// <summary>Whether the trigger row is deleted outright.</summary>
        public bool DeletesTrigger { get; init; }

        public override string ToString() => Instruction.ToString();
    }

    public static IEnumerable<CompletionCase> CompletionCases()
    {
        yield return new CompletionCase(SchedulerInstruction.NoInstruction);
        yield return new CompletionCase(SchedulerInstruction.ReExecuteJob);
        yield return new CompletionCase(SchedulerInstruction.SetTriggerComplete)
        {
            FiredTriggerState = TriggerState.Complete,
            TriggerStateWritten = StoredTriggerState.Complete,
        };
        yield return new CompletionCase(SchedulerInstruction.SetTriggerError)
        {
            FiredTriggerState = TriggerState.Error,
            TriggerStateWritten = StoredTriggerState.Error,
        };
        yield return new CompletionCase(SchedulerInstruction.SetAllJobTriggersComplete)
        {
            FiredTriggerState = TriggerState.Complete,
            SiblingState = TriggerState.Complete,
            JobTriggerStatesWritten = StoredTriggerState.Complete,
        };
        yield return new CompletionCase(SchedulerInstruction.SetAllJobTriggersError)
        {
            FiredTriggerState = TriggerState.Error,
            SiblingState = TriggerState.Error,
            JobTriggerStatesWritten = StoredTriggerState.Error,
        };
        yield return new CompletionCase(SchedulerInstruction.DeleteTrigger)
        {
            FiredTriggerState = TriggerState.None,
            DeletesTrigger = true,
        };
    }

    /// <summary>
    /// Guards the table itself: an instruction added to the enum without a case here would otherwise
    /// silently go untested on both stores.
    /// </summary>
    [Test]
    public void EveryInstructionHasACase()
    {
        CompletionCases().Select(x => x.Instruction).Should().BeEquivalentTo(
            Enum.GetValues<SchedulerInstruction>(),
            "the matrix is only a matrix while it covers every instruction the scheduler can hand a store");
    }

    #region RAMJobStore

    [TestCaseSource(nameof(CompletionCases))]
    public async Task RamStoreLeavesTheJobsTriggersInTheStateTheInstructionAsksFor(CompletionCase testCase)
    {
        RAMJobStore store = TestJobStores.Ram();
        IJobDetail job = CreateJob<CompletionTestJob>();
        IOperableTrigger fired = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        IOperableTrigger sibling = CreateTrigger("sibling", job, DateTimeOffset.UtcNow.AddHours(1));

        IOperableTrigger firing = await GivenAFiredTrigger(store, job, fired, sibling);

        await store.TriggeredJobComplete(firing, job, testCase.Instruction);

        (await store.GetTriggerState(fired.Key)).Should().Be(testCase.FiredTriggerState,
            $"completing a firing with {testCase.Instruction} decides what becomes of the trigger that fired");
        (await store.GetTriggerState(sibling.Key)).Should().Be(testCase.SiblingState,
            $"completing a firing with {testCase.Instruction} decides whether the verdict spreads to the job's other triggers");
        (await store.GetJob(job.Key)).Should().NotBeNull(
            "the job is still referenced by its other trigger, so no instruction may orphan it away");
    }

    [Test]
    public async Task RamStoreDeletesAJobItsLastTriggerLeavesBehind()
    {
        RAMJobStore store = TestJobStores.Ram();
        IJobDetail job = CreateJob<CompletionTestJob>();
        IOperableTrigger only = CreateTrigger("only", job, DateTimeOffset.UtcNow);

        IOperableTrigger firing = await GivenAFiredTrigger(store, job, only);

        await store.TriggeredJobComplete(firing, job, SchedulerInstruction.DeleteTrigger);

        (await store.GetTrigger(only.Key)).Should().BeNull("the instruction asked for the trigger to go");
        (await store.GetJob(job.Key)).Should().BeNull(
            "a job that is not durable has nothing left to keep it once its last trigger is deleted");
    }

    /// <summary>
    /// The other arm of the delete branch. A trigger that has run out of fire times may have been given
    /// new ones by the very job that was running, so the store re-reads what it holds before deleting.
    /// </summary>
    [Test]
    public async Task RamStoreKeepsATriggerTheJobRescheduledWhileItRan()
    {
        RAMJobStore store = TestJobStores.Ram();
        IJobDetail job = CreateJob<CompletionTestJob>();
        IOperableTrigger only = CreateTrigger("only", job, DateTimeOffset.UtcNow);

        IOperableTrigger firing = await GivenAFiredTrigger(store, job, only);

        // What the scheduler holds says the trigger is finished; what the store holds says it is not,
        // which is the shape a job rescheduling its own trigger mid-run leaves behind.
        firing.NextFireTimeUtc = null;

        await store.TriggeredJobComplete(firing, job, SchedulerInstruction.DeleteTrigger);

        (await store.GetTrigger(only.Key)).Should().NotBeNull(
            "the store still has fire times for this trigger, so the delete is cancelled rather than losing a live schedule");
    }

    [Test]
    public async Task RamStoreReleasesTheSiblingsADisallowConcurrentJobBlocked()
    {
        RAMJobStore store = TestJobStores.Ram();
        IJobDetail job = CreateJob<DisallowConcurrentCompletionTestJob>();
        IOperableTrigger fired = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        IOperableTrigger sibling = CreateTrigger("sibling", job, DateTimeOffset.UtcNow.AddHours(1));

        IOperableTrigger firing = await GivenAFiredTrigger(store, job, fired, sibling);

        (await store.GetTriggerState(sibling.Key)).Should().Be(TriggerState.Blocked,
            "firing one trigger of a job that forbids concurrent execution blocks the rest of them");

        await store.TriggeredJobComplete(firing, job, SchedulerInstruction.NoInstruction);

        (await store.GetTriggerState(sibling.Key)).Should().Be(TriggerState.Normal,
            "completion is what lets the blocked siblings go, and it does so even when the instruction itself decides nothing");
    }

    /// <summary>
    /// A trigger that sat blocked past its own fire time has its misfire policy applied as it is let
    /// go, because completion is the first thing that can settle the debt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A blocked trigger is out of the acquisition set and out of the misfire sweep's, so a store that
    /// left the policy to the next acquisition would hand a caller a past-due fire time in the window
    /// between — the divergence #3463 reported and #3471 closed.
    /// </para>
    /// <para>
    /// The contract suite asserts the same thing against both stores, but it runs only in the
    /// integration leg, whose coverage the Sonar gate does not read. This is the in-memory half of it
    /// in the fast suite, on a clock the test owns so that "rescheduled to now" is an exact instant
    /// rather than a window.
    /// </para>
    /// </remarks>
    [Test]
    public async Task RamStoreAppliesTheMisfirePolicyOfATriggerItUnblocks()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 6, 12, 0, 0, TimeSpan.Zero));
        RAMJobStore store = TestJobStores.Ram(timeProvider: clock);
        store.MisfireThreshold = TimeSpan.FromMinutes(1);

        IJobDetail job = CreateJob<DisallowConcurrentCompletionTestJob>();
        IOperableTrigger firing = await GivenABlockingFiring(store, clock, job);

        // FireNow on a one-shot: the missed firing is owed, so the policy hands the trigger back due
        // now rather than due in the past.
        IOperableTrigger blocked = CreateOverdueOneShot("blocked", job, clock, SimpleTriggerMisfireInstruction.FireNow);
        await store.AddTrigger(blocked, replace: false);

        (await store.GetTriggerState(blocked.Key)).Should().Be(TriggerState.Blocked,
            "a trigger added while the job is already running is blocked as it arrives");

        await store.TriggeredJobComplete(firing, job, SchedulerInstruction.NoInstruction);

        (await store.GetTriggerState(blocked.Key)).Should().Be(TriggerState.Normal,
            "the trigger has a firing left to do, so completing the execution that blocked it leaves it waiting");

        IOperableTrigger readBack = await store.GetTrigger(blocked.Key);

        readBack.NextFireTimeUtc.Should().Be(clock.GetUtcNow(),
            "the policy was applied while the completion was unblocking the trigger, not left to the next "
            + "acquisition - which would still be reporting the fire time the trigger missed");
    }

    [Test]
    public async Task RamStoreRemovesAnUnblockedTriggerWithNothingLeftToFire()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 6, 12, 0, 0, TimeSpan.Zero));
        RAMJobStore store = TestJobStores.Ram(timeProvider: clock);
        store.MisfireThreshold = TimeSpan.FromMinutes(1);

        IJobDetail job = CreateJob<DisallowConcurrentCompletionTestJob>();
        IOperableTrigger firing = await GivenABlockingFiring(store, clock, job);

        // Reschedule to the next firing after now, of which a one-shot whose only firing was missed
        // has none: the policy writes the missed firing off and leaves the trigger finished.
        IOperableTrigger blocked = CreateOverdueOneShot("blocked", job, clock, SimpleTriggerMisfireInstruction.NextWithRemainingCount);
        await store.AddTrigger(blocked, replace: false);

        await store.TriggeredJobComplete(firing, job, SchedulerInstruction.NoInstruction);

        (await store.GetTrigger(blocked.Key)).Should().BeNull(
            "a trigger whose policy left it with nothing to fire is finished, and a store that kept it "
            + "would go on handing callers a trigger that can never fire again");
        (await store.GetTriggerState(blocked.Key)).Should().Be(TriggerState.None,
            "a trigger that is gone has no state");
        (await store.GetJob(job.Key)).Should().NotBeNull(
            "the job still has the trigger that was running, so removing the finished one must not take it");
    }

    /// <summary>
    /// Fires one trigger of a job that forbids concurrent execution, leaving the job blocked and the
    /// firing open for the caller to complete.
    /// </summary>
    private static async Task<IOperableTrigger> GivenABlockingFiring(
        RAMJobStore store,
        FakeTimeProvider clock,
        IJobDetail job)
    {
        IOperableTrigger running = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("running", "completion")
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        running.ComputeFirstFireTimeUtc(calendar: null);

        await store.ScheduleJob(job, running);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
        });

        IOperableTrigger firing = acquired.Should().ContainSingle(x => x.Key.Equals(running.Key),
            "the trigger due now is the one whose firing blocks the job").Subject;

        List<TriggerFiredResult> results = await store.TriggersFired([firing]);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the firing has to be committed for the job to count as running");

        return firing;
    }

    /// <summary>
    /// A one-shot trigger whose only firing was an hour ago and which nobody got around to firing.
    /// </summary>
    /// <remarks>
    /// The fire time is written back by hand because <see cref="IOperableTrigger.ComputeFirstFireTimeUtc" />
    /// is what a scheduler would have called before storing it, and a trigger that is overdue is one
    /// whose stored fire time has since gone past.
    /// </remarks>
    private static IOperableTrigger CreateOverdueOneShot(
        string name,
        IJobDetail job,
        FakeTimeProvider clock,
        SimpleTriggerMisfireInstruction misfireInstruction)
    {
        DateTimeOffset missed = clock.GetUtcNow().AddHours(-1);

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, "completion")
            .ForJob(job)
            .StartAt(missed)
            .WithSimpleSchedule(x => x
                .WithRepeatCount(0)
                .WithInterval(TimeSpan.Zero)
                .WithMisfireInstruction(misfireInstruction))
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        trigger.NextFireTimeUtc = missed;
        return trigger;
    }

    #endregion

    #region ADO job store

    [TestCaseSource(nameof(CompletionCases))]
    public async Task AdoStoreWritesTheTriggerStatesTheInstructionAsksFor(CompletionCase testCase)
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        // Durable, so that a deleted trigger does not also drag the job into this test; the job's own
        // fate has a test of its own below.
        IJobDetail job = CreateJob<CompletionTestJob>(durable: true);
        IOperableTrigger trigger = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";

        await store.TriggeredJobComplete(trigger, job, testCase.Instruction);

        if (testCase.TriggerStateWritten is StoredTriggerState triggerState)
        {
            A.CallTo(() => driverDelegate.UpdateTriggerState(
                    A<ConnectionAndTransactionHolder>.Ignored,
                    trigger.Key,
                    triggerState,
                    A<CancellationToken>.Ignored))
                .MustHaveHappenedOnceExactly();
        }
        else
        {
            A.CallTo(() => driverDelegate.UpdateTriggerState(
                    A<ConnectionAndTransactionHolder>.Ignored,
                    A<TriggerKey>.Ignored,
                    A<StoredTriggerState>.Ignored,
                    A<CancellationToken>.Ignored))
                .MustNotHaveHappened();
        }

        if (testCase.JobTriggerStatesWritten is StoredTriggerState jobTriggerState)
        {
            A.CallTo(() => driverDelegate.UpdateTriggerStatesForJob(
                    A<ConnectionAndTransactionHolder>.Ignored,
                    trigger.JobKey,
                    jobTriggerState,
                    A<CancellationToken>.Ignored))
                .MustHaveHappenedOnceExactly();
        }
        else
        {
            A.CallTo(() => driverDelegate.UpdateTriggerStatesForJob(
                    A<ConnectionAndTransactionHolder>.Ignored,
                    A<JobKey>.Ignored,
                    A<StoredTriggerState>.Ignored,
                    A<CancellationToken>.Ignored))
                .MustNotHaveHappened();
        }

        A.CallTo(() => driverDelegate.DeleteTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                trigger.Key,
                A<CancellationToken>.Ignored))
            .MustHaveHappened(testCase.DeletesTrigger ? 1 : 0, Times.Exactly);

        A.CallTo(() => driverDelegate.DeleteFiredTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                trigger.FireInstanceId,
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AdoStoreDeletesAJobItsLastTriggerLeavesBehind()
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        IJobDetail job = CreateJob<CompletionTestJob>();
        IOperableTrigger trigger = CreateTrigger("only", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";

        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.CountTriggersForJob(A<ConnectionAndTransactionHolder>.Ignored, job.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(0));
        A.CallTo(() => driverDelegate.DeleteJobDetail(A<ConnectionAndTransactionHolder>.Ignored, job.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(1));

        await store.TriggeredJobComplete(trigger, job, SchedulerInstruction.DeleteTrigger);

        A.CallTo(() => driverDelegate.DeleteJobDetail(A<ConnectionAndTransactionHolder>.Ignored, job.Key, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The other arm of the delete branch, on the ADO side: the trigger the scheduler holds has run out
    /// of fire times, so the store re-reads the row before deleting it, and finds that the job gave it
    /// new ones while it ran.
    /// </summary>
    [TestCase(true, TestName = "AdoStoreKeepsATriggerTheJobRescheduledWhileItRan")]
    [TestCase(false, TestName = "AdoStoreDeletesATriggerThatIsFinishedInTheDatabaseToo")]
    public async Task AdoStoreRereadsTheRowBeforeDeletingAnExhaustedTrigger(bool rescheduled)
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        IJobDetail job = CreateJob<CompletionTestJob>(durable: true);
        IOperableTrigger trigger = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";
        trigger.NextFireTimeUtc = null;

        A.CallTo(() => driverDelegate.SelectTriggerHeader(A<ConnectionAndTransactionHolder>.Ignored, trigger.Key, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerHeader>(new StoredTriggerHeader(
                trigger.Key,
                job.Key,
                StoredTriggerState.Waiting,
                rescheduled ? DateTimeOffset.UtcNow.AddHours(1) : null,
                AdoConstants.TriggerTypeSimple)));

        await store.TriggeredJobComplete(trigger, job, SchedulerInstruction.DeleteTrigger);

        A.CallTo(() => driverDelegate.DeleteTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                trigger.Key,
                A<CancellationToken>.Ignored))
            .MustHaveHappened(rescheduled ? 0 : 1, Times.Exactly);
    }

    [TestCaseSource(nameof(CompletionCases))]
    public async Task AdoStoreReleasesTheSiblingsADisallowConcurrentJobBlocked(CompletionCase testCase)
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        IJobDetail job = CreateJob<DisallowConcurrentCompletionTestJob>(durable: true);
        IOperableTrigger trigger = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";

        await store.TriggeredJobComplete(trigger, job, testCase.Instruction);

        // Both transitions travel in one call, which is one round trip inside the trigger-access lock
        // rather than two.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesForJobFromOtherState(
                A<ConnectionAndTransactionHolder>.Ignored,
                job.Key,
                A<IReadOnlyList<TriggerStateTransition>>.That.IsSameSequenceAs(
                    new TriggerStateTransition(StoredTriggerState.Blocked, StoredTriggerState.Waiting),
                    new TriggerStateTransition(StoredTriggerState.PausedBlocked, StoredTriggerState.Paused)),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    [TestCaseSource(nameof(CompletionCases))]
    public async Task AdoStoreLeavesTheSiblingsOfAConcurrentJobAlone(CompletionCase testCase)
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        IJobDetail job = CreateJob<CompletionTestJob>(durable: true);
        IOperableTrigger trigger = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";

        await store.TriggeredJobComplete(trigger, job, testCase.Instruction);

        A.CallTo(() => driverDelegate.UpdateTriggerStatesForJobFromOtherState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<JobKey>.Ignored,
                A<IReadOnlyList<TriggerStateTransition>>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// Wires a fresh delegate and signaler into the store, with the reads the completion ladder makes
    /// answered so that nothing it does depends on a dummy.
    /// </summary>
    private static IDriverDelegate GivenADriverDelegate(CompletingAdoJobStore store)
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        store.DirectDelegate = driverDelegate;
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();

        // The unblocking fan-out re-checks the job's triggers for misfires; none of these tests has a
        // trigger to find there.
        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<JobKey>.Ignored,
                A<StoredTriggerState>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<TriggerKey>>(new List<TriggerKey>()));

        return driverDelegate;
    }

    /// <summary>
    /// The ADO harness from <see cref="AdoJobStoreBaseTest" />, with the lock wrapper actually running
    /// the work it is given. The harness answers <c>default</c> without calling back, which is enough
    /// for the tests that drive the protected members directly, but not for one that goes in through
    /// the public <c>TriggeredJobComplete</c>.
    /// </summary>
    /// <summary>
    /// Unblocking a job's triggers used to cost a read of the job's triggers, a read of each trigger's
    /// state, and a reload of every trigger that turned out to be waiting. It is now the keys in the one
    /// state that matters, then one bulk load of those.
    /// </summary>
    [Test]
    public async Task AdoStoreChecksTheUnblockedTriggersForMisfiresWithoutAReadPerTrigger()
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        IJobDetail job = CreateJob<DisallowConcurrentCompletionTestJob>(durable: true);
        IOperableTrigger trigger = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";

        List<IOperableTrigger> siblings =
        [
            CreateTrigger("s1", job, DateTimeOffset.UtcNow.AddHours(1)),
            CreateTrigger("s2", job, DateTimeOffset.UtcNow.AddHours(2)),
            CreateTrigger("s3", job, DateTimeOffset.UtcNow.AddHours(3)),
        ];

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(
                A<ConnectionAndTransactionHolder>.Ignored,
                job.Key,
                StoredTriggerState.Waiting,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<TriggerKey>>(siblings.Select(x => x.Key).ToList()));

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyCollection<TriggerKey>>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>(siblings));

        await store.TriggeredJobComplete(trigger, job, SchedulerInstruction.NoInstruction);

        // Three triggers, still one read.
        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyCollection<TriggerKey>>.That.IsSameSequenceAs(siblings.Select(x => x.Key)),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.SelectTriggerState(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<TriggerKey>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        A.CallTo(() => driverDelegate.SelectTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<TriggerKey>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();

        // None of these has passed its fire time, so nothing is written for them either.
        A.CallTo(() => driverDelegate.UpdateMisfiredTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyList<MisfiredTriggerUpdate>>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AdoStoreAppliesTheMisfirePolicyOfEveryUnblockedTriggerInOneWrite()
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        IJobDetail job = CreateJob<DisallowConcurrentCompletionTestJob>(durable: true);
        IOperableTrigger trigger = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";

        // Two triggers that sat blocked long enough to miss their turn while the job ran.
        List<IOperableTrigger> missed =
        [
            CreateTrigger("m1", job, DateTimeOffset.UtcNow.AddHours(-2)),
            CreateTrigger("m2", job, DateTimeOffset.UtcNow.AddHours(-3)),
        ];

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(
                A<ConnectionAndTransactionHolder>.Ignored,
                job.Key,
                StoredTriggerState.Waiting,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<TriggerKey>>(missed.Select(x => x.Key).ToList()));

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyCollection<TriggerKey>>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>(missed));

        await store.TriggeredJobComplete(trigger, job, SchedulerInstruction.NoInstruction);

        // Both misfires belong in the same write.
        A.CallTo(() => driverDelegate.UpdateMisfiredTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyList<MisfiredTriggerUpdate>>.That.Matches(x => x.Count == 2),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.UpdateMisfiredTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IOperableTrigger>.Ignored,
                A<StoredTriggerState>.Ignored,
                A<DateTimeOffset?>.Ignored,
                A<CancellationToken>.Ignored))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// A trigger that runs out of fire times while it was blocked is stored COMPLETE by the misfire
    /// policy, and a COMPLETE row would linger where callers expect the trigger to be gone.
    /// </summary>
    [Test]
    public async Task AdoStoreDeletesAnUnblockedTriggerThatMisfiredItsWayToCompletion()
    {
        CompletingAdoJobStore store = new();
        IDriverDelegate driverDelegate = GivenADriverDelegate(store);

        IJobDetail job = CreateJob<DisallowConcurrentCompletionTestJob>(durable: true);
        IOperableTrigger trigger = CreateTrigger("fired", job, DateTimeOffset.UtcNow);
        trigger.FireInstanceId = "fire-1";

        // One fire, an hour ago, and its misfire instruction throws the missed fire away.
        SimpleTriggerImpl onceOnly = new()
        {
            Key = new TriggerKey("once", "g"),
            JobKey = job.Key,
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            RepeatCount = 0,
            RepeatInterval = TimeSpan.Zero,
        };
        onceOnly.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;
        onceOnly.NextFireTimeUtc = DateTimeOffset.UtcNow.AddHours(-1);

        A.CallTo(() => driverDelegate.SelectTriggerKeysForJob(
                A<ConnectionAndTransactionHolder>.Ignored,
                job.Key,
                StoredTriggerState.Waiting,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<TriggerKey>>([onceOnly.Key]));

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyCollection<TriggerKey>>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>([onceOnly]));

        await store.TriggeredJobComplete(trigger, job, SchedulerInstruction.NoInstruction);

        A.CallTo(() => driverDelegate.UpdateMisfiredTriggers(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<IReadOnlyList<MisfiredTriggerUpdate>>.That.Matches(x => x.Count == 1 && x[0].NewState == StoredTriggerState.Complete),
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => driverDelegate.DeleteTrigger(
                A<ConnectionAndTransactionHolder>.Ignored,
                onceOnly.Key,
                A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    private sealed class CompletingAdoJobStore : AdoJobStoreBaseTest.TestAdoJobStoreBase
    {
        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }
    }

    #endregion

    /// <summary>
    /// Puts <paramref name="triggers" /> in the store and takes the first one all the way through
    /// acquisition and firing, which is the only state from which a completion means anything. Hands
    /// back the trigger the store itself produced, since that is the one carrying the fire instance the
    /// completion has to release — the caller's own object never learns of it.
    /// </summary>
    private static async Task<IOperableTrigger> GivenAFiredTrigger(
        RAMJobStore store,
        IJobDetail job,
        params IOperableTrigger[] triggers)
    {
        await store.ScheduleJob(job, triggers[0]);
        for (int i = 1; i < triggers.Length; i++)
        {
            await store.AddTrigger(triggers[i], replace: false);
        }

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1,
        });

        acquired.Select(x => x.Key).Should().Equal([triggers[0].Key],
            "the trigger due now is the one the completion under test belongs to");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the firing has to be committed before completing it says anything");

        return acquired[0];
    }

    private static IJobDetail CreateJob<TJob>(bool durable = false) where TJob : IJob
    {
        return JobBuilder.Create<TJob>()
            .WithIdentity("job", "completion")
            .StoreDurably(durable)
            .Build();
    }

    private static IOperableTrigger CreateTrigger(string name, IJobDetail job, DateTimeOffset startAt)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "completion")
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        // Job stores keep what they are given; working out when a trigger first fires is the
        // scheduler's job, and nothing is acquirable until it has been done.
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private sealed class CompletionTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    private sealed class DisallowConcurrentCompletionTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
