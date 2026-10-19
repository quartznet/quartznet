#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Data;
using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Which firing rounds take <c>TRIGGER_ACCESS</c> and which take none (#4067), what a round that takes
/// none does differently, and what it does when it finds it was wrong.
/// </summary>
/// <remarks>
/// <para>
/// The delegate is faked, so a statement is a call to a delegate member and a transaction is a
/// connection the store opened; the lock handler is the in-process one behind a counter, so a lock is a
/// lock the store really asked for. What the statements do to rows is <c>LockFreeRoundGuardsSqliteTest</c>'s
/// and the integration tests' to prove.
/// </para>
/// <para>
/// A candidate here carries what the shipped delegates read — the serial flag, the fire time, the overlap
/// policy — unless a test is about one that does not.
/// </para>
/// </remarks>
public sealed class LockFreeRoundTest
{
    /// <summary>Log events <c>AdoJobStoreLog.RoundEscalatedToLock</c> and <c>AdoJobStoreLog.RoundRolledBackForLock</c>.</summary>
    private const int RoundEscalatedToLock = 3057;
    private const int RoundRolledBackForLock = 3058;

    /// <summary>Log event <c>AdoJobStoreLog.RoundsLockedForContention</c>.</summary>
    private const int RoundsLockedForContention = 3060;

    /// <summary>Log events <c>AdoJobStoreLog.RoundFireHandedToLock</c> and <c>AdoJobStoreLog.RoundFireWithoutLockFailed</c>.</summary>
    private const int RoundFireHandedToLock = 3062;
    private const int RoundFireWithoutLockFailed = 3063;

    private const string TriggerAccess = "TRIGGER_ACCESS";

    /// <summary>Safely in the past, so that every candidate is due.</summary>
    private static readonly DateTimeOffset FireTime = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly JobKey jobKey = new("j1", "jg1");

    private IDriverDelegate driverDelegate;
    private ISchedulerSignaler signaler;
    private RecordingLoggerProvider logs;
    private ILoggerFactory loggerFactory;
    private CountingLockHandler locks;
    private CountingStore store;

    [SetUp]
    public void SetUp()
    {
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        locks = new CountingLockHandler();
        signaler = A.Fake<ISchedulerSignaler>();
        driverDelegate = A.Fake<IDriverDelegate>();

        // The shipped delegates' answer; the test about one that says otherwise says so itself.
        A.CallTo(() => driverDelegate.SupportsFireOnAcquire).Returns(true);

        // Every candidate wins its compare-and-swap, one at a time or together.
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => driverDelegate.ApplyTriggersFired(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerFiredUpdate>>._, A<CancellationToken>._))
            .CallsBaseMethod();

        // Every claimed trigger reads back ACQUIRED, as a claim in this transaction leaves it.
        A.CallTo(() => driverDelegate.SelectStoredTriggerHeaders(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<StoredTriggerHeader>>(
                    keys.Select(key => new StoredTriggerHeader(key, jobKey, StoredTriggerState.Acquired, FireTime, AdoConstants.TriggerTypeSimple)).ToList()));

        GivenTheJob(ConcurrentJob());
        store = NewStore();
    }

    [TearDown]
    public void TearDown()
    {
        loggerFactory.Dispose();
        logs.Dispose();
    }

    /// <summary>
    /// A round whose every candidate may run beside itself takes no lock, claims before it reads, and
    /// never asks the fired-trigger table: there is no serial job in it to ask about.
    /// </summary>
    [Test]
    public async Task ARoundOfConcurrentAllowedWorkTakesNoLockAndClaimsBeforeItReads()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"), Candidate("t3"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 3);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t2", "t3"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        round.LockName.Should().Be("none", "the round is what the acquisition metric is tagged with");
        round.LockReason.Should().BeNull();
        locks.Acquired.Should().BeEmpty("nothing in the round needed TRIGGER_ACCESS");
        store.Transactions.Should().Be(1);

        // Claimed first, by the fire time the candidate read carried, and read in full after: the read is
        // of rows this transaction holds, so what the fire writes over is what it read.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<TriggerClaim>>.That.Matches(x => x.Count == 3 && x.All(claim => claim.NextFireTimeUtc == FireTime)),
                StoredTriggerState.Acquired,
                StoredTriggerState.Waiting,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => driverDelegate.SelectTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());

        A.CallTo(() => driverDelegate.IsJobCurrentlyExecuting(A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>.That.Matches(x => x.FiredOnAcquire), A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    /// <summary>
    /// What the store's configuration, its cluster and the request decide before anything is read: each
    /// of these locks every round, and says why.
    /// </summary>
    [TestCase("option_off")]
    [TestCase("acquire_within_lock")]
    [TestCase("isolation_level")]
    [TestCase("lock_all_operations")]
    [TestCase("delegate")]
    [TestCase("older_peer")]
    [TestCase("cluster_limit")]
    public async Task ARoundIsLockedBeforeItsFirstReadWhenTheStoreOrTheRequestSaysSo(string reason)
    {
        TriggerAcquisitionRequest request = Request(maxCount: 2);
        switch (reason)
        {
            case "option_off":
                store = NewStore(configure: options => options.LockFreeFiringRounds = false);
                break;
            case "acquire_within_lock":
                store = NewStore(configure: options => options.AcquireTriggersWithinLock = true);
                break;
            case "isolation_level":
                store = NewStore(configure: options => options.TransactionIsolationLevel = IsolationLevel.Serializable);
                break;
            case "lock_all_operations":
                store.LockAllOperations = true;
                break;
            case "delegate":
                A.CallTo(() => driverDelegate.SupportsFireOnAcquire).CallsBaseMethod();
                break;
            case "older_peer":
                // Clustered, and no check-in has read the state table yet.
                store = NewStore(clustered: true);
                break;
            case "cluster_limit":
                request = Request(maxCount: 2, limits: ExecutionLimitsBuilder.Create().ForGroup("batch", 2, ExecutionLimitScope.Cluster).Build());
                break;
        }

        GivenCandidates();

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(request);

        round.LockName.Should().Be(TriggerAccess);
        round.LockReason.Should().Be(reason);
        locks.Acquired.Should().Equal([SchedulerLock.TriggerAccess]);
        store.Transactions.Should().Be(1, "a round that knows before its first read does not roll back to find out");
    }

    /// <summary>
    /// A round of one that takes the lock takes it as 4.5 did, whatever locked it before its read: acquired
    /// without the lock and left for the scheduler to fire under it, in a transaction of its own. Folded
    /// under the lock it held TRIGGER_ACCESS through its read, its claim and its fire, and two SQL Server
    /// nodes acquiring one trigger at a time ran seconds behind their schedule. The tag describes the round
    /// — the acquisition and the fire it hands on — so every such round reads as locked, as it is.
    /// </summary>
    [TestCase("option_off")]
    [TestCase("isolation_level")]
    [TestCase("lock_all_operations")]
    [TestCase("delegate")]
    [TestCase("older_peer")]
    public async Task ALockedRoundOfOneIsLeftForTheSchedulerToFireAsIn45(string reason)
    {
        switch (reason)
        {
            case "option_off":
                store = NewStore(configure: options => options.LockFreeFiringRounds = false);
                break;
            case "isolation_level":
                store = NewStore(configure: options => options.TransactionIsolationLevel = IsolationLevel.Serializable);
                break;
            case "lock_all_operations":
                store.LockAllOperations = true;
                break;
            case "delegate":
                A.CallTo(() => driverDelegate.SupportsFireOnAcquire).CallsBaseMethod();
                break;
            case "older_peer":
                store = NewStore(clustered: true);
                break;
        }

        GivenCandidates(Candidate("t1"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);

        round.Due.Should().BeEmpty("4.5 fired a round of one in the scheduler's TriggersFired, not in the acquisition");
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockName.Should().Be(TriggerAccess, "the scheduler's TriggersFired takes it for the fire");
        round.LockReason.Should().Be(reason);
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// A round of one that counts against a cluster-scoped limit stays folded under the lock: the count is
    /// exact only when the acquisition holds it, which the multi-tenancy page promises.
    /// </summary>
    [Test]
    public async Task ARoundOfOneThatCountsAgainstAClusterScopedLimitStaysFolded()
    {
        GivenCandidates(Candidate("t1"));

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(
            Request(maxCount: 1, limits: ExecutionLimitsBuilder.Create().ForGroup("batch", 2, ExecutionLimitScope.Cluster).Build()));

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        round.LockReason.Should().Be("cluster_limit");
        locks.Acquired.Should().Equal([SchedulerLock.TriggerAccess], "the acquisition counts the quota under the lock");
    }

    /// <summary>
    /// <c>AcquireTriggersWithinLock</c> asks for the acquisition under the lock, which 4.5 folded too.
    /// </summary>
    [Test]
    public async Task ARoundOfOneAcquiredWithinTheLockStaysFolded()
    {
        store = NewStore(configure: options => options.AcquireTriggersWithinLock = true);
        GivenCandidates(Candidate("t1"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockReason.Should().Be("acquire_within_lock");
        locks.Acquired.Should().Equal([SchedulerLock.TriggerAccess]);
    }

    /// <summary>
    /// A round of one that escalates is retried as 4.5's round, not folded under the lock; the round after
    /// it starts in 4.5's shape too, and one that finds nothing needing the lock lets the next go without.
    /// </summary>
    [Test]
    public async Task AnEscalatedRoundOfOneAndTheRoundAfterItAreLeftForTheSchedulerAsIn45()
    {
        GivenCandidates(Candidate("t1", serial: true));

        TriggerAcquisitionResult escalated = await AcquireAndFire(maxCount: 1);

        escalated.Due.Should().BeEmpty();
        escalated.Pending.Select(x => x.Key.Name).Should().Equal(["t1"]);
        escalated.LockReason.Should().Be("serial_job");
        store.Transactions.Should().Be(2, "the attempt without the lock, rolled back, and 4.5's acquisition");

        GivenCandidates(Candidate("t2"));
        TriggerAcquisitionResult hysteresis = await AcquireAndFire(maxCount: 1);
        hysteresis.Pending.Select(x => x.Key.Name).Should().Equal(["t2"]);
        hysteresis.LockReason.Should().Be("hysteresis");

        TriggerAcquisitionResult free = await AcquireAndFire(maxCount: 1);
        free.Due.Select(x => x.Key.Name).Should().Equal(["t2"], "the round before found nothing that needed the lock");
        free.LockName.Should().Be("none");
    }

    /// <summary>
    /// With the option on, a round of one is a round like any other: fired in the round, and with no lock
    /// when nothing in it needs one. The two-transaction shape existed because the round could not take
    /// the lock for the fire without taking it for every acquisition; a round that fires without the lock
    /// has nothing to take.
    /// </summary>
    [Test]
    public async Task ARoundOfOneIsFiredInTheRoundWithoutTheLock()
    {
        GivenCandidates(Candidate("t1"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        round.Pending.Should().BeEmpty();
        round.LockName.Should().Be("none");
        locks.Acquired.Should().BeEmpty();
        store.Transactions.Should().Be(1);
    }

    /// <summary>
    /// A candidate whose job disallows concurrent execution needs the fired-trigger table kept still, which
    /// only the lock does. The round finds it in its first read, before any write, and runs again locked.
    /// </summary>
    [Test]
    public async Task ASerialCandidateSendsTheRoundToTheLockBeforeItsFirstWrite()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2", serial: true));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t2"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        round.LockName.Should().Be(TriggerAccess);
        round.LockReason.Should().Be("serial_job");
        locks.Acquired.Should().Equal([SchedulerLock.TriggerAccess]);
        store.Transactions.Should().Be(2, "the attempt without the lock, rolled back, and the one under it");

        // Nothing of the first attempt stands, and nothing of it was written: the claim is the locked
        // attempt's alone.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundEscalatedToLock)
            .Which.Should().Match<LogEntry>(x => x.Level == LogLevel.Debug && x.Message.Contains("t2"),
                "an escalation is the rule working, not a problem, and names the candidate that asked for it");
    }

    /// <summary>
    /// An overlap policy the fire decides from the trigger's running firings needs those kept still, which
    /// only the lock does; the two that do not decide anything at the fire need nothing.
    /// </summary>
    [TestCase(OverlapPolicy.Skip, TriggerAccess, "overlap_policy")]
    [TestCase(OverlapPolicy.BufferOne, TriggerAccess, "overlap_policy")]
    [TestCase(OverlapPolicy.CancelPrevious, TriggerAccess, "overlap_policy")]
    [TestCase(OverlapPolicy.AllowAll, "none", null)]
    [TestCase(OverlapPolicy.Default, "none", null)]
    public async Task ACandidateWhosePolicyTheFireDecidesSendsTheRoundToTheLock(OverlapPolicy policy, string lockName, string lockReason)
    {
        GivenCandidates(Candidate("t1", policy: policy));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockName.Should().Be(lockName);
        round.LockReason.Should().Be(lockReason);
    }

    /// <summary>
    /// A trigger that measures its interval from completion is held while it runs, and its fire stores it
    /// held against the paused groups, which only the lock keeps still (#4140).
    /// </summary>
    [Test]
    public async Task ACandidateThatMeasuresFromCompletionSendsTheRoundToTheLock()
    {
        GivenCandidates(Candidate("t1", fixedDelay: true));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockName.Should().Be(TriggerAccess);
        round.LockReason.Should().Be("overlap_policy", "the hold is the one a BufferOne firing takes, and is reported as one");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundEscalatedToLock,
            "the candidate read said so, so the round escalated before it claimed anything");
        logs.Entries.Should().NotContain(x => x.EventId.Id == RoundRolledBackForLock);
    }

    /// <summary>
    /// A delegate that did not read whether a trigger measures from completion answers "no", and the fire
    /// that finds the trigger does rolls the round back to the lock rather than store the hold without it.
    /// </summary>
    [Test]
    public async Task ATriggerFoundMeasuringFromCompletionAfterTheClaimRollsTheRoundBack()
    {
        GivenCandidates(Candidate("t1"));
        GivenTheTriggersReadBack(CreateTrigger("t1", FireTime, fixedDelay: true));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockReason.Should().Be("definition_changed");
        store.Transactions.Should().Be(2);
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundRolledBackForLock);
    }

    /// <summary>
    /// A delegate that says it takes part in the round but did not read what the rule decides by — a
    /// subclass whose acquisition statement is 4.5's — gives the rule no answer, and no answer is "lock".
    /// </summary>
    [Test]
    public async Task ACandidateReadWithoutTheFieldsTheRuleDecidesBySendsTheRoundToTheLock()
    {
        GivenCandidates(new TriggerAcquireResult(new TriggerKey("t1", "g1"), typeof(NoOpJob).AssemblyQualifiedName, null));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockName.Should().Be(TriggerAccess);
        round.LockReason.Should().Be("delegate");
    }

    /// <summary>
    /// A round that had to take the lock leaves the next one starting under it, so that a schedule whose
    /// serial job is first in the order does not roll back every round; a locked round that found nothing
    /// needing the lock lets the one after it start without.
    /// </summary>
    [Test]
    public async Task ARoundThatEscalatedLeavesTheNextOneLockedUntilALockedRoundNeedsItForNothing()
    {
        GivenCandidates(Candidate("t1", serial: true));
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("serial_job");

        GivenCandidates(Candidate("t2"));
        TriggerAcquisitionResult second = await AcquireAndFire(maxCount: 2);
        second.LockName.Should().Be(TriggerAccess);
        second.LockReason.Should().Be("hysteresis");
        store.Transactions.Should().Be(3, "the second round started locked rather than rolling back to find out");

        TriggerAcquisitionResult third = await AcquireAndFire(maxCount: 2);
        third.LockName.Should().Be("none", "the round before it held the lock and found nothing that needed it");
        third.LockReason.Should().BeNull();
    }

    /// <summary>
    /// A locked round that did find a candidate needing the lock keeps the next one locked too: the
    /// hysteresis ends only on a round the lock was taken for nothing.
    /// </summary>
    [Test]
    public async Task TheHysteresisHoldsWhileLockedRoundsKeepFindingWorkThatNeedsTheLock()
    {
        GivenCandidates(Candidate("t1", serial: true));
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("serial_job");

        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("hysteresis");
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("hysteresis", "the round before it needed the lock, so nothing has shown the lock to be unneeded");
        store.Transactions.Should().Be(4, "one rollback, then one transaction a round");
    }

    /// <summary>
    /// The candidate read said the job allows concurrent execution; the read after the claim finds it does
    /// not — it was replaced in between. The decision no longer holds, so the round rolls back and runs
    /// again under the lock, and the job fires once.
    /// </summary>
    [Test]
    public async Task AJobFoundSerialAfterTheClaimRollsTheRoundBackAndRunsItUnderTheLock()
    {
        GivenCandidates(Candidate("t1"));
        GivenTheJob(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().DisallowConcurrentExecution().Build());

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        round.LockName.Should().Be(TriggerAccess);
        round.LockReason.Should().Be("definition_changed");
        store.Transactions.Should().Be(2);

        // Claimed in both attempts — the first's claim rolled back with it — and written in the second
        // alone, so the job fires exactly once.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundRolledBackForLock)
            .Which.Should().Match<LogEntry>(x => x.Level == LogLevel.Warning && x.Message.Contains("t1"),
                "a definition changing under a round is rare enough to be worth a line an operator sees");
    }

    /// <summary>
    /// The same for the trigger: a policy the fire decides, found on the row only after the claim.
    /// </summary>
    [Test]
    public async Task ATriggerFoundWithAFireDecidedPolicyAfterTheClaimRollsTheRoundBack()
    {
        GivenCandidates(Candidate("t1"));
        GivenTheTriggersReadBack(CreateTrigger("t1", FireTime, OverlapPolicy.Skip));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockReason.Should().Be("definition_changed");
        store.Transactions.Should().Be(2);
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundRolledBackForLock);
    }

    /// <summary>
    /// The candidate read left out the groups this node does not serve (#4069); the read after the claim
    /// finds the trigger moved into one of them in between. That is a changed definition like a job made
    /// serial: the round rolls back and runs again under the lock, whose read leaves the trigger to a node
    /// that serves it, and this node neither fires it nor — when it is due later in the window — reserves it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ATriggerMovedToAGroupThisNodeDoesNotServeAfterTheReadIsNeitherFiredNorReserved(bool dueLaterInTheWindow)
    {
        // A shipped delegate: its statement keeps unserved groups out, so the store's backstop is not what
        // stops the trigger here.
        A.CallTo(() => driverDelegate.FiltersAcquisitionExecutionGroups).Returns(true);

        DateTimeOffset at = dueLaterInTheWindow ? DateTimeOffset.UtcNow.AddHours(1) : FireTime;
        TriggerAcquireResult candidate = Candidate("t1", at: at) with { ExecutionGroup = "batch" };
        int reads = 0;
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(A<ConnectionAndTransactionHolder>._, A<TriggerAcquisitionCriteria>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<List<TriggerAcquireResult>>(Interlocked.Increment(ref reads) == 1 ? [candidate] : []));

        IOperableTrigger regrouped = CreateTrigger("t1", at);
        regrouped.ExecutionGroup = "reports";
        GivenTheTriggersReadBack(regrouped);

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow + TimeSpan.FromHours(3),
            MaxCount = 2,
            TimeWindow = dueLaterInTheWindow ? TimeSpan.FromHours(2) : TimeSpan.Zero,
            ServedExecutionGroups = [ExecutionGroupScope.Named("batch")],
        });

        round.Due.Should().BeEmpty("the trigger now belongs to a group another node serves");
        round.Pending.Should().BeEmpty("a trigger this node would never run is not reserved by it either");
        round.LockName.Should().Be(TriggerAccess);
        round.LockReason.Should().Be("definition_changed");
        locks.Acquired.Should().Equal([SchedulerLock.TriggerAccess]);
        store.Transactions.Should().Be(2, "the attempt without the lock, rolled back, and the one under it");
        reads.Should().Be(2, "the locked attempt reads the candidates again, and its statement leaves the trigger out");

        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.InsertFiredTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<IOperableTrigger>>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundRolledBackForLock)
            .Which.Message.Should().Contain("t1").And.Contain("execution group");
    }

    /// <summary>
    /// A trigger read back in a group the node serves is fired as any other: the re-check costs a lookup,
    /// not a rollback.
    /// </summary>
    [Test]
    public async Task ATriggerReadBackInAGroupThisNodeServesIsFiredWithoutTheLock()
    {
        A.CallTo(() => driverDelegate.FiltersAcquisitionExecutionGroups).Returns(true);
        GivenCandidates(Candidate("t1") with { ExecutionGroup = "batch" });
        IOperableTrigger batch = CreateTrigger("t1", FireTime);
        batch.ExecutionGroup = "batch";
        GivenTheTriggersReadBack(batch);

        // One request for both rounds, as the scheduler hands every round the same lists.
        TriggerAcquisitionRequest request = new()
        {
            NoLaterThan = DateTimeOffset.UtcNow + TimeSpan.FromHours(3),
            MaxCount = 2,
            RefusedExecutionGroups = [ExecutionGroupScope.Named("reports")],
        };

        for (int i = 0; i < 2; i++)
        {
            TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(request);

            round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
            round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
            round.LockName.Should().Be("none", "a node that refuses only 'reports' serves 'batch'");
        }

        store.Transactions.Should().Be(2, "one transaction a round, with no rollback");
    }

    /// <summary>
    /// A job type that will not load is settled before the claim, as the locked round settles it: stored
    /// <c>ERROR</c> from <c>WAITING</c> — the state the candidate read found, since this round has not
    /// claimed the row and another node may have — reported, and left out of the claim.
    /// </summary>
    [Test]
    public async Task AJobTypeThatWillNotLoadIsStoredErrorFromWaitingAndNotClaimed()
    {
        GivenCandidates(Candidate("t1", jobTypeName: "No.Such.Job, Nowhere"), Candidate("t2"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t2"]);
        round.LockName.Should().Be("none");

        TriggerKey broken = new("t1", "g1");
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherState(A<ConnectionAndTransactionHolder>._, broken, StoredTriggerState.Error, StoredTriggerState.Waiting, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(broken, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<TriggerClaim>>.That.Matches(x => x.Count == 1 && x[0].TriggerKey.Name == "t2"),
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A claim another node took first is not this round's: not read back, not fired, not reserved.
    /// </summary>
    [Test]
    public async Task AClaimAnotherNodeTookIsNeitherReadNorFired()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"), Candidate("t3"));
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerKey>>([new TriggerKey("t1", "g1"), new TriggerKey("t3", "g1")]));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 3);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t3"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>.That.Matches(keys => keys.Count == 2 && !keys.Any(key => key.Name == "t2")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.InsertFiredTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<IOperableTrigger>>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// A row the claim moved is this transaction's until it commits, so it is there to read. One that is
    /// not is a delegate answering wrongly, and the round fails rather than leave a claim with no row.
    /// </summary>
    [Test]
    public async Task AClaimedRowThatCannotBeReadBackFailsTheRound()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"));
        GivenTheTriggersReadBack(CreateTrigger("t1", FireTime));

        Func<Task> round = async () => await AcquireAndFire(maxCount: 2);

        await round.Should().ThrowAsync<JobPersistenceException>().WithMessage("*t2*claimed but could not be read back*");
    }

    /// <summary>
    /// A trigger due later in the batch window is claimed and reserved in the round, as it always was, and
    /// left pending for the scheduler: the window needs no lock either.
    /// </summary>
    [Test]
    public async Task ATriggerDueLaterInTheWindowIsReservedAndLeftPendingWithoutTheLock()
    {
        DateTimeOffset later = DateTimeOffset.UtcNow.AddHours(1);
        GivenCandidates(Candidate("t1"), Candidate("t2", at: later));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2, window: TimeSpan.FromHours(2));

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t2"]);
        round.LockName.Should().Be("none");
        A.CallTo(() => driverDelegate.InsertFiredTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<IOperableTrigger>>.That.Matches(x => x.Count == 1 && x[0].Key.Name == "t2"),
                StoredTriggerState.Acquired,
                null,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A clustered node's rounds are locked until its first check-in has read the state table and found
    /// every live peer at 4.6 or later; from then on they decide for themselves.
    /// </summary>
    [Test]
    public async Task AClusteredNodeStartsLockedAndIsFreedByACheckInThatFindsOnlyPeersAtFourSix()
    {
        store = NewStore(clustered: true);
        GivenPeersAtFourSix();
        GivenCandidates(Candidate("t1"));

        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("older_peer", "nothing is known about the peers before the state table has been read");

        await store.CheckIn(Guid.NewGuid());

        (await AcquireAndFire(maxCount: 2)).LockName.Should().Be("none");
    }

    /// <summary>
    /// Rounds that take no lock race for the same rows on every node. One that loses every claim on every
    /// retry takes nothing while due work is there, and says how many rows another node took, so that the
    /// scheduler looks again soon rather than sleeping its idle wait behind due work. Off a cluster no other
    /// node claims a row, and nothing is said.
    /// </summary>
    [TestCase(true, 2)]
    [TestCase(false, 0)]
    public async Task ARoundThatLosesEveryClaimSaysHowManyRowsAnotherNodeTook(bool clustered, int claimedElsewhere)
    {
        store = NewStore(clustered);
        if (clustered)
        {
            GivenPeersAtFourSix();
            await store.CheckIn(Guid.NewGuid());
        }

        GivenCandidates(Candidate("t1"), Candidate("t2"));
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<List<TriggerKey>>(new List<TriggerKey>()));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Should().BeEmpty();
        round.Pending.Should().BeEmpty();
        round.LockName.Should().Be("none");
        round.ClaimedElsewhere.Should().Be(claimedElsewhere,
            "a node that lost every row it reached for found due work, and must not sleep its idle wait behind it");
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    /// <summary>
    /// A round without the lock that lost a claim to another node turns the rounds after it to the lock,
    /// for contention, and says so once (#4067): the nodes were racing for the same backlog, and the lock
    /// serialises them more cheaply than the race. The round that lost says it, so the loop goes again at once.
    /// </summary>
    [Test]
    public async Task ARoundThatLosesAClaimToAnotherNodeTurnsTheRoundsAfterItToTheLock()
    {
        await GivenAClusterAtFourSix();
        GivenCandidates(Candidate("t1"), Candidate("t2"));
        GivenClaimsLost("t2");

        TriggerAcquisitionResult first = await AcquireAndFire(maxCount: 2);
        first.LockName.Should().Be("none");
        first.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        first.Contended.Should().BeTrue("it lost t2 to another node, which is what turns the next rounds to the lock");

        GivenClaimsLost();
        TriggerAcquisitionResult second = await AcquireAndFire(maxCount: 2);
        second.LockName.Should().Be(TriggerAccess);
        second.LockReason.Should().Be("contention");
        second.Contended.Should().BeFalse("a locked round loses no claim to a node that takes the lock too");

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundsLockedForContention)
            .Which.Message.Should().Contain("lost 1 claims");
    }

    /// <summary>
    /// The episode lasts while the backlog does: a locked round that takes all it asked for leaves the next
    /// one locked, so the rounds do not alternate and race on every other one.
    /// </summary>
    [Test]
    public async Task AContentionEpisodeHoldsWhileLockedRoundsComeBackFull()
    {
        await GivenAClusterAtFourSix();
        await GivenAContentionEpisode();

        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("contention");
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("contention", "the round before took both triggers it asked for: the backlog is still there");
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("contention");
        logs.Entries.Count(x => x.EventId.Id == RoundsLockedForContention).Should().Be(1, "once per episode");
    }

    /// <summary>
    /// A locked round that comes back short of what it asked for found the backlog gone, and the round after
    /// it goes without the lock again.
    /// </summary>
    [Test]
    public async Task AContentionEpisodeEndsWithALockedRoundThatComesBackShort()
    {
        await GivenAClusterAtFourSix();
        await GivenAContentionEpisode();

        GivenCandidates(Candidate("t3"));
        TriggerAcquisitionResult shortRound = await AcquireAndFire(maxCount: 2);
        shortRound.LockReason.Should().Be("contention");
        shortRound.Due.Should().ContainSingle();

        TriggerAcquisitionResult after = await AcquireAndFire(maxCount: 2);
        after.LockName.Should().Be("none", "one trigger for a round that asked for two: the backlog is gone");
        after.LockReason.Should().BeNull();
    }

    /// <summary>
    /// An episode whose rounds keep coming back full ends after <see cref="AdoJobStoreBase.ContentionCoolDown" />
    /// all the same, and one round goes without the lock to find out whether the nodes still contend.
    /// </summary>
    [Test]
    public async Task AContentionEpisodeEndsAfterItsCoolDown()
    {
        FakeTimeProvider clock = new(FireTime);
        await GivenAClusterAtFourSix(clock);
        await GivenAContentionEpisode();

        clock.Advance(AdoJobStoreBase.ContentionCoolDown - TimeSpan.FromMilliseconds(1));
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("contention", "the cool-down has not passed and the round before came back full");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        TriggerAcquisitionResult probe = await AcquireAndFire(maxCount: 2);
        probe.LockName.Should().Be("none", "the cool-down passed, so one round finds out whether the nodes still contend");
    }

    /// <summary>
    /// A round of one locked for contention is 4.5's, whose acquisition still claims without the lock. One
    /// that loses every claim says how many rows another node took, so the scheduler looks again soon rather
    /// than after its idle wait, and coming back empty that way does not end the episode: the backlog is there.
    /// </summary>
    [Test]
    public async Task ARoundOfOneLockedForContentionThatLosesEveryClaimSaysSoAndKeepsTheEpisode()
    {
        await GivenAClusterAtFourSix();
        await GivenAContentionEpisode();
        GivenClaimsLost("t1", "t2");

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);
        round.LockReason.Should().Be("contention");
        round.Pending.Should().BeEmpty();
        round.ClaimedElsewhere.Should().BeGreaterThan(0, "a node that lost every row it reached for found due work, and must not sleep its idle wait behind it");

        (await AcquireAndFire(maxCount: 1)).LockReason.Should().Be("contention",
            "the round before came back empty because another node took its rows, not because the backlog was gone");
    }

    /// <summary>
    /// Off a cluster no other node claims a row: a claim this node could not make starts no episode, and a
    /// single node's rounds stay lock-free.
    /// </summary>
    [Test]
    public async Task OffAClusterALostClaimStartsNoEpisode()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"));
        GivenClaimsLost("t2");

        (await AcquireAndFire(maxCount: 2)).Contended.Should().BeFalse();

        GivenClaimsLost();
        (await AcquireAndFire(maxCount: 2)).LockName.Should().Be("none");
        logs.Entries.Should().NotContain(x => x.EventId.Id == RoundsLockedForContention);
    }

    /// <summary>
    /// The serial-job hysteresis keeps its own rule: a short round under it ends nothing, because the round
    /// still found a candidate that needed the lock, whatever a short round means to a contention episode.
    /// </summary>
    [Test]
    public async Task TheSerialHysteresisIsNotEndedByAShortRound()
    {
        GivenCandidates(Candidate("t1", serial: true));
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("serial_job");

        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("hysteresis");
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("hysteresis",
            "the round before came back short, but it needed the lock for its serial candidate, and that is the hysteresis's rule");
    }

    /// <summary>
    /// A round without the lock on a dialect that fires apart (#4117) is two transactions, neither locked:
    /// the claim, which reserves every trigger it claims and fires none, and the fire, which claims the due
    /// rows again from ACQUIRED at the fire time the claim held them to, reads them, and updates each
    /// reservation to EXECUTING.
    /// </summary>
    [Test]
    public async Task ARoundThatFiresApartClaimsInOneTransactionAndFiresInASecondWithoutTheLock()
    {
        store.FiresApart = true;
        GivenCandidates(Candidate("t1"), Candidate("t2"), Candidate("t3"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 3);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t2", "t3"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        round.Pending.Should().BeEmpty();
        round.LockName.Should().Be("none");
        round.LockReason.Should().BeNull();
        locks.Acquired.Should().BeEmpty("neither transaction needs TRIGGER_ACCESS");
        store.Transactions.Should().Be(2, "the claim and the fire each commit on their own");

        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>.That.Matches(x => x.Count == 3), StoredTriggerState.Acquired, StoredTriggerState.Waiting, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => driverDelegate.InsertFiredTriggers(
                    A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<IOperableTrigger>>.That.Matches(x => x.Count == 3), StoredTriggerState.Acquired, null, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                    A<ConnectionAndTransactionHolder>._,
                    A<IReadOnlyList<TriggerClaim>>.That.Matches(x => x.Count == 3 && x.All(claim => claim.NextFireTimeUtc == FireTime)),
                    StoredTriggerState.Acquired,
                    StoredTriggerState.Acquired,
                    A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => driverDelegate.SelectTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
                .MustHaveHappened());

        // Read after the claim, and again after the guard: what the fire writes over is the row it holds.
        A.CallTo(() => driverDelegate.SelectTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>.That.Matches(x => !x.FiredOnAcquire), A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>.That.Matches(x => x.FiredOnAcquire), A<CancellationToken>._))
            .MustNotHaveHappened();
        round.Fired.Select(x => x.TriggerFiredBundle.Trigger.FireInstanceId).Should().Equal(round.Due.Select(x => x.FireInstanceId),
            "each fire updates the reservation its claim wrote");
    }

    /// <summary>
    /// The same dialect folds a round as before when something in it needs the lock: the split is a round
    /// without the lock's shape, and a locked batch stays one locked transaction.
    /// </summary>
    [Test]
    public async Task ARoundThatEscalatesIsFoldedUnderTheLockWhereTheDialectFiresApart()
    {
        store.FiresApart = true;
        GivenCandidates(Candidate("t1"), Candidate("t2", serial: true));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t2"]);
        round.LockReason.Should().Be("serial_job");
        store.Transactions.Should().Be(2, "the attempt without the lock, rolled back before any write, and the locked round");
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, StoredTriggerState.Acquired, StoredTriggerState.Acquired, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// A row the guard does not move was paused, deleted, rescheduled or recovered between the claim and the
    /// fire. It is not fired, and keeps its reservation for the scheduler to release, as
    /// <c>TriggersFired</c> answers a trigger no longer ACQUIRED; the rest of the round fires.
    /// </summary>
    [Test]
    public async Task ARowMovedBetweenTheClaimAndTheFireIsNotFired()
    {
        store.FiresApart = true;
        GivenCandidates(Candidate("t1"), Candidate("t2"));
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, new TriggerKey("t2", "g1"), StoredTriggerState.Acquired, StoredTriggerState.Acquired, A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t2"]);
        round.Fired[0].TriggerFiredBundle.Should().NotBeNull();
        round.Fired[1].Should().Match<TriggerFiredResult>(x => x.TriggerFiredBundle == null && !x.IsDeclined && x.Exception == null,
            "a reservation the scheduler releases, which leaves a paused or replaced row as it is");
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // t2 keeps the reservation its claim wrote: nothing reserves it again.
        A.CallTo(() => driverDelegate.InsertFiredTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<IOperableTrigger>>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A job turned serial between the claim and the fire needs the lock, which the fire cannot take while it
    /// holds row locks and cannot roll the committed claim back for. Every due trigger goes back to the
    /// scheduler still reserved, which fires it through <c>TriggersFired</c> under the lock, and the next
    /// round starts locked.
    /// </summary>
    [Test]
    public async Task AJobFoundSerialAtTheFireHandsTheDueTriggersToTheSchedulerUnderTheLock()
    {
        store.FiresApart = true;
        GivenCandidates(Candidate("t1"), Candidate("t2"));
        GivenTheJob(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().DisallowConcurrentExecution().Build());

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Due.Should().BeEmpty();
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t1", "t2"]);
        round.Pending.Should().OnlyContain(x => x.FireInstanceId != null, "each is still reserved by its claim");
        round.LockName.Should().Be(TriggerAccess, "the scheduler's TriggersFired takes it for the fire");
        round.LockReason.Should().Be("definition_changed");
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundFireHandedToLock)
            .Which.Should().Match<LogEntry>(x => x.Level == LogLevel.Warning && x.Message.Contains("t1"));

        GivenTheJob(ConcurrentJob());
        GivenCandidates(Candidate("t3"));
        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("hysteresis", "the round after a definition changed starts locked");
    }

    /// <summary>
    /// A fire that fails without the lock cannot take the committed claims with it. The due triggers go back
    /// to the scheduler still reserved, whose <c>TriggersFired</c> fires them under the lock and handles a
    /// fire that fails again.
    /// </summary>
    [Test]
    public async Task AFireThatFailsWithoutTheLockHandsTheDueTriggersToTheScheduler()
    {
        store.FiresApart = true;
        GivenCandidates(Candidate("t1"));
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the fire's write failed"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);

        round.Due.Should().BeEmpty();
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockName.Should().Be("none", "nothing about the round asked for the lock");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundFireWithoutLockFailed)
            .Which.Should().Match<LogEntry>(x => x.Level == LogLevel.Warning && x.Exception != null);
    }

    /// <summary>
    /// A trigger due later in the batch window is claimed and reserved as before, and left pending; only the
    /// due ones are claimed again and fired. A round that claimed nothing due makes no second transaction.
    /// </summary>
    [Test]
    public async Task ARoundThatFiresApartFiresOnlyWhatIsDue()
    {
        store.FiresApart = true;
        DateTimeOffset later = DateTimeOffset.UtcNow.AddHours(1);
        GivenCandidates(Candidate("t1"), Candidate("t2", at: later));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2, window: TimeSpan.FromHours(2));

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t2"]);
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<TriggerClaim>>.That.Matches(x => x.Count == 1 && x[0].TriggerKey.Name == "t1"),
                StoredTriggerState.Acquired,
                StoredTriggerState.Acquired,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        GivenCandidates(Candidate("t4", at: later));
        int before = store.Transactions;
        TriggerAcquisitionResult nothingDue = await AcquireAndFire(maxCount: 1, window: TimeSpan.FromHours(2));
        nothingDue.Pending.Select(x => x.Key.Name).Should().Equal(["t4"]);
        (store.Transactions - before).Should().Be(1, "with nothing due there is nothing to fire");
    }

    /// <summary>
    /// A round of one without the lock fires apart too: the split is the round's shape, whatever its size.
    /// </summary>
    [Test]
    public async Task ARoundOfOneFiresApartWithoutTheLock()
    {
        store.FiresApart = true;
        GivenCandidates(Candidate("t1"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        round.LockName.Should().Be("none");
        locks.Acquired.Should().BeEmpty();
        store.Transactions.Should().Be(2);
    }

    /// <summary>
    /// A clustered store at 4.6 whose peers allow rounds without the lock, on <paramref name="clock" />.
    /// </summary>
    private async Task GivenAClusterAtFourSix(TimeProvider clock = null)
    {
        store = NewStore(clustered: true, timeProvider: clock);
        GivenPeersAtFourSix();
        await store.CheckIn(Guid.NewGuid());
    }

    /// <summary>
    /// A round without the lock loses t2 to another node, and the store is in a contention episode with two
    /// candidates due and every claim winning from then on.
    /// </summary>
    private async Task GivenAContentionEpisode()
    {
        GivenCandidates(Candidate("t1"), Candidate("t2"));
        GivenClaimsLost("t2");
        (await AcquireAndFire(maxCount: 2)).Contended.Should().BeTrue();
        GivenClaimsLost();
    }

    /// <summary>
    /// The claims on these triggers find their row already moved by another node, and every other claim wins.
    /// </summary>
    private void GivenClaimsLost(params string[] names)
    {
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, TriggerKey key, StoredTriggerState _, StoredTriggerState _, DateTimeOffset _, CancellationToken _) =>
                new ValueTask<int>(names.Contains(key.Name) ? 0 : 1));
    }

    /// <summary>
    /// A clustered store's next check-in finds one live peer, stamped 4.6.0.
    /// </summary>
    private void GivenPeersAtFourSix()
    {
        store.LastCheckin = DateTimeOffset.UtcNow;
        A.CallTo(() => driverDelegate.UpdateSchedulerState(A<ConnectionAndTransactionHolder>._, A<string>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.SelectFiredTriggerInstanceNames(A<ConnectionAndTransactionHolder>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<string>>(new List<string>()));
        A.CallTo(() => driverDelegate.SelectSchedulerStateRecords(A<ConnectionAndTransactionHolder>._, A<string>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(
            [
                new SchedulerStateRecord("peer-a", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(7.5)) { QuartzVersion = "4.6.0" },
            ]));
    }

    private ValueTask<TriggerAcquisitionResult> AcquireAndFire(int maxCount, TimeSpan window = default)
    {
        return store.AcquireNextTriggersAndFireDue(Request(maxCount, window));
    }

    private static TriggerAcquisitionRequest Request(int maxCount, TimeSpan window = default, ExecutionLimits limits = null)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow + TimeSpan.FromHours(3),
            MaxCount = maxCount,
            TimeWindow = window,
            ExecutionLimits = limits,
        };
    }

    private CountingStore NewStore(bool clustered = false, Action<AdoJobStoreOptions> configure = null, TimeProvider timeProvider = null)
    {
        return new CountingStore(clustered, loggerFactory, configure, timeProvider)
        {
            DirectDelegate = driverDelegate,
            DirectSignaler = signaler,
            LockHandler = locks,
        };
    }

    private static IJobDetail ConcurrentJob()
    {
        return JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build();
    }

    private void GivenTheJob(IJobDetail job)
    {
        A.CallTo(() => driverDelegate.SelectJobDetails(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<ITypeLoader>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<List<IJobDetail>>([job]));
    }

    private static TriggerAcquireResult Candidate(
        string name,
        DateTimeOffset? at = null,
        bool serial = false,
        OverlapPolicy policy = OverlapPolicy.Default,
        string jobTypeName = null,
        bool fixedDelay = false)
    {
        return new TriggerAcquireResult(new TriggerKey(name, "g1"), jobTypeName ?? typeof(NoOpJob).AssemblyQualifiedName, null)
        {
            ConcurrentExecutionDisallowed = serial,
            NextFireTimeUtc = at ?? FireTime,
            OverlapPolicy = policy,
            MeasureIntervalFromCompletion = fixedDelay,
        };
    }

    /// <summary>
    /// The candidate read returns these, and the read of the rows returns a trigger built from each, as
    /// the row the candidate describes: due at its fire time, with its policy.
    /// </summary>
    private void GivenCandidates(params TriggerAcquireResult[] candidates)
    {
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(A<ConnectionAndTransactionHolder>._, A<TriggerAcquisitionCriteria>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<List<TriggerAcquireResult>>(candidates.ToList()));

        GivenTheTriggersReadBack(candidates.Select(x => CreateTrigger(x.TriggerKey.Name, x.NextFireTimeUtc ?? FireTime, x.OverlapPolicy ?? OverlapPolicy.Default, x.MeasureIntervalFromCompletion)).ToArray());
    }

    private void GivenTheTriggersReadBack(params IOperableTrigger[] triggers)
    {
        A.CallTo(() => driverDelegate.SelectTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<IOperableTrigger>>(triggers.Where(x => keys.Contains(x.Key)).Select(x => (IOperableTrigger) x.Clone()).ToList()));
    }

    private static IOperableTrigger CreateTrigger(string name, DateTimeOffset nextFireTimeUtc, OverlapPolicy policy = OverlapPolicy.Default, bool fixedDelay = false)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g1")
            .ForJob(jobKey)
            .StartAt(nextFireTimeUtc)
            .WithOverlapPolicy(policy)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever().MeasureIntervalFromCompletion(fixedDelay))
            .Build();
        trigger.NextFireTimeUtc = nextFireTimeUtc;
        return trigger;
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// The in-process lock handler, counting what the store asks it for.
    /// </summary>
    private sealed class CountingLockHandler : ILockHandler
    {
        private readonly InProcessLockHandler inner = new();
        private readonly List<SchedulerLock> acquired = [];

        public List<SchedulerLock> Acquired
        {
            get
            {
                lock (acquired)
                {
                    return [.. acquired];
                }
            }
        }

        public bool RequiresConnection => inner.RequiresConnection;

        public ValueTask<bool> AcquireLock(Guid requestorId, ConnectionAndTransactionHolder conn, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            lock (acquired)
            {
                acquired.Add(lockKind);
            }

            return inner.AcquireLock(requestorId, conn, lockKind, cancellationToken);
        }

        public ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            return inner.ReleaseLock(requestorId, lockKind, cancellationToken);
        }
    }

    /// <summary>
    /// Counts the transactions the store opens: every unit of work starts by asking for its connection.
    /// </summary>
    private sealed class CountingStore : AdoJobStoreBaseTest.TestAdoJobStoreBase
    {
        private int transactions;

        public CountingStore(bool clustered, ILoggerFactory loggerFactory, Action<AdoJobStoreOptions> configure, TimeProvider timeProvider = null)
            : base(clustered, timeProvider, loggerFactory, configure)
        {
        }

        public int Transactions => Volatile.Read(ref transactions);

        /// <summary>
        /// Whether a round without the lock fires apart, as a dialect asks through its delegate (#4117); the
        /// delegate here is a fake, so the store answers for it.
        /// </summary>
        public bool FiresApart { get; set; }

        internal override bool FiresLockFreeRoundsApart => FiresApart;

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref transactions);
            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }
    }
}
