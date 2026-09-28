namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The one-at-a-time acquisition scenario against PostgreSQL, whose lock-free single-trigger
/// acquisition is the arrangement the G8 gate found the starvation on.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredAcquisitionSkipPostgresTest : ClusteredAcquisitionSkipTestBase
{
    public ClusteredAcquisitionSkipPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
