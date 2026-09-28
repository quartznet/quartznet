using Quartz.Configuration;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The failed fire against MySQL, which keeps the transaction alive after a failed statement, as SQL
/// Server does.
/// </summary>
[Category("db-mysql")]
[NonParallelizable]
public sealed class TriggerFireFailureMySqlTest : TriggerFireFailureTestBase
{
    public TriggerFireFailureMySqlTest() : base(DataSourceOptions.Providers.MySqlConnector, typeof(FaultingMySQLDelegate))
    {
    }
}
