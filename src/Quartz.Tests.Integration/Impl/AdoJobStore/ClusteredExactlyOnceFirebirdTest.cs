using Quartz.Configuration;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two contending nodes against Firebird. Until now the Firebird leg ran the smoke test — with
/// clustering off — the paging test and the schema test, so nothing anywhere said whether a Firebird
/// store hands each due trigger to one node.
/// </summary>
[Category("db-firebird")]
[NonParallelizable]
public sealed class ClusteredExactlyOnceFirebirdTest : ClusteredExactlyOnceTestBase
{
    public ClusteredExactlyOnceFirebirdTest() : base(DataSourceOptions.Providers.Firebird)
    {
    }
}
