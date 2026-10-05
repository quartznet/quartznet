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

using System.Data.Common;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a round of already-due triggers costs when the store acquires and fires them together (#3864):
/// one transaction, one read of the round's headers and one of its jobs, and a fired-trigger row written
/// by each fire rather than reserved by the acquisition and updated by the fire.
/// </summary>
/// <remarks>
/// The delegate is faked, so a statement here is a call to a delegate member and a transaction is a
/// connection the store opened. The rows those calls leave behind are
/// <c>FireOnAcquireSqliteTest</c>'s to prove.
/// </remarks>
public class FireOnAcquireRoundTripTest
{
    private static readonly DateTimeOffset FireTime = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly JobKey jobKey = new("j1", "jg1");

    private CountingStore store;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        driverDelegate = A.Fake<IDriverDelegate>();
        store = new CountingStore
        {
            DirectDelegate = driverDelegate,
            DirectSignaler = A.Fake<ISchedulerSignaler>(),
        };

        // Every candidate wins its compare-and-swap.
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerKey>._,
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<DateTimeOffset>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));

        // The round's claims and fire writes through the interface's defaults — the single-trigger calls,
        // each arranged here — unless a test answers them itself.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<TriggerClaim>>._,
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<CancellationToken>._))
            .CallsBaseMethod();
        A.CallTo(() => driverDelegate.ApplyTriggersFired(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerFiredUpdate>>._, A<CancellationToken>._))
            .CallsBaseMethod();

        // Every claimed trigger reads back ACQUIRED, as a claim in this transaction leaves it.
        A.CallTo(() => driverDelegate.SelectStoredTriggerHeaders(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<StoredTriggerHeader>>(
                    keys.Select(key => new StoredTriggerHeader(key, jobKey, StoredTriggerState.Acquired, FireTime, AdoConstants.TriggerTypeSimple)).ToList()));

        GivenTheJob(JobBuilder.Create<NoOpAcquisitionJob>().WithIdentity(jobKey).StoreDurably().Build());
    }

    /// <summary>
    /// A round of three due triggers is one transaction, where acquiring and then firing them was two;
    /// the fires read nothing of their own, and write their rows rather than update reservations.
    /// </summary>
    [Test]
    public async Task ARoundOfDueTriggersIsOneTransaction()
    {
        GivenCandidates("t1", "t2", "t3");

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 3);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t2", "t3"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        round.Pending.Should().BeEmpty();

        store.Transactions.Should().Be(1, "the acquisition and the fires are one unit of work");

        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>.That.Matches(x => x.FiredOnAcquire), A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => driverDelegate.SelectStoredTriggerHeaders(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.SelectJobDetails(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<ITypeLoader>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // A fire of a trigger acquired in the same transaction reads its header and its job with the
        // round's, and has its row written by the fire rather than reserved first.
        A.CallTo(() => driverDelegate.SelectTriggerHeader(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.SelectJobDetail(A<ConnectionAndTransactionHolder>._, A<JobKey>._, A<ITypeLoader>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => driverDelegate.InsertFiredTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<IOperableTrigger>>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        // Two fires of one job are two firings, and each runs with a job detail of its own.
        round.Fired[0].TriggerFiredBundle!.JobDetail.Should().NotBeSameAs(round.Fired[1].TriggerFiredBundle!.JobDetail);
    }

    /// <summary>
    /// The same three triggers acquired and then fired: the two transactions the round above replaces.
    /// </summary>
    [Test]
    public async Task AcquiringAndThenFiringIsTwoTransactions()
    {
        GivenCandidates("t1", "t2", "t3");

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(Request(maxCount: 3));
        A.CallTo(() => driverDelegate.SelectTriggerHeader(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, TriggerKey key, CancellationToken _) =>
                new ValueTask<StoredTriggerHeader>(new StoredTriggerHeader(key, jobKey, StoredTriggerState.Acquired, FireTime, AdoConstants.TriggerTypeSimple)));
        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);

        fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        store.Transactions.Should().Be(2);
    }

    /// <summary>
    /// One trigger a round, acquired without the lock, stays two transactions: the round cannot take the
    /// lock the fire needs without taking it for every acquisition, the idle ones included.
    /// </summary>
    [Test]
    public async Task ALockFreeBatchOfOneIsLeftForTheSchedulerToFire()
    {
        store.AcquireTriggersWithinLock = false;
        GivenCandidates("t1");

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 1);

        round.Due.Should().BeEmpty();
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t1"]);
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        // Reserved, as acquisition always reserved it.
        A.CallTo(() => driverDelegate.InsertFiredTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<IOperableTrigger>>._, StoredTriggerState.Acquired, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A trigger due later in the batch window is acquired and reserved in the round, and left pending.
    /// </summary>
    [Test]
    public async Task ATriggerDueLaterInTheWindowIsReservedAndLeftPending()
    {
        GivenCandidates(("t1", FireTime), ("t2", DateTimeOffset.UtcNow.AddHours(1)));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2, window: TimeSpan.FromHours(2));

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1"]);
        round.Pending.Select(x => x.Key.Name).Should().Equal(["t2"]);
        A.CallTo(() => driverDelegate.InsertFiredTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<IOperableTrigger>>.That.Matches(x => x.Count == 1 && x[0].Key.Name == "t2"),
                StoredTriggerState.Acquired,
                null,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A due trigger that does not fire after all — its job is gone — is left as a trigger acquired
    /// earlier is left by a fire that does not happen: <c>ACQUIRED</c>, with its reservation row, for the
    /// scheduler to release. It is never left <c>ACQUIRED</c> with no row at all.
    /// </summary>
    [Test]
    public async Task ADueTriggerThatDoesNotFireIsReservedForTheSchedulerToRelease()
    {
        GivenCandidates("t1");
        GivenTheJob(null);

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Fired.Should().ContainSingle().Which.Should().BeSameAs(TriggerFiredResult.NotFired);
        A.CallTo(() => driverDelegate.InsertFiredTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<IOperableTrigger>>.That.Matches(x => x.Count == 1 && x[0].Key.Name == "t1"),
                StoredTriggerState.Acquired,
                null,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A job whose stored data will not read fails the round's read of every job beside it. Each fire then
    /// reads its own job, as the fire of an acquired trigger does, so the one that fails is settled alone.
    /// </summary>
    [Test]
    public async Task AJobReadThatFailsForTheRoundIsReadAgainByEachFire()
    {
        GivenCandidates("t1", "t2");
        A.CallTo(() => driverDelegate.SelectJobDetails(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<ITypeLoader>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the stored job data does not deserialize"));
        A.CallTo(() => driverDelegate.SelectJobDetail(A<ConnectionAndTransactionHolder>._, jobKey, A<ITypeLoader>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<IJobDetail>(JobBuilder.Create<NoOpAcquisitionJob>().WithIdentity(jobKey).StoreDurably().Build()));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Fired.Should().HaveCount(2).And.OnlyContain(x => x.TriggerFiredBundle != null);
        A.CallTo(() => driverDelegate.SelectJobDetail(A<ConnectionAndTransactionHolder>._, jobKey, A<ITypeLoader>._, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
        store.Transactions.Should().Be(1, "a batch read that did not read is not a failed fire, so nothing is rolled back");
    }

    /// <summary>
    /// The round's claims are one call and its fire writes another, which a delegate that batches sends as
    /// one batch each.
    /// </summary>
    [Test]
    public async Task ARoundClaimsInOneCallAndWritesItsFiresInAnother()
    {
        GivenCandidates("t1", "t2", "t3");

        await AcquireAndFire(maxCount: 3);

        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<TriggerClaim>>.That.Matches(x => x.Count == 3),
                StoredTriggerState.Acquired,
                StoredTriggerState.Waiting,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.ApplyTriggersFired(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<TriggerFiredUpdate>>.That.Matches(x => x.Count == 3 && x.All(update => update.FiredOnAcquire)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A claim another node took first is not this round's: it is neither fired nor reserved.
    /// </summary>
    [Test]
    public async Task AClaimAnotherNodeTookIsNeitherFiredNorReserved()
    {
        GivenCandidates("t1", "t2", "t3");
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerKey>>([new TriggerKey("t1", "g1"), new TriggerKey("t3", "g1")]));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 3);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t3"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        A.CallTo(() => driverDelegate.InsertFiredTriggers(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<IOperableTrigger>>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// A batch of fire writes that failed without saying whose rolls the round back, and it runs again
    /// claiming and writing one trigger at a time — which finds the one that failed, and fires the rest once
    /// (#3931).
    /// </summary>
    [Test]
    public async Task AWriteBatchThatFailedWithoutSayingWhoseRunsTheRoundAgainOneTriggerAtATime()
    {
        GivenCandidates("t1", "t2", "t3");
        A.CallTo(() => driverDelegate.ApplyTriggersFired(A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerFiredUpdate>>._, A<CancellationToken>._))
            .Throws(new TriggerWriteFailedException(-1, new InvalidOperationException("the batch failed")));
        A.CallTo(() => driverDelegate.ApplyTriggerFired(A<ConnectionAndTransactionHolder>._, A<TriggerFiredUpdate>.That.Matches(x => x.Trigger.Key.Name == "t2"), A<CancellationToken>._))
            .Throws(new InvalidOperationException("t2's fire cannot be written"));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 3);

        round.Due.Select(x => x.Key.Name).Should().Equal(["t1", "t2", "t3"]);
        round.Fired[0].TriggerFiredBundle.Should().NotBeNull();
        round.Fired[1].Exception.Should().NotBeNull("the fire that failed is found once the round writes one trigger at a time");
        round.Fired[2].TriggerFiredBundle.Should().NotBeNull();
        store.Transactions.Should().Be(3, "the batch that failed, the run that found the failed fire, and the run that committed without it");

        // Only the first run claims together; the runs after it claim one trigger at a time.
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A batch of claims that could not say which took rolls the round back, and it claims one trigger at a
    /// time instead.
    /// </summary>
    [Test]
    public async Task AClaimOutcomeTheBatchCouldNotTellClaimsTheRoundAgainOneTriggerAtATime()
    {
        GivenCandidates("t1", "t2");
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<IReadOnlyList<TriggerClaim>>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<CancellationToken>._))
            .Throws(new ClaimOutcomeUnknownException(1, 2));

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 2);

        round.Fired.Should().HaveCount(2).And.OnlyContain(x => x.TriggerFiredBundle != null);
        store.Transactions.Should().Be(2);
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<StoredTriggerState>._, A<StoredTriggerState>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    private ValueTask<TriggerAcquisitionResult> AcquireAndFire(int maxCount, TimeSpan window = default)
    {
        return store.AcquireNextTriggersAndFireDue(Request(maxCount, window));
    }

    private static TriggerAcquisitionRequest Request(int maxCount, TimeSpan window = default)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow + TimeSpan.FromHours(3),
            MaxCount = maxCount,
            TimeWindow = window,
        };
    }

    private void GivenTheJob(IJobDetail job)
    {
        A.CallTo(() => driverDelegate.SelectJobDetails(A<ConnectionAndTransactionHolder>._, A<IReadOnlyCollection<JobKey>>._, A<ITypeLoader>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<List<IJobDetail>>(job is null ? [] : [job]));
    }

    private void GivenCandidates(params string[] names)
    {
        GivenCandidates([.. names.Select(name => (name, FireTime))]);
    }

    private void GivenCandidates(params (string Name, DateTimeOffset NextFireTimeUtc)[] candidates)
    {
        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerAcquisitionCriteria>._,
                A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<List<TriggerAcquireResult>>(
                candidates.Select(x => new TriggerAcquireResult(new TriggerKey(x.Name, "g1"), typeof(NoOpAcquisitionJob).AssemblyQualifiedName, null)).ToList()));

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<IOperableTrigger>>(candidates
                    .Where(x => keys.Contains(new TriggerKey(x.Name, "g1")))
                    .Select(x => CreateTrigger(x.Name, x.NextFireTimeUtc))
                    .ToList()));
    }

    private static IOperableTrigger CreateTrigger(string name, DateTimeOffset nextFireTimeUtc)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "g1")
            .ForJob(jobKey)
            .StartAt(nextFireTimeUtc)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        trigger.NextFireTimeUtc = nextFireTimeUtc;
        return trigger;
    }

    private sealed class NoOpAcquisitionJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Counts the transactions the store opens: every unit of work starts by asking for its connection.
    /// </summary>
    private sealed class CountingStore : AdoJobStoreBaseTest.TestAdoJobStoreBase
    {
        private int transactions;

        public int Transactions => Volatile.Read(ref transactions);

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref transactions);
            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }
    }
}
