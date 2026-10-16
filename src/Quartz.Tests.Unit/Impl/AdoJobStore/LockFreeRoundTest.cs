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
    /// The 4.5 shape, which the option off keeps: a round of one acquired without the lock is left for the
    /// scheduler to fire under it. The tag describes that round — the acquisition and the fire it hands
    /// on — so an operator comparing the two settings sees every round locked, as every round is.
    /// </summary>
    [Test]
    public async Task WithTheOptionOffARoundOfOneIsLeftForTheSchedulerAndSaysItIsLocked()
    {
        store = NewStore(configure: options => options.LockFreeFiringRounds = false);
        GivenCandidates(Candidate("t1"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);

        round.Due.Should().BeEmpty();
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.LockName.Should().Be(TriggerAccess, "the scheduler's TriggersFired takes it for the fire");
        round.LockReason.Should().Be("option_off");
        locks.Acquired.Should().BeEmpty("the acquisition itself takes none, as it never did");
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
        GivenCandidates(Candidate("t1"));

        (await AcquireAndFire(maxCount: 2)).LockReason.Should().Be("older_peer", "nothing is known about the peers before the state table has been read");

        await store.CheckIn(Guid.NewGuid());

        (await AcquireAndFire(maxCount: 2)).LockName.Should().Be("none");
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

    private CountingStore NewStore(bool clustered = false, Action<AdoJobStoreOptions> configure = null)
    {
        return new CountingStore(clustered, loggerFactory, configure)
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
        string jobTypeName = null)
    {
        return new TriggerAcquireResult(new TriggerKey(name, "g1"), jobTypeName ?? typeof(NoOpJob).AssemblyQualifiedName, null)
        {
            ConcurrentExecutionDisallowed = serial,
            NextFireTimeUtc = at ?? FireTime,
            OverlapPolicy = policy,
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

        GivenTheTriggersReadBack(candidates.Select(x => CreateTrigger(x.TriggerKey.Name, x.NextFireTimeUtc ?? FireTime, x.OverlapPolicy ?? OverlapPolicy.Default)).ToArray());
    }

    private void GivenTheTriggersReadBack(params IOperableTrigger[] triggers)
    {
        A.CallTo(() => driverDelegate.SelectTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<IOperableTrigger>>(triggers.Where(x => keys.Contains(x.Key)).Select(x => (IOperableTrigger) x.Clone()).ToList()));
    }

    private static IOperableTrigger CreateTrigger(string name, DateTimeOffset nextFireTimeUtc, OverlapPolicy policy = OverlapPolicy.Default)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g1")
            .ForJob(jobKey)
            .StartAt(nextFireTimeUtc)
            .WithOverlapPolicy(policy)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
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

        public CountingStore(bool clustered, ILoggerFactory loggerFactory, Action<AdoJobStoreOptions> configure)
            : base(clustered, timeProvider: null, loggerFactory, configure)
        {
        }

        public int Transactions => Volatile.Read(ref transactions);

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref transactions);
            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }
    }
}
