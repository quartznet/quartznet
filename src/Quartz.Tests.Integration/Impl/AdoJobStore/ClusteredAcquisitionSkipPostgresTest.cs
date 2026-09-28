using System.Data.Common;

using Npgsql;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The one-at-a-time acquisition scenario against PostgreSQL, whose lock-free single-trigger
/// acquisition is the arrangement the starvation was found on.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredAcquisitionSkipPostgresTest : ClusteredAcquisitionSkipTestBase
{
    protected override string Provider => TestConstants.PostgresProvider;

    protected override string DriverDelegateType => "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";

    protected override string ConnectionString => TestConstants.PostgresConnectionString;

    protected override DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);
}
