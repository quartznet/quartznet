using Quartz.Extensions.Redis;

namespace Quartz.Tests.Integration.Impl.Redis;

/// <summary>
/// Two schedulers, one store, and a Redis key between them: every scheduled firing happens on exactly
/// one of them, and the job that forbids concurrency never runs twice at once.
/// </summary>
/// <remarks>
/// <para>
/// <c>RedisLockHandlerTest</c> proves the handler keeps its own contract — re-entry, cancellation,
/// expiry, ownership. None of that says a cluster locking through it cannot double-fire, because that
/// is a property of the handler <em>and</em> of everything the store does under it. This is the case
/// that says it, and it is the reason the package exists.
/// </para>
/// <para>
/// The two nodes contend for the same two trigger rows two hundred and fifty times over, with database
/// row locking switched off, so there is nothing but the Redis key keeping them apart. A lock that
/// granted itself twice would show up as a scheduled fire time fired by both nodes; one that lost a
/// release would show up as a fire time that never fired at all. Both are asserted, by name.
/// </para>
/// </remarks>
[Category("db-redis")]
[NonParallelizable]
public sealed class RedisTwoNodeTest : RedisClusterTestBase
{
    private const int OrdinaryFirings = 150;
    private const int SerialFirings = 100;

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    protected override string SchedulerName => "redis-two-node";

    [Test]
    public async Task TwoNodesLockingThroughRedisFireEveryScheduledTimeExactlyOnce()
    {
        await using RedisNode nodeA = await CreateNode("nodeA");
        await using RedisNode nodeB = await CreateNode("nodeB");

        // The premise, asserted rather than assumed: a store that had quietly built a lock handler of
        // its own would pass everything below without Redis being involved at all.
        nodeA.Store.LockHandler.Should().BeOfType<RedisLockHandler>(
            "UseRedisLockHandler registers the handler the store then locks through, and everything "
            + "this fixture asserts is about that handler");
        nodeB.Store.LockHandler.Should().BeOfType<RedisLockHandler>();

        nodeA.Store.UseDbLocks.Should().BeFalse(
            "a QRTZ_LOCKS row underneath would keep the two nodes apart whatever Redis did, and the "
            + "proof would be of the database's locking rather than of this package's");
        nodeB.Store.UseDbLocks.Should().BeFalse();

        await nodeA.Scheduler.Start();
        await nodeB.Scheduler.Start();

        // Far enough out that both triggers are stored, and both nodes awake, before either is due.
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(3);

        // Due at the same instants, so both nodes reach for the same earliest row on every cycle. Two
        // of them rather than one so that the loser of each race still has something to fire: a node
        // that never fired would make the exactly-once assertions true of a cluster of one.
        DateTimeOffset[] ordinary = await ScheduleOrdinaryTrigger(nodeA.Scheduler, start, OrdinaryFirings, Interval);
        DateTimeOffset[] serial = await ScheduleSerialTrigger(nodeB.Scheduler, start, SerialFirings, Interval);

        await WaitForCondition(
            () => Task.FromResult(FiringCount() >= OrdinaryFirings + SerialFirings),
            timeoutMs: 90_000,
            async () => $"all {OrdinaryFirings + SerialFirings} firings; {FiringCount()} arrived "
                        + $"({NodeFiringCounts()}). State:\n{await DumpDatabaseState()}");

        // Absence cannot be polled for. A duplicate that lost its race by a few hundred milliseconds
        // arrives after the last legitimate firing rather than before it, so the wait above would end
        // just as happily either way.
        await Task.Delay(3000);

        ReportFirings();

        AssertFiredExactlyOnce(OrdinaryTriggerName, ordinary);
        AssertFiredExactlyOnce(SerialTriggerName, serial);
        AssertSerialJobNeverOverlapped();
        AssertBothNodesFired("nodeA", "nodeB");

        FiringCount().Should().Be(OrdinaryFirings + SerialFirings,
            "the two triggers have {0} scheduled firings between them and no other trigger exists, so "
            + "any other total is a firing this fixture cannot account for", OrdinaryFirings + SerialFirings);

        // The last statement rather than the first, because a trigger that has run out of repeats is
        // deleted: "Complete or None" cannot tell a finished schedule from one that never existed, and
        // is only worth asserting once the firings above have said which of the two this is.
        await WaitForTriggerCompletion(nodeB.Scheduler, new TriggerKey(OrdinaryTriggerName, Group), 10_000);
        await WaitForTriggerCompletion(nodeB.Scheduler, new TriggerKey(SerialTriggerName, Group), 10_000);
    }
}
