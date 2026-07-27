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
    /// store.UseGenericDatabase("MyDatabase", connectionString, metadata =>
    /// {
    ///     metadata.ProductName = "My Database";
    ///     metadata.AssemblyName = typeof(MyConnection).Assembly.FullName;
    ///     metadata.ConnectionType = typeof(MyConnection);
    ///     metadata.CommandType = typeof(MyCommand);
    ///     metadata.ParameterType = typeof(MyParameter);
    ///     metadata.ParameterDbType = typeof(MyDbType);
    ///     metadata.ParameterDbTypePropertyName = nameof(MyParameter.MyDbType);
    ///     metadata.ParameterNamePrefix = "@";
    ///     metadata.ExceptionType = typeof(MyException);
    ///     metadata.UseParameterNamePrefixInParameterCollection = true;
    ///     metadata.BindByName = true;
    ///     metadata.DbBinaryTypeName = "VarBinary";
    /// });
    /// </code>
    /// </example>
    /// <param name="builder">The store being configured.</param>
    /// <param name="provider">The provider name the driver description is registered under.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="configureMetadata">Describes the ADO.NET driver.</param>
    public static IPersistentStoreBuilder UseGenericDatabase(
        this IPersistentStoreBuilder builder,
        string provider,
        string connectionString,
        Action<DbMetadata> configureMetadata)
    {
        DescribeDbProvider(builder, provider, configureMetadata);
        return builder.UseDatabase<StdAdoDelegate>(provider, connectionString);
    }

    /// <summary>
    /// Stores the schedule in a database Quartz ships no ADO.NET driver description for, describing both
    /// the data source and the driver in code.
    /// </summary>
    /// <param name="builder">The store being configured.</param>
    /// <param name="provider">The provider name the driver description is registered under.</param>
    /// <param name="configureDataSource">Configures the data source, for example a named connection string.</param>
    /// <param name="configureMetadata">Describes the ADO.NET driver.</param>
    public static IPersistentStoreBuilder UseGenericDatabase(
        this IPersistentStoreBuilder builder,
        string provider,
        Action<DataSourceOptions> configureDataSource,
        Action<DbMetadata> configureMetadata)
    {
        DescribeDbProvider(builder, provider, configureMetadata);
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
    /// <see cref="DbMetadata.Initialize"/> is called here rather than later because it is what turns the
    /// settable bag into usable metadata — it resolves the binary column type and the parameter's db type
    /// property by reflection. Doing it now means a description that cannot work fails while the
    /// container is being configured rather than when the first command is built.
    /// </para>
    /// </remarks>
    private static void DescribeDbProvider(
        IPersistentStoreBuilder builder,
        string provider,
        Action<DbMetadata> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(configure);

        var metadata = new DbMetadata();
        configure(metadata);
        metadata.Initialize();

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
