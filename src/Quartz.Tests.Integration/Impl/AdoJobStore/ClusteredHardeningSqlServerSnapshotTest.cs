namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Exactly-once and the clustered hardening scenarios on SQL Server with <c>READ_COMMITTED_SNAPSHOT</c> on,
/// the one SQL Server configuration whose firing rounds take no lock (#4117): a round claims in one
/// transaction and fires in a second, and the claim's compare-and-swap and the fire's guard keep two nodes
/// apart.
/// </summary>
/// <remarks>
/// <c>ClusteredHardeningSqlServerTest</c> runs the same cases on SQL Server's default, where every round is
/// locked for <c>read_committed_snapshot_off</c>.
/// </remarks>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class ClusteredHardeningSqlServerSnapshotTest : ClusteredHardeningTestBase
{
    public ClusteredHardeningSqlServerSnapshotTest() : base(ClusteredTestDatabase.SqlServerSnapshot)
    {
    }
}
