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

#nullable enable

using FakeItEasy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a clustered node decides about its peers from each check-in's read of the state table: whether
/// every live one is a 4.6 node, so that a firing round of concurrent-allowed work may take no
/// <c>TRIGGER_ACCESS</c> lock beside them (#4067).
/// </summary>
/// <remarks>
/// The delegate is a fake, so the table is whatever rows the test says a check-in reads; the clock is the
/// test's, so a row is live or failed by arithmetic rather than by waiting.
/// </remarks>
public sealed class ClusterPeerVersionTest
{
    /// <summary>Log events <c>ClusterLog.OlderPeerKeepsRoundsLocked</c> and <c>ClusterLog.EveryPeerAllowsLockFreeRounds</c>.</summary>
    private const int OlderPeerKeepsRoundsLocked = 3517;
    private const int EveryPeerAllowsLockFreeRounds = 3518;

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(7.5);

    private FakeTimeProvider clock = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private IDriverDelegate driverDelegate = null!;
    private AdoJobStoreBaseTest.TestAdoJobStoreBase store = null!;

    [SetUp]
    public void SetUp()
    {
        clock = new FakeTimeProvider(Now);
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        store = new AdoJobStoreBaseTest.TestAdoJobStoreBase(clustered: true, timeProvider: clock, loggerFactory: loggerFactory);
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();
        store.LockHandler = new InProcessLockHandler();
        store.LastCheckin = Now;

        UseDelegate(A.Fake<IDriverDelegate>());
    }

    /// <summary>
    /// The store's delegate is <paramref name="fake" />, whose check-in writes take and which finds no fired
    /// trigger of a node without a state row.
    /// </summary>
    private void UseDelegate(IDriverDelegate fake)
    {
        driverDelegate = fake;
        store.DirectDelegate = driverDelegate;

        A.CallTo(() => driverDelegate.UpdateSchedulerState(A<ConnectionAndTransactionHolder>._, A<string>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.SelectFiredTriggerInstanceNames(A<ConnectionAndTransactionHolder>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<string>>(new List<string>()));
    }

    [TearDown]
    public void TearDown()
    {
        loggerFactory.Dispose();
        logs.Dispose();
    }

    [Test]
    public async Task UntilTheFirstCheckInHasAnsweredTheRoundsAreLocked()
    {
        GivenPeers(Live("peer-a", "4.6.0"));

        store.PeersAllowLockFreeRounds.Should().BeFalse("nothing is known about the peers before the state table has been read");

        await store.CheckIn(Guid.NewGuid());

        store.PeersAllowLockFreeRounds.Should().BeTrue();
    }

    [Test]
    public async Task EveryLivePeerAtFourSixOrLaterAllowsLockFreeRounds()
    {
        GivenPeers(Live("peer-a", "4.6.0"), Live("peer-b", "4.7.2"));

        await store.CheckIn(Guid.NewGuid());

        store.PeersAllowLockFreeRounds.Should().BeTrue();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == EveryPeerAllowsLockFreeRounds)
            .Which.Message.Should().Contain("2 beside this one");
        logs.Entries.Should().NotContain(x => x.EventId.Id == OlderPeerKeepsRoundsLocked);
    }

    [TestCase(null, "a row a 4.5 node wrote carries no stamp")]
    [TestCase("4.5.1", "a 4.5 node's misfire and edit writes are by key")]
    [TestCase("not-a-version", "a stamp nothing can read says nothing about the node")]
    public async Task ALivePeerThatIsOlderOrUnreadableKeepsTheRoundsLocked(string? stamp, string because)
    {
        GivenPeers(Live("peer-a", "4.6.0"), Live("peer-old", stamp));

        await store.CheckIn(Guid.NewGuid());

        store.PeersAllowLockFreeRounds.Should().BeFalse(because);
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == OlderPeerKeepsRoundsLocked)
            .Which.Message.Should().Contain("peer-old").And.Contain(stamp ?? "none");
    }

    [Test]
    public async Task APeerPastItsFailureThresholdIsNotLive()
    {
        GivenPeers(Live("peer-a", "4.6.0"), Failed("peer-gone", null));
        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(A<ConnectionAndTransactionHolder>._, A<FiredTriggerQuery>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(new List<FiredTriggerRecord>()));

        await store.CheckIn(Guid.NewGuid());

        store.PeersAllowLockFreeRounds.Should().BeTrue("a row past its threshold is a node about to be recovered, not one whose writes can land");
    }

    [Test]
    public async Task TheVerdictIsLoggedOncePerTransition()
    {
        GivenPeers(Live("peer-a", "4.6.0"), Live("peer-old", "4.5.1"));
        await store.CheckIn(Guid.NewGuid());
        store.PeersAllowLockFreeRounds.Should().BeFalse();

        // The same table again: nothing changed, nothing is said.
        await store.CheckIn(Guid.NewGuid());
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == OlderPeerKeepsRoundsLocked);

        // The old node rolled: its row now carries 4.6.
        GivenPeers(Live("peer-a", "4.6.0"), Live("peer-old", "4.6.0"));
        await store.CheckIn(Guid.NewGuid());
        store.PeersAllowLockFreeRounds.Should().BeTrue();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == EveryPeerAllowsLockFreeRounds);

        // And a 4.5 node joined again.
        GivenPeers(Live("peer-a", "4.6.0"), Live("peer-older", null));
        await store.CheckIn(Guid.NewGuid());
        store.PeersAllowLockFreeRounds.Should().BeFalse();
        logs.Entries.Count(x => x.EventId.Id == OlderPeerKeepsRoundsLocked).Should().Be(2, "each transition back to locked rounds is one line");
    }

    [Test]
    public async Task ANodeAloneInItsClusterAllowsLockFreeRounds()
    {
        GivenPeers();

        await store.CheckIn(Guid.NewGuid());

        store.PeersAllowLockFreeRounds.Should().BeTrue("there is no older node whose writes the lock would have to keep off a fired row");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == EveryPeerAllowsLockFreeRounds)
            .Which.Message.Should().Contain("0 beside this one");
    }

    /// <summary>
    /// A 4.6 node locks every SQL Server round of its own, so a SQL Server cluster with one keeps 4.6's rounds
    /// on every node until it is gone (#4117). The other dialects keep 4.6 as the line.
    /// </summary>
    [TestCase("4.6.0", false)]
    [TestCase("4.7.0", true)]
    public async Task OnSqlServerEveryLivePeerHasToBeFourSeven(string stamp, bool allows)
    {
        UseDelegate(A.Fake<SqlServerDelegate>());
        GivenPeers(Live("peer-a", stamp));

        await store.CheckIn(Guid.NewGuid());

        store.PeersAllowLockFreeRounds.Should().Be(allows, "SQL Server's rounds take no lock beside 4.7 nodes only");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == (allows ? EveryPeerAllowsLockFreeRounds : OlderPeerKeepsRoundsLocked))
            .Which.Message.Should().Contain("4.7", "the line names the release the dialect needs");
    }

    [Test]
    public async Task OnPostgreSqlAFourSixPeerAllowsLockFreeRounds()
    {
        UseDelegate(A.Fake<PostgreSQLDelegate>());
        GivenPeers(Live("peer-a", "4.6.0"));

        await store.CheckIn(Guid.NewGuid());

        store.PeersAllowLockFreeRounds.Should().BeTrue("a 4.6 node holds its writes to the row as read, which is all a PostgreSQL round needs of it");
    }

    [Test]
    public void AStoreThatIsNotClusteredHasNoPeersToWaitFor()
    {
        AdoJobStoreBaseTest.TestAdoJobStoreBase alone = new(clustered: false, timeProvider: clock);

        alone.PeersAllowLockFreeRounds.Should().BeTrue("the check is about other nodes' writes, and a store that is not clustered has none");
    }

    private void GivenPeers(params SchedulerStateRecord[] peers)
    {
        A.CallTo(() => driverDelegate.SelectSchedulerStateRecords(A<ConnectionAndTransactionHolder>._, A<string?>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(peers.ToList()));
    }

    private static SchedulerStateRecord Live(string instanceId, string? stamp)
    {
        return new SchedulerStateRecord(instanceId, Now, Interval) { QuartzVersion = stamp };
    }

    private static SchedulerStateRecord Failed(string instanceId, string? stamp)
    {
        return new SchedulerStateRecord(instanceId, Now.AddHours(-1), Interval) { QuartzVersion = stamp };
    }
}
