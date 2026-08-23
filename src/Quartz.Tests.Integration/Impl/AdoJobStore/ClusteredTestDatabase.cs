using System.Data.Common;

using Microsoft.Data.SqlClient;

using Npgsql;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The database a clustered fixture runs against: everything <see cref="ClusteredJobStoreTestBase"/>
/// needs in order to be written once and executed against more than one engine. Instances are
/// stateless singletons; the containers themselves are started once per assembly by
/// <see cref="TestcontainersDatabaseEnvironment"/> and are only addressed here by connection string.
/// </summary>
public abstract class ClusteredTestDatabase
{
    /// <summary>
    /// The assembly-wide PostgreSQL database.
    /// </summary>
    public static ClusteredTestDatabase Postgres { get; } = new PostgresTestDatabase();

    /// <summary>
    /// The assembly-wide SQL Server database.
    /// </summary>
    public static ClusteredTestDatabase SqlServer { get; } = new SqlServerTestDatabase();

    /// <summary>
    /// Resolves the database for a <c>quartz.dataSource.default.provider</c> value, which is what an
    /// NUnit <c>[TestFixture]</c> argument can carry — attribute arguments have to be constants, and
    /// the provider names already are.
    /// </summary>
    public static ClusteredTestDatabase For(string provider) => provider switch
    {
        TestConstants.PostgresProvider => Postgres,
        TestConstants.DefaultSqlServerProvider => SqlServer,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "no clustered test database for this provider")
    };

    /// <summary>
    /// The ADO.NET provider name, as <c>quartz.dataSource.default.provider</c> spells it.
    /// </summary>
    public abstract string Provider { get; }

    /// <summary>
    /// The connection string of the container this assembly started.
    /// </summary>
    public abstract string ConnectionString { get; }

    /// <summary>
    /// The <c>quartz.jobStore.driverDelegateType</c> for this engine.
    /// </summary>
    public abstract string DriverDelegateType { get; }

    /// <summary>
    /// Opens a connection of this engine's own type, for the fixtures' direct SQL. Test SQL is written
    /// in unquoted upper case, which SQL Server matches case-insensitively and PostgreSQL folds to the
    /// lower-case identifiers its scripts create, so the statements themselves stay dialect-neutral.
    /// </summary>
    public abstract DbConnection CreateConnection();

    public override string ToString() => Provider;

    private sealed class PostgresTestDatabase : ClusteredTestDatabase
    {
        public override string Provider => TestConstants.PostgresProvider;

        public override string ConnectionString => TestConstants.PostgresConnectionString;

        public override string DriverDelegateType => "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";

        public override DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);
    }

    private sealed class SqlServerTestDatabase : ClusteredTestDatabase
    {
        public override string Provider => TestConstants.DefaultSqlServerProvider;

        public override string ConnectionString => TestConstants.SqlServerConnectionString;

        public override string DriverDelegateType => "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

        public override DbConnection CreateConnection() => new SqlConnection(ConnectionString);
    }
}
