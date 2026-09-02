namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The non-overlap property against PostgreSQL, whose acquisition locks through
/// <c>PostgreSqlSelectForUpdateLockHandler</c> — so the block is held by a row lock rather than by an
/// updated lock row, which is the arrangement most likely to let two nodes past it.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredSerialJobPostgresTest : ClusteredSerialJobTestBase
{
    public ClusteredSerialJobPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
