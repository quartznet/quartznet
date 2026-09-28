namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The failed fire against SQL Server, which keeps the transaction alive after a failed statement, so
/// that the fire's earlier writes were committed with the batch.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class TriggerFireFailureSqlServerTest : TriggerFireFailureTestBase
{
    public TriggerFireFailureSqlServerTest() : base(TestConstants.DefaultSqlServerProvider, typeof(FaultingSqlServerDelegate))
    {
    }
}
