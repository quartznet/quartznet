using System;
using System.Data.Common;

using Microsoft.Data.SqlClient;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The failed fire against SQL Server, which keeps the transaction alive after a failed statement, so
/// the commit used to keep the half-applied fire: its job's other triggers <c>BLOCKED</c>.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class TriggerFireFailureSqlServerTest : TriggerFireFailureTestBase
{
    protected override string Provider => TestConstants.DefaultSqlServerProvider;

    protected override Type FaultingDelegate => typeof(FaultingSqlServerDelegate);

    protected override string ConnectionString => TestConstants.SqlServerConnectionString;

    protected override DbConnection CreateConnection() => new SqlConnection(ConnectionString);
}
