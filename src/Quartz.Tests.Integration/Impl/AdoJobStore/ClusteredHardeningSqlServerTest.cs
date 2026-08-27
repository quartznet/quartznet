namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered hardening scenarios against SQL Server, plus the one lock handler only SQL Server can
/// carry. A clustered SQL Server store locks through <c>SelectForUpdateLockHandler</c> with the
/// <c>(UPDLOCK,ROWLOCK)</c> hint the store defaults it to, which is a different statement from the
/// PostgreSQL fixture's <c>SELECT ... FOR UPDATE</c> and is not exercised anywhere else under real
/// two-node contention.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class ClusteredHardeningSqlServerTest : ClusteredHardeningTestBase
{
    public ClusteredHardeningSqlServerTest() : base(TestConstants.DefaultSqlServerProvider)
    {
    }

    /// <summary>
    /// The same race driven through <see cref="Quartz.Impl.AdoJobStore.UpdateRowLockHandler"/> rather than
    /// the handler the store picks for itself. It is reachable from configuration and nothing else in the
    /// suite runs its update-or-insert against a real database — the unit tests only prove it retries in
    /// front of a fake provider.
    /// </summary>
    /// <remarks>
    /// SQL Server only, deliberately. When two nodes first contend for a lock name the row does not exist
    /// yet, so both fall through to the INSERT and one takes a primary key violation; SQL Server leaves
    /// the transaction usable so the handler's retry takes the lock, while PostgreSQL marks the whole
    /// transaction aborted and the retry would fail too. That is the reason PostgreSQL gets its own
    /// lock handler, and the reason this test would be testing a broken combination if it ran there.
    /// </remarks>
    [Test]
    public Task TwoNodes_EveryOneShotTriggerFiresExactlyOnce_WithUpdateRowLockHandler()
    {
        return AssertNoDoubleFire(configure: properties =>
            properties["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.UpdateRowLockHandler, Quartz");
    }
}
