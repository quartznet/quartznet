namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The one-at-a-time acquisition scenario on SQL Server with <c>READ_COMMITTED_SNAPSHOT</c> on, where a
/// round of concurrent-allowed work takes no lock and fires apart (#4117): each node still fires its own
/// pinned trigger on time while a serial job runs on the other.
/// </summary>
/// <remarks>
/// <c>ClusteredAcquisitionSkipSqlServerTest</c> runs the same scenario on SQL Server's default, where every
/// round is 4.5's. The folded round of 4.6 failed this one: nodes acquiring one trigger at a time ran
/// seconds late, locked or not.
/// </remarks>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class ClusteredAcquisitionSkipSqlServerSnapshotTest : ClusteredAcquisitionSkipTestBase
{
    public ClusteredAcquisitionSkipSqlServerSnapshotTest() : base(ClusteredTestDatabase.SqlServerSnapshot)
    {
    }
}
