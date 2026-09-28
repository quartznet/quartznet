using Quartz.Configuration;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The failed fire against Firebird.
/// </summary>
[Category("db-firebird")]
[NonParallelizable]
public sealed class TriggerFireFailureFirebirdTest : TriggerFireFailureTestBase
{
    public TriggerFireFailureFirebirdTest() : base(DataSourceOptions.Providers.Firebird, typeof(FaultingFirebirdDelegate))
    {
    }
}
