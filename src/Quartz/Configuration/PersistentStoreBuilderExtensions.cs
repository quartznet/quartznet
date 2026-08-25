using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

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
/// <c>UseSqlite</c> and <c>UseSystemDataSqlite</c>, choose between different ADO.NET drivers for the
/// same database. In each pair the short name is the driver to reach for, and the longer name says
/// which other driver it is.
/// </para>
/// </remarks>
public static class PersistentStoreBuilderExtensions
{
    /// <summary>Stores the schedule in Microsoft SQL Server.</summary>
    public static IPersistentStoreBuilder UseSqlServer(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SqlServerDelegate>(DataSourceOptions.Providers.SqlServer, connectionString);

    /// <summary>Stores the schedule in Microsoft SQL Server.</summary>
    public static IPersistentStoreBuilder UseSqlServer(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SqlServerDelegate>(DataSourceOptions.Providers.SqlServer, configure);

    /// <summary>Stores the schedule in PostgreSQL.</summary>
    public static IPersistentStoreBuilder UsePostgres(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<PostgreSQLDelegate>(DataSourceOptions.Providers.Npgsql, connectionString);

    /// <summary>Stores the schedule in PostgreSQL.</summary>
    public static IPersistentStoreBuilder UsePostgres(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<PostgreSQLDelegate>(DataSourceOptions.Providers.Npgsql, configure);

    /// <summary>Stores the schedule in MySQL, using the MySql.Data driver.</summary>
    public static IPersistentStoreBuilder UseMySql(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySql, connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySql.Data driver.</summary>
    public static IPersistentStoreBuilder UseMySql(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySql, configure);

    /// <summary>Stores the schedule in MySQL, using the MySqlConnector driver.</summary>
    public static IPersistentStoreBuilder UseMySqlConnector(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySqlConnector, connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySqlConnector driver.</summary>
    public static IPersistentStoreBuilder UseMySqlConnector(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySqlConnector, configure);

    /// <summary>Stores the schedule in Firebird.</summary>
    public static IPersistentStoreBuilder UseFirebird(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<FirebirdDelegate>(DataSourceOptions.Providers.Firebird, connectionString);

    /// <summary>Stores the schedule in Firebird.</summary>
    public static IPersistentStoreBuilder UseFirebird(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<FirebirdDelegate>(DataSourceOptions.Providers.Firebird, configure);

    /// <summary>Stores the schedule in Oracle.</summary>
    public static IPersistentStoreBuilder UseOracle(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<OracleDelegate>(DataSourceOptions.Providers.Oracle, connectionString);

    /// <summary>Stores the schedule in Oracle.</summary>
    public static IPersistentStoreBuilder UseOracle(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<OracleDelegate>(DataSourceOptions.Providers.Oracle, configure);

    /// <summary>Stores the schedule in SQLite, using the Microsoft.Data.Sqlite driver.</summary>
    /// <remarks>
    /// The modern driver, and what <c>UseSqlite</c> means: the short name goes to the default the way
    /// <c>UseMySql</c> does, and the way Entity Framework Core spells the same choice. The legacy
    /// System.Data.SQLite driver is <see cref="UseSystemDataSqlite(IPersistentStoreBuilder, string)"/>.
    /// </remarks>
    public static IPersistentStoreBuilder UseSqlite(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.Sqlite, connectionString);

    /// <inheritdoc cref="UseSqlite(IPersistentStoreBuilder, string)"/>
    public static IPersistentStoreBuilder UseSqlite(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.Sqlite, configure);

    /// <summary>Stores the schedule in SQLite, using the legacy System.Data.SQLite driver.</summary>
    /// <remarks>
    /// Named after its driver rather than after the database, because
    /// <see cref="UseSqlite(IPersistentStoreBuilder, string)"/> is the one to reach for.
    /// </remarks>
    public static IPersistentStoreBuilder UseSystemDataSqlite(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.SystemDataSqlite, connectionString);

    /// <inheritdoc cref="UseSystemDataSqlite(IPersistentStoreBuilder, string)"/>
    public static IPersistentStoreBuilder UseSystemDataSqlite(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.SystemDataSqlite, configure);

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

    /// <summary>
    /// Stores the schedule in a database Quartz has no specific support for, using the generic SQL
    /// dialect.
    /// </summary>
    /// <param name="builder">The store being configured.</param>
    /// <param name="provider">The Quartz provider name identifying the ADO.NET driver.</param>
    /// <param name="configure">Configures the data source, for example a named connection string.</param>
    public static IPersistentStoreBuilder UseGenericDatabase(
        this IPersistentStoreBuilder builder,
        string provider,
        Action<DataSourceOptions> configure)
        => builder.UseDatabase<StdAdoDelegate>(provider, configure);

    /// <summary>
    /// Stores the schedule in a database Quartz ships no ADO.NET driver description for, describing the
    /// driver in code.
    /// </summary>
    /// <remarks>
    /// This is the code-first form of the <c>quartz.dbprovider.&lt;name&gt;.*</c> keys: it says which
    /// connection, command and parameter types to instantiate, how parameters are named, and which enum
    /// value means "binary column". Registering a description under a name Quartz already ships one for
    /// replaces it.
    /// </remarks>
    /// <example>
    /// <code>
    /// store.UseGenericDatabase("MyDatabase", connectionString, () => new DbMetadata
    /// {
    ///     ProductName = "My Database",
    ///     AssemblyName = typeof(MyConnection).Assembly.FullName,
    ///     ConnectionType = typeof(MyConnection),
    ///     CommandType = typeof(MyCommand),
    ///     ParameterType = typeof(MyParameter),
    ///     ParameterDbType = typeof(MyDbType),
    ///     ParameterDbTypePropertyName = nameof(MyParameter.MyDbType),
    ///     ParameterNamePrefix = "@",
    ///     ExceptionType = typeof(MyException),
    ///     UseParameterNamePrefixInParameterCollection = true,
    ///     BindByName = true,
    ///     DbBinaryTypeName = "VarBinary",
    /// });
    /// </code>
    /// </example>
    /// <param name="builder">The store being configured.</param>
    /// <param name="provider">The provider name the driver description is registered under.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="describeMetadata">Builds the ADO.NET driver description.</param>
    public static IPersistentStoreBuilder UseGenericDatabase(
        this IPersistentStoreBuilder builder,
        string provider,
        string connectionString,
        Func<DbMetadata> describeMetadata)
    {
        DescribeDbProvider(builder, provider, describeMetadata);
        return builder.UseDatabase<StdAdoDelegate>(provider, connectionString);
    }

    /// <summary>
    /// Stores the schedule in a database Quartz ships no ADO.NET driver description for, describing both
    /// the data source and the driver in code.
    /// </summary>
    /// <param name="builder">The store being configured.</param>
    /// <param name="provider">The provider name the driver description is registered under.</param>
    /// <param name="configureDataSource">Configures the data source, for example a named connection string.</param>
    /// <param name="describeMetadata">Builds the ADO.NET driver description.</param>
    public static IPersistentStoreBuilder UseGenericDatabase(
        this IPersistentStoreBuilder builder,
        string provider,
        Action<DataSourceOptions> configureDataSource,
        Func<DbMetadata> describeMetadata)
    {
        DescribeDbProvider(builder, provider, describeMetadata);
        return builder.UseDatabase<StdAdoDelegate>(provider, configureDataSource);
    }

    /// <summary>
    /// Registers a driver description as a metadata factory in the container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added rather than tried, so describing two providers registers two descriptions instead of the
    /// second one silently losing to the first.
    /// </para>
    /// <para>
    /// The metadata is immutable once the callback returns: the binary column type and the parameter's
    /// db type property derive from the described values on first use, so a description that cannot
    /// work fails when the first binary parameter is bound rather than needing a separate
    /// initialization step here.
    /// </para>
    /// </remarks>
    private static void DescribeDbProvider(
        IPersistentStoreBuilder builder,
        string provider,
        Func<DbMetadata> describe)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(describe);

        DbMetadata metadata = describe();
        metadata.Validate();

        builder.Services.AddSingleton<DbMetadataFactory>(new ConfiguredDbMetadataFactory(provider, metadata));
    }

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
