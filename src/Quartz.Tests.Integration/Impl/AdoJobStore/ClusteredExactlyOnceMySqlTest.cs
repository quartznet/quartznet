using Quartz.Configuration;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two contending nodes against MySQL, whose store locks with <c>SELECT … FOR UPDATE</c> through
/// <see cref="Quartz.Impl.AdoJobStore.SelectForUpdateLockHandler" /> and pages with
/// <c>MySQLDelegate</c>'s own <c>LIMIT</c>. Until now the MySQL leg ran the smoke test, the paging test
/// and the schema test, and nothing that put two nodes on one database at once.
/// </summary>
[Category("db-mysql")]
[NonParallelizable]
public sealed class ClusteredExactlyOnceMySqlTest : ClusteredExactlyOnceTestBase
{
    public ClusteredExactlyOnceMySqlTest() : base(DataSourceOptions.Providers.MySqlConnector)
    {
    }
}
