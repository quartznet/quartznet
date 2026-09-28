using Quartz.Configuration;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The failed fire against Oracle.
/// </summary>
[Category("db-oracle")]
[NonParallelizable]
public sealed class TriggerFireFailureOracleTest : TriggerFireFailureTestBase
{
    public TriggerFireFailureOracleTest() : base(DataSourceOptions.Providers.Oracle, typeof(FaultingOracleDelegate))
    {
    }
}
