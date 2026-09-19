namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The cross-node continuation against PostgreSQL, whose acquisition locks through
/// <c>PostgreSqlSelectForUpdateLockHandler</c> — a row lock rather than an updated lock row, which is
/// the arrangement where a settlement written inside the parent's transaction is most likely to be
/// seen half-done by the node acquiring next.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredContinuationPostgresTest : ClusteredContinuationTestBase
{
    public ClusteredContinuationPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
