using System;
using System.Data.Common;

using Npgsql;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The failed fire against PostgreSQL, the engine on which a failed statement aborts the transaction:
/// every fire after it failed too, and the commit rolled back the fires reported before it.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class TriggerFireFailurePostgresTest : TriggerFireFailureTestBase
{
    protected override string Provider => TestConstants.PostgresProvider;

    protected override Type FaultingDelegate => typeof(FaultingPostgreSQLDelegate);

    protected override string ConnectionString => TestConstants.PostgresConnectionString;

    protected override DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);
}
