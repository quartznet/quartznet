using Quartz.Configuration;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two contending nodes against Oracle, whose store locks with <c>SELECT … FOR UPDATE</c> through
/// <see cref="Quartz.Impl.AdoJobStore.SelectForUpdateLockHandler" /> and whose driver is the only one
/// here that binds parameters with <c>:</c> rather than <c>@</c>. Until now the Oracle leg ran the
/// smoke test, the paging test and the schema test, and nothing that put two nodes on one database at
/// once.
/// </summary>
[Category("db-oracle")]
[NonParallelizable]
public sealed class ClusteredExactlyOnceOracleTest : ClusteredExactlyOnceTestBase
{
    public ClusteredExactlyOnceOracleTest() : base(DataSourceOptions.Providers.Oracle)
    {
    }
}
