using System.Data.Common;

using Microsoft.Data.SqlClient;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The one-at-a-time acquisition scenario against SQL Server, whose row-hinted lock handler
/// serializes the fire that blocks the other node's reservation differently from PostgreSQL's.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class ClusteredAcquisitionSkipSqlServerTest : ClusteredAcquisitionSkipTestBase
{
    protected override string Provider => TestConstants.DefaultSqlServerProvider;

    protected override string DriverDelegateType => "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

    protected override string ConnectionString => TestConstants.SqlServerConnectionString;

    protected override DbConnection CreateConnection() => new SqlConnection(ConnectionString);
}
