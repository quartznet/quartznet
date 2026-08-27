namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The cluster-scoped ceiling against SQL Server, whose acquisition locks through
/// <c>SelectForUpdateLockHandler</c> with the <c>(UPDLOCK,ROWLOCK)</c> hint and whose <c>COUNT(*)</c>
/// comes back as a 32-bit integer.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class ClusteredExecutionCeilingSqlServerTest : ClusteredExecutionCeilingTestBase
{
    public ClusteredExecutionCeilingSqlServerTest() : base(TestConstants.DefaultSqlServerProvider)
    {
    }
}
