namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered hardening scenarios against PostgreSQL, whose store locks through
/// <c>PostgreSqlSelectForUpdateSemaphore</c>.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredHardeningPostgresTest : ClusteredHardeningTestBase
{
    public ClusteredHardeningPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
