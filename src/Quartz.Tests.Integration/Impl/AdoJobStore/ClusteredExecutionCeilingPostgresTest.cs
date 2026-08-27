namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The cluster-scoped ceiling against PostgreSQL, whose acquisition locks through
/// <c>PostgreSqlSelectForUpdateLockHandler</c> and whose <c>COUNT(*)</c> comes back as a 64-bit integer.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredExecutionCeilingPostgresTest : ClusteredExecutionCeilingTestBase
{
    public ClusteredExecutionCeilingPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
