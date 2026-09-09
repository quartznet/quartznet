using StackExchange.Redis;

using Quartz.Extensions.Redis;

namespace Quartz.Tests.Integration.Impl.Redis;

/// <summary>
/// A node leaves a running cluster: it closes the Redis connection it opened, it fires nothing more,
/// and the schedule it was halfway through still comes out having fired every instant exactly once.
/// </summary>
/// <remarks>
/// <para>
/// <c>RedisLockHandlerTest.ShuttingDownClosesTheConnectionItOpened</c> is the same #3639 path with
/// nothing else going on — a handler that took one lock, handled directly. This is that path where it
/// actually matters: the handler is the store's, the store is mid-acquisition, a peer is contending
/// for the very key being released, and the connection being closed is one the scheduler has been
/// locking through for thousands of round trips.
/// </para>
/// <para>
/// The workload is <see cref="RedisTwoNodeTest" />'s, shorter. It keeps the
/// <c>[DisallowConcurrentExecution]</c> trigger because a node that vanishes while holding a job is
/// the case that leaves a <c>BLOCKED</c> trigger behind for the survivor's cluster recovery to unblock,
/// and that unblocking is part of what "costs the cluster no firing" means.
/// </para>
/// </remarks>
[Category("db-redis")]
[NonParallelizable]
public sealed class RedisLockHandlerShutdownUnderLoadTest : RedisClusterTestBase
{
    private const int OrdinaryFirings = 100;
    private const int SerialFirings = 60;

    /// <summary>
    /// How much of the schedule has to be behind the cluster before one node leaves, so that the
    /// leaving genuinely happens mid-run.
    /// </summary>
    private const int FiringsBeforeLeaving = 20;

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    protected override string SchedulerName => "redis-shutdown-load";

    [Test]
    public async Task ANodeLeavingMidRunClosesItsConnectionAndCostsTheClusterNoFiring()
    {
        await using RedisNode nodeA = await CreateNode("nodeA");
        await using RedisNode nodeB = await CreateNode("nodeB");

        nodeA.Store.LockHandler.Should().BeOfType<RedisLockHandler>(
            "the connection this test watches close is one the store opened for its own locking, so a "
            + "store locking through anything else would make every assertion below vacuous");
        nodeB.Store.LockHandler.Should().BeOfType<RedisLockHandler>();

        await nodeA.Scheduler.Start();
        await nodeB.Scheduler.Start();

        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(3);
        DateTimeOffset[] ordinary = await ScheduleOrdinaryTrigger(nodeA.Scheduler, start, OrdinaryFirings, Interval);
        DateTimeOffset[] serial = await ScheduleSerialTrigger(nodeB.Scheduler, start, SerialFirings, Interval);

        // Both nodes have to have taken the lock and fired through it, or "one node left" would be
        // "one node never joined" and the survivor would simply carry on as it always had.
        await WaitForCondition(
            () => Task.FromResult(FiringCount() >= FiringsBeforeLeaving && NodeFiringCount("nodeA") > 0 && NodeFiringCount("nodeB") > 0),
            timeoutMs: 60_000,
            async () => $"both nodes to be firing, with at least {FiringsBeforeLeaving} firings behind "
                        + $"them ({NodeFiringCounts()}). State:\n{await DumpDatabaseState()}");

        RedisLockHandler leaving = nodeA.LockHandler;
        IConnectionMultiplexer opened = leaving.Connection;

        opened.Should().NotBeNull("the node has been locking through Redis, which is what opens the connection");
        opened.IsConnected.Should().BeTrue("the node was talking to this server a moment ago");

        int nodeBBeforeLeaving = NodeFiringCount("nodeB");

        await nodeA.Scheduler.Shutdown(waitForJobsToComplete: false);

        opened.IsConnected.Should().BeFalse(
            "the multiplexer belongs to the handler and the handler to the store, so a scheduler that "
            + "has shut down leaves no Redis connection and no heartbeat behind (#3639) — and it has to "
            + "hold with a peer still contending for the very key this node was releasing");
        leaving.Connection.Should().BeNull("the handler has let go of what it closed");

        // A firing already dispatched when the halt landed still records itself, so the leaver's count
        // is taken after that has had time to happen rather than at the instant of the shutdown.
        await Task.Delay(2000);
        int nodeAAfterLeaving = NodeFiringCount("nodeA");

        // Generous, because the survivor may first have to notice its peer is gone: a node that leaves
        // does not delete its own SCHEDULER_STATE row, so anything it was holding waits on the other
        // node's cluster recovery — one check-in interval plus the misfire threshold.
        await WaitForCondition(
            () => Task.FromResult(FiringCount() >= OrdinaryFirings + SerialFirings),
            timeoutMs: 90_000,
            async () => $"the survivor to finish the schedule alone; {FiringCount()} of "
                        + $"{OrdinaryFirings + SerialFirings} firings arrived ({NodeFiringCounts()}). "
                        + $"State:\n{await DumpDatabaseState()}");

        await Task.Delay(3000);

        ReportFirings();

        NodeFiringCount("nodeB").Should().BeGreaterThan(nodeBBeforeLeaving,
            "the survivor has to take over the whole schedule — a cluster whose remaining node stopped "
            + "firing when its peer left would be worse than one that never had a peer");

        NodeFiringCount("nodeA").Should().Be(nodeAAfterLeaving,
            "a scheduler that has shut down acquires nothing, so every firing from here on is the "
            + "survivor's");

        AssertFiredExactlyOnce(OrdinaryTriggerName, ordinary);
        AssertFiredExactlyOnce(SerialTriggerName, serial);
        AssertSerialJobNeverOverlapped();

        FiringCount().Should().Be(OrdinaryFirings + SerialFirings,
            "a node leaving costs the cluster no firing and gains it none — the two triggers have {0} "
            + "scheduled instants whether or not both nodes saw them through",
            OrdinaryFirings + SerialFirings);
    }
}
