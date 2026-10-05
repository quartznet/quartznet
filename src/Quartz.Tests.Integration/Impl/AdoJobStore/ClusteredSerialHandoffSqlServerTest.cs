namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The serial job's hand-off between two nodes against SQL Server.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class ClusteredSerialHandoffSqlServerTest : ClusteredSerialHandoffTestBase
{
    public ClusteredSerialHandoffSqlServerTest() : base(TestConstants.DefaultSqlServerProvider)
    {
    }
}
