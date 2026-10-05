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

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// When an acquisition asks how many of its node's pinned triggers a firing on another node holds
/// <c>BLOCKED</c> (#3988): only on a cluster, and only in a round that took nothing.
/// </summary>
/// <remarks>
/// The delegate is faked, and the question is answered by the store's own member, which the test
/// overrides to count the times it is asked. The statement behind the real answer is
/// <c>AcquisitionBehindExecutingJobSqliteTest</c>'s to prove.
/// </remarks>
public class AcquisitionHeldBackTest
{
    private static readonly JobKey jobKey = new("j1", "jg1");

    private static readonly DateTimeOffset BlockingFiredUtc = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.SupportsFireOnAcquire).Returns(true);

        // Every candidate wins its claim, one at a time or together.
        A.CallTo(() => driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerKey>._,
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<DateTimeOffset>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyList<TriggerClaim>>._,
                A<StoredTriggerState>._,
                A<StoredTriggerState>._,
                A<CancellationToken>._))
            .CallsBaseMethod();
    }

    /// <summary>
    /// A clustered round that takes nothing says how many of its node's pinned triggers are held, and by a
    /// firing fired when, through the lock-free acquisition of one trigger and through a round under the
    /// lock alike.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    public async Task AClusteredRoundThatTakesNothingCountsThePinnedTriggersHeldElsewhere(int maxCount)
    {
        GivenCandidates();
        ProbingStore store = Store(clustered: true, answer: 2);

        TriggerAcquisitionResult result = await store.AcquireNextTriggersAndFireDue(Request(maxCount));

        result.Pending.Should().BeEmpty();
        store.Probes.Should().Be(1, "the round took nothing, so its node is about to wait");
        result.Blocked.Should().Be(2, "both pinned triggers held elsewhere are what the scheduler is to look again for");
        result.LatestBlockingFiredUtc.Should().Be(BlockingFiredUtc,
            "the scheduler compares it with the last round's to tell whether the job changed hands");
    }

    /// <summary>
    /// A round that took something does not ask: its node is not about to wait out an idle wait.
    /// </summary>
    [Test]
    public async Task ARoundThatTakesSomethingDoesNotAsk()
    {
        GivenCandidates("t1");
        ProbingStore store = Store(clustered: true, answer: 2);

        TriggerAcquisitionResult result = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 2));

        result.Pending.Select(x => x.Key.Name).Should().Equal(["t1"]);
        store.Probes.Should().Be(0, "a node with a trigger to wait for looks again when it is due anyway");
        result.Blocked.Should().Be(0);
        result.LatestBlockingFiredUtc.Should().BeNull();
    }

    /// <summary>
    /// A store that is not clustered does not ask: every firing that can hold its triggers back ends on
    /// its own scheduler, and that end wakes it.
    /// </summary>
    [Test]
    public async Task AStoreThatIsNotClusteredDoesNotAsk()
    {
        GivenCandidates();
        ProbingStore store = Store(clustered: false, answer: 2);

        TriggerAcquisitionResult result = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 2));

        store.Probes.Should().Be(0, "nothing runs anywhere but on this node");
        result.Blocked.Should().Be(0);
    }

    /// <summary>
    /// A delegate other than the ones Quartz ships has no statement to ask with, so its node hears that
    /// nothing is held and looks again after its idle wait, as before.
    /// </summary>
    [Test]
    public async Task ADelegateOfSomebodysOwnCountsNothing()
    {
        GivenCandidates();
        ProbingStore store = Store(clustered: true, answer: null);

        TriggerAcquisitionResult result = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 2));

        store.Probes.Should().Be(1);
        result.Blocked.Should().Be(0, "only the shipped delegates can be asked");
        result.LatestBlockingFiredUtc.Should().BeNull();
    }

    private ProbingStore Store(bool clustered, int? answer)
    {
        return new ProbingStore(clustered, answer)
        {
            DirectDelegate = driverDelegate,
            DirectSignaler = A.Fake<ISchedulerSignaler>(),
        };
    }

    private static TriggerAcquisitionRequest Request(int maxCount)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow + TimeSpan.FromHours(3),
            MaxCount = maxCount,
            TimeWindow = TimeSpan.Zero,
        };
    }

    /// <summary>
    /// Candidates due in an hour, so that a round reserves them and fires nothing.
    /// </summary>
    private void GivenCandidates(params string[] names)
    {
        DateTimeOffset nextFireTimeUtc = DateTimeOffset.UtcNow.AddHours(1);

        A.CallTo(() => driverDelegate.SelectTriggersToAcquire(
                A<ConnectionAndTransactionHolder>._,
                A<TriggerAcquisitionCriteria>._,
                A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<List<TriggerAcquireResult>>(
                names.Select(name => new TriggerAcquireResult(new TriggerKey(name, "g1"), typeof(NoOpJob).AssemblyQualifiedName, null)).ToList()));

        A.CallTo(() => driverDelegate.SelectTriggers(
                A<ConnectionAndTransactionHolder>._,
                A<IReadOnlyCollection<TriggerKey>>._,
                A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IReadOnlyCollection<TriggerKey> keys, CancellationToken _) =>
                new ValueTask<List<IOperableTrigger>>(names
                    .Where(name => keys.Contains(new TriggerKey(name, "g1")))
                    .Select(name => CreateTrigger(name, nextFireTimeUtc))
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

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Counts the times the store asks about pinned triggers held elsewhere, and answers with a fixed
    /// count and fire time — or, with no count, the way the store answers for a delegate it cannot ask.
    /// </summary>
    private sealed class ProbingStore(bool clustered, int? answer) : AdoJobStoreBaseTest.TestAdoJobStoreBase(clustered)
    {
        private int probes;

        public int Probes => Volatile.Read(ref probes);

        internal override ValueTask<PinnedTriggersBlocked> SelectPinnedTriggersBlockedElsewhere(
            ConnectionAndTransactionHolder conn,
            DateTimeOffset noLaterThan,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref probes);
            return answer is { } count
                ? new ValueTask<PinnedTriggersBlocked>(new PinnedTriggersBlocked(count, BlockingFiredUtc))
                : base.SelectPinnedTriggersBlockedElsewhere(conn, noLaterThan, cancellationToken);
        }
    }
}
