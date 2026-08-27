using System.Data.Common;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Data.SqlClient;

using MySqlConnector;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

using Quartz.Configuration;
using Quartz.Impl.AdoJobStore;

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
    /// The assembly-wide MySQL database.
    /// </summary>
    public static ClusteredTestDatabase MySql { get; } = new MySqlTestDatabase();

    /// <summary>
    /// The assembly-wide Oracle database.
    /// </summary>
    public static ClusteredTestDatabase Oracle { get; } = new OracleTestDatabase();

    /// <summary>
    /// The assembly-wide Firebird database.
    /// </summary>
    public static ClusteredTestDatabase Firebird { get; } = new FirebirdTestDatabase();

    /// <summary>
    /// Resolves the database for a <c>quartz.dataSource.default.provider</c> value, which is what an
    /// NUnit <c>[TestFixture]</c> argument can carry — attribute arguments have to be constants, and
    /// the provider names already are.
    /// </summary>
    public static ClusteredTestDatabase For(string provider) => provider switch
    {
        TestConstants.PostgresProvider => Postgres,
        TestConstants.DefaultSqlServerProvider => SqlServer,
        DataSourceOptions.Providers.MySqlConnector => MySql,
        DataSourceOptions.Providers.Oracle => Oracle,
        DataSourceOptions.Providers.Firebird => Firebird,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "no clustered test database for this provider")
    };

    /// <summary>
    /// What this engine's driver spells a parameter placeholder with in statement text. Every fixture
    /// writes <c>@name</c>, which is what all but one of them use;
    /// <see cref="ClusteredJobStoreTestBase.ExecuteNonQuery" /> rewrites it for the one that does not.
    /// </summary>
    /// <remarks>
    /// This is the test side of what <c>AdoUtil.AddCommandParameter</c> does for the store's own
    /// statements. It is a property of the driver rather than of the SQL, which is why the fixtures do
    /// not each have to know about it.
    /// </remarks>
    public virtual string ParameterPrefix => "@";

    /// <summary>
    /// The connection string the container this assembly started published, read from the environment
    /// variable it publishes it in.
    /// </summary>
    /// <remarks>
    /// Read rather than defaulted: a container is the only supported way to get a <c>db-*</c> leg going,
    /// so an empty value means the container never started, and saying that is worth more than timing
    /// out against whatever else happens to be on localhost.
    /// </remarks>
    protected static string ContainerConnectionString(string variableName)
    {
        string connectionString = Environment.GetEnvironmentVariable(variableName);

        connectionString.Should().NotBeNullOrWhiteSpace(
            "{0} is set by the container this assembly starts, so an empty one means the container for "
            + "this leg never started — run the fixture through its own QUARTZ_TEST_DATABASE leg",
            variableName);

        return connectionString;
    }

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
    /// This engine's driver delegate, for a fixture that constructs a job store directly instead of
    /// through configuration. <see cref="DriverDelegateType"/> is a name for the property bridge to
    /// resolve, which is no use to a caller holding a constructor.
    /// </summary>
    public abstract IDriverDelegate CreateDriverDelegate();

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

        public override IDriverDelegate CreateDriverDelegate() => new PostgreSQLDelegate();

        public override DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);
    }

    private sealed class SqlServerTestDatabase : ClusteredTestDatabase
    {
        public override string Provider => TestConstants.DefaultSqlServerProvider;

        public override string ConnectionString => TestConstants.SqlServerConnectionString;

        public override string DriverDelegateType => "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

        public override IDriverDelegate CreateDriverDelegate() => new SqlServerDelegate();

        public override DbConnection CreateConnection() => new SqlConnection(ConnectionString);
    }

    private sealed class MySqlTestDatabase : ClusteredTestDatabase
    {
        public override string Provider => DataSourceOptions.Providers.MySqlConnector;

        public override string ConnectionString => ContainerConnectionString("MYSQL_CONNECTION_STRING");

        public override string DriverDelegateType => "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";

        public override IDriverDelegate CreateDriverDelegate() => new MySQLDelegate();

        public override DbConnection CreateConnection() => new MySqlConnection(ConnectionString);
    }

    private sealed class OracleTestDatabase : ClusteredTestDatabase
    {
        public override string Provider => DataSourceOptions.Providers.Oracle;

        public override string ConnectionString => ContainerConnectionString("ORACLE_CONNECTION_STRING");

        public override string DriverDelegateType => "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";

        /// <summary>
        /// The one driver here that does not spell a placeholder <c>@name</c>.
        /// </summary>
        public override string ParameterPrefix => ":";

        public override IDriverDelegate CreateDriverDelegate() => new OracleDelegate();

        public override DbConnection CreateConnection() => new OracleConnection(ConnectionString);
    }

    private sealed class FirebirdTestDatabase : ClusteredTestDatabase
    {
        public override string Provider => DataSourceOptions.Providers.Firebird;

        public override string ConnectionString => ContainerConnectionString("FIREBIRD_CONNECTION_STRING");

        public override string DriverDelegateType => "Quartz.Impl.AdoJobStore.FirebirdDelegate, Quartz";

        public override IDriverDelegate CreateDriverDelegate() => new FirebirdDelegate();

        public override DbConnection CreateConnection() => new FbConnection(ConnectionString);
    }
}
