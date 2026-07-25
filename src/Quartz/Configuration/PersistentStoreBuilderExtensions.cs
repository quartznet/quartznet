using System.Diagnostics.CodeAnalysis;

using Quartz.Impl.AdoJobStore;

namespace Quartz;

/// <summary>
/// Database-specific configuration for a persistent job store.
/// </summary>
/// <remarks>
/// <para>
/// Each method selects the driver delegate that speaks the right SQL dialect and the ADO.NET provider
/// that talks to it, so a connection string is all a caller has to supply — the shape every
/// Entity Framework Core user already knows.
/// </para>
/// <para>
/// The pairs that look redundant are not: <c>UseMySql</c> and <c>UseMySqlConnector</c>, like
/// <c>UseSQLite</c> and <c>UseMicrosoftSQLite</c>, choose between different ADO.NET drivers for the
/// same database.
/// </para>
/// </remarks>
public static class PersistentStoreBuilderExtensions
{
    /// <summary>Stores the schedule in Microsoft SQL Server.</summary>
    public static IPersistentStoreBuilder UseSqlServer(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SqlServerDelegate>("SqlServer", connectionString);

    /// <summary>Stores the schedule in Microsoft SQL Server.</summary>
    public static IPersistentStoreBuilder UseSqlServer(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SqlServerDelegate>("SqlServer", configure);

    /// <summary>Stores the schedule in PostgreSQL.</summary>
    public static IPersistentStoreBuilder UsePostgres(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<PostgreSQLDelegate>("Npgsql", connectionString);

    /// <summary>Stores the schedule in PostgreSQL.</summary>
    public static IPersistentStoreBuilder UsePostgres(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<PostgreSQLDelegate>("Npgsql", configure);

    /// <summary>Stores the schedule in MySQL, using the MySql.Data driver.</summary>
    public static IPersistentStoreBuilder UseMySql(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<MySQLDelegate>("MySql", connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySql.Data driver.</summary>
    public static IPersistentStoreBuilder UseMySql(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<MySQLDelegate>("MySql", configure);

    /// <summary>Stores the schedule in MySQL, using the MySqlConnector driver.</summary>
    public static IPersistentStoreBuilder UseMySqlConnector(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<MySQLDelegate>("MySqlConnector", connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySqlConnector driver.</summary>
    public static IPersistentStoreBuilder UseMySqlConnector(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<MySQLDelegate>("MySqlConnector", configure);

    /// <summary>Stores the schedule in Firebird.</summary>
    public static IPersistentStoreBuilder UseFirebird(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<FirebirdDelegate>("Firebird", connectionString);

    /// <summary>Stores the schedule in Firebird.</summary>
    public static IPersistentStoreBuilder UseFirebird(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<FirebirdDelegate>("Firebird", configure);

    /// <summary>Stores the schedule in Oracle.</summary>
    public static IPersistentStoreBuilder UseOracle(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<OracleDelegate>("OracleODPManaged", connectionString);

    /// <summary>Stores the schedule in Oracle.</summary>
    public static IPersistentStoreBuilder UseOracle(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<OracleDelegate>("OracleODPManaged", configure);

    /// <summary>Stores the schedule in SQLite, using the System.Data.SQLite driver.</summary>
    public static IPersistentStoreBuilder UseSQLite(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>("SQLite", connectionString);

    /// <summary>Stores the schedule in SQLite, using the System.Data.SQLite driver.</summary>
    public static IPersistentStoreBuilder UseSQLite(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SQLiteDelegate>("SQLite", configure);

    /// <summary>Stores the schedule in SQLite, using the Microsoft.Data.Sqlite driver.</summary>
    public static IPersistentStoreBuilder UseMicrosoftSQLite(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>("SQLite-Microsoft", connectionString);

    /// <summary>Stores the schedule in SQLite, using the Microsoft.Data.Sqlite driver.</summary>
    public static IPersistentStoreBuilder UseMicrosoftSQLite(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SQLiteDelegate>("SQLite-Microsoft", configure);

    /// <summary>
    /// Stores the schedule in a database Quartz has no specific support for, using the generic SQL
    /// dialect.
    /// </summary>
    /// <param name="builder">The store being configured.</param>
    /// <param name="provider">The Quartz provider name identifying the ADO.NET driver.</param>
    /// <param name="connectionString">The connection string.</param>
    public static IPersistentStoreBuilder UseGenericDatabase(
        this IPersistentStoreBuilder builder,
        string provider,
        string connectionString)
        => builder.UseDatabase<StdAdoDelegate>(provider, connectionString);

    private static IPersistentStoreBuilder UseDatabase<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TDelegate>(
        this IPersistentStoreBuilder builder,
        string provider,
        string connectionString) where TDelegate : class, IDriverDelegate
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseDatabase<TDelegate>(provider, options => options.ConnectionString = connectionString);
    }

    private static IPersistentStoreBuilder UseDatabase<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TDelegate>(
        this IPersistentStoreBuilder builder,
        string provider,
        Action<DataSourceOptions> configure) where TDelegate : class, IDriverDelegate
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.UseDriverDelegate<TDelegate>();
        return builder.UseDataSource(options =>
        {
            options.Provider = provider;
            configure(options);
        });
    }
}
