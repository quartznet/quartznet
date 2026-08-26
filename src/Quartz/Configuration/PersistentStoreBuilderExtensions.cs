using System.Data.Common;
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
/// <para>
/// Each of them also takes the driver's <see cref="DbProviderFactory"/> instead of only a connection
/// string — <c>UseSqlServer(SqlClientFactory.Instance, connectionString)</c>. That overload names no
/// type: it asks the factory for connections rather than resolving the driver's types from strings, so
/// it is the one a trimmed or ahead-of-time-compiled application uses. The others resolve the driver by
/// name and say so.
/// </para>
/// </remarks>
public static class PersistentStoreBuilderExtensions
{
    /// <summary>
    /// What every overload that chooses a driver by name has to say for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It surfaces inside the application's <c>UsePersistentStore</c> callback, which is where the
    /// decision is made and where the answer is: the same registration with the driver's factory, or a
    /// <see cref="System.Data.Common.DbDataSource"/> in the container. It stops there — <c>AddQuartz</c>
    /// does not carry it, because these are extension methods nothing inside Quartz calls.
    /// </para>
    /// </remarks>
    private const string NamesTheDriversTypes =
        "The driver is chosen by name, and Quartz names its connection, command and parameter types as strings, "
        + "so a trimmed application has no guarantee they survived. Pass the driver's DbProviderFactory to the "
        + "overload that takes one, or register a DbDataSource in the container.";

    /// <summary>Stores the schedule in Microsoft SQL Server.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseSqlServer(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SqlServerDelegate>(DataSourceOptions.Providers.SqlServer, connectionString);

    /// <summary>Stores the schedule in Microsoft SQL Server.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseSqlServer(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SqlServerDelegate>(DataSourceOptions.Providers.SqlServer, configure);

    /// <summary>
    /// Stores the schedule in Microsoft SQL Server, reached through
    /// <c>Microsoft.Data.SqlClient.SqlClientFactory.Instance</c>.
    /// </summary>
    /// <inheritdoc cref="UseDatabase{TDelegate}(IPersistentStoreBuilder, string, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})" path="/remarks"/>
    public static IPersistentStoreBuilder UseSqlServer(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<SqlServerDelegate>(DataSourceOptions.Providers.SqlServer, factory, connectionString);

    /// <summary>Stores the schedule in PostgreSQL.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UsePostgres(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<PostgreSQLDelegate>(DataSourceOptions.Providers.Npgsql, connectionString);

    /// <summary>Stores the schedule in PostgreSQL.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UsePostgres(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<PostgreSQLDelegate>(DataSourceOptions.Providers.Npgsql, configure);

    /// <summary>Stores the schedule in PostgreSQL, reached through <c>Npgsql.NpgsqlFactory.Instance</c>.</summary>
    /// <inheritdoc cref="UseDatabase{TDelegate}(IPersistentStoreBuilder, string, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})" path="/remarks"/>
    public static IPersistentStoreBuilder UsePostgres(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<PostgreSQLDelegate>(DataSourceOptions.Providers.Npgsql, factory, connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySql.Data driver.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseMySql(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySql, connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySql.Data driver.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseMySql(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySql, configure);

    /// <summary>
    /// Stores the schedule in MySQL, reached through
    /// <c>MySql.Data.MySqlClient.MySqlClientFactory.Instance</c>.
    /// </summary>
    /// <inheritdoc cref="UseDatabase{TDelegate}(IPersistentStoreBuilder, string, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})" path="/remarks"/>
    public static IPersistentStoreBuilder UseMySql(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySql, factory, connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySqlConnector driver.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseMySqlConnector(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySqlConnector, connectionString);

    /// <summary>Stores the schedule in MySQL, using the MySqlConnector driver.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseMySqlConnector(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySqlConnector, configure);

    /// <summary>
    /// Stores the schedule in MySQL, reached through
    /// <c>MySqlConnector.MySqlConnectorFactory.Instance</c>.
    /// </summary>
    /// <inheritdoc cref="UseDatabase{TDelegate}(IPersistentStoreBuilder, string, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})" path="/remarks"/>
    public static IPersistentStoreBuilder UseMySqlConnector(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<MySQLDelegate>(DataSourceOptions.Providers.MySqlConnector, factory, connectionString);

    /// <summary>Stores the schedule in Firebird.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseFirebird(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<FirebirdDelegate>(DataSourceOptions.Providers.Firebird, connectionString);

    /// <summary>Stores the schedule in Firebird.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseFirebird(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<FirebirdDelegate>(DataSourceOptions.Providers.Firebird, configure);

    /// <summary>
    /// Stores the schedule in Firebird, reached through
    /// <c>FirebirdSql.Data.FirebirdClient.FirebirdClientFactory.Instance</c>.
    /// </summary>
    /// <inheritdoc cref="UseDatabase{TDelegate}(IPersistentStoreBuilder, string, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})" path="/remarks"/>
    public static IPersistentStoreBuilder UseFirebird(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<FirebirdDelegate>(DataSourceOptions.Providers.Firebird, factory, connectionString);

    /// <summary>Stores the schedule in Oracle.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseOracle(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<OracleDelegate>(DataSourceOptions.Providers.Oracle, connectionString);

    /// <summary>Stores the schedule in Oracle.</summary>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseOracle(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<OracleDelegate>(DataSourceOptions.Providers.Oracle, configure);

    /// <summary>
    /// Stores the schedule in Oracle, reached through
    /// <c>Oracle.ManagedDataAccess.Client.OracleClientFactory.Instance</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Oracle is the driver that needs
    /// <see cref="UseOracle(IPersistentStoreBuilder, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})"/>
    /// rather than this overload for anything but the smallest job data: the managed driver binds
    /// parameters by position unless <c>BindByName</c> is set on its command, and it maps
    /// <see cref="System.Data.DbType.Binary"/> to <c>OracleDbType.Raw</c>, which holds two kilobytes.
    /// Quartz names neither of those types, so an application that references the driver has to say it.
    /// </para>
    /// </remarks>
    public static IPersistentStoreBuilder UseOracle(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<OracleDelegate>(DataSourceOptions.Providers.Oracle, factory, connectionString);

    /// <summary>
    /// Stores the schedule in Oracle, reached through its factory and told the two things only an
    /// application that references the driver can say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name path reaches <c>OracleCommand.BindByName</c> and <c>OracleParameter.OracleDbType</c> by
    /// reflecting over the types the driver description names. A factory names none, so the two are
    /// said in code:
    /// </para>
    /// <code>
    /// store.UseOracle(
    ///     OracleClientFactory.Instance,
    ///     connectionString,
    ///     configureCommand: command =&gt; ((OracleCommand) command).BindByName = true,
    ///     configureBinaryParameter: parameter =&gt; ((OracleParameter) parameter).OracleDbType = OracleDbType.Blob);
    /// </code>
    /// <para>
    /// Both matter. Without the first, every statement binds its parameters by position and the store
    /// reads the wrong columns; without the second, a job data map larger than two kilobytes will not go
    /// in, because <see cref="System.Data.DbType.Binary"/> is <c>OracleDbType.Raw</c> and not
    /// <c>Blob</c>. Oracle is the only driver Quartz ships a description for that needs either — this
    /// overload exists for it, and any other driver says the same things on its own
    /// <see cref="DbMetadata"/> through
    /// <see cref="UseGenericDatabase(IPersistentStoreBuilder, DbProviderFactory, string, DbMetadata)"/>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The store being configured.</param>
    /// <param name="factory">The driver's factory, normally <c>OracleClientFactory.Instance</c>.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="configureCommand">Applied to every command, for <c>BindByName</c>.</param>
    /// <param name="configureBinaryParameter">Applied to every blob parameter, for <c>OracleDbType</c>.</param>
    public static IPersistentStoreBuilder UseOracle(
        this IPersistentStoreBuilder builder,
        DbProviderFactory factory,
        string connectionString,
        Action<DbCommand>? configureCommand,
        Action<DbParameter>? configureBinaryParameter)
    {
        return builder.UseDatabase<OracleDelegate>(
            DataSourceOptions.Providers.Oracle,
            factory,
            connectionString,
            configureCommand,
            configureBinaryParameter);
    }

    /// <summary>Stores the schedule in SQLite, using the Microsoft.Data.Sqlite driver.</summary>
    /// <remarks>
    /// The modern driver, and what <c>UseSqlite</c> means: the short name goes to the default the way
    /// <c>UseMySql</c> does, and the way Entity Framework Core spells the same choice. The legacy
    /// System.Data.SQLite driver is <see cref="UseSystemDataSqlite(IPersistentStoreBuilder, string)"/>.
    /// </remarks>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseSqlite(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.Sqlite, connectionString);

    /// <inheritdoc cref="UseSqlite(IPersistentStoreBuilder, string)"/>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseSqlite(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.Sqlite, configure);

    /// <summary>
    /// Stores the schedule in SQLite, reached through
    /// <c>Microsoft.Data.Sqlite.SqliteFactory.Instance</c>.
    /// </summary>
    /// <inheritdoc cref="UseDatabase{TDelegate}(IPersistentStoreBuilder, string, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})" path="/remarks"/>
    public static IPersistentStoreBuilder UseSqlite(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.Sqlite, factory, connectionString);

    /// <summary>Stores the schedule in SQLite, using the legacy System.Data.SQLite driver.</summary>
    /// <remarks>
    /// Named after its driver rather than after the database, because
    /// <see cref="UseSqlite(IPersistentStoreBuilder, string)"/> is the one to reach for.
    /// </remarks>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseSystemDataSqlite(this IPersistentStoreBuilder builder, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.SystemDataSqlite, connectionString);

    /// <inheritdoc cref="UseSystemDataSqlite(IPersistentStoreBuilder, string)"/>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IPersistentStoreBuilder UseSystemDataSqlite(this IPersistentStoreBuilder builder, Action<DataSourceOptions> configure)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.SystemDataSqlite, configure);

    /// <summary>
    /// Stores the schedule in SQLite, reached through
    /// <c>System.Data.SQLite.SQLiteFactory.Instance</c>.
    /// </summary>
    /// <inheritdoc cref="UseDatabase{TDelegate}(IPersistentStoreBuilder, string, DbProviderFactory, string, Action{DbCommand}, Action{DbParameter})" path="/remarks"/>
    public static IPersistentStoreBuilder UseSystemDataSqlite(this IPersistentStoreBuilder builder, DbProviderFactory factory, string connectionString)
        => builder.UseDatabase<SQLiteDelegate>(DataSourceOptions.Providers.SystemDataSqlite, factory, connectionString);

    /// <summary>
    /// Stores the schedule in a database Quartz has no specific support for, using the generic SQL
    /// dialect.
    /// </summary>
    /// <param name="builder">The store being configured.</param>
    /// <param name="provider">The Quartz provider name identifying the ADO.NET driver.</param>
    /// <param name="connectionString">The connection string.</param>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
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
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
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
    /// Stores the schedule in a database Quartz has no specific support for, reached through the
    /// driver's own factory and described in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registration that names nothing at all: the factory supplies the connections, and the
    /// description says how the driver spells a parameter. It needs no provider name, because a
    /// provider name exists to look a description up and this one arrived.
    /// </para>
    /// <para>
    /// A description here may still name the driver's types — nothing stops it, and
    /// <c>ParameterDbTypePropertyName</c> is how a blob gets the driver's own parameter type — but it
    /// does not have to, and a trimmed application should not:
    /// </para>
    /// <code>
    /// store.UseGenericDatabase(MyFactory.Instance, connectionString, new DbMetadata
    /// {
    ///     ProductName = "My Database",
    ///     ParameterNamePrefix = "@",
    ///     UseParameterNamePrefixInParameterCollection = true,
    ///     BindByName = true,
    ///     ConfigureBinaryParameter = parameter =&gt; ((MyParameter) parameter).MyDbType = MyDbType.Blob,
    /// });
    /// </code>
    /// </remarks>
    /// <param name="builder">The store being configured.</param>
    /// <param name="factory">The driver's factory, normally its <c>Instance</c> singleton.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="metadata">The ADO.NET driver description.</param>
    public static IPersistentStoreBuilder UseGenericDatabase(
        this IPersistentStoreBuilder builder,
        DbProviderFactory factory,
        string connectionString,
        DbMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        metadata.Validate();

        builder.UseDriverDelegate<StdAdoDelegate>();
        return builder.UseConnectionProvider(_ => new ProviderFactoryDbProvider(metadata, factory, connectionString));
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

    /// <summary>
    /// Chooses the dialect and the driver, with the driver reached through its own factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider name still chooses the driver description — how parameters are spelled, and whether
    /// they bind by name — but only the half of it that names no type is read, because the factory
    /// supplies every object the store would otherwise have constructed. That is what makes this the
    /// overload a trimmed or ahead-of-time-compiled application uses: nothing on this path calls
    /// <c>Type.GetType</c>.
    /// </para>
    /// <para>
    /// The provider is registered directly rather than assembled from <see cref="DataSourceOptions"/>,
    /// because a factory and a driver description are values only code can supply and options are
    /// bound from configuration. Putting them on the options type made the configuration binder's
    /// source generator — which a native AOT publish turns on, and which issue #3430 is about turning
    /// on everywhere — try to construct a <see cref="DbProviderFactory"/> from a configuration section.
    /// </para>
    /// </remarks>
    private static IPersistentStoreBuilder UseDatabase<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TDelegate>(
        this IPersistentStoreBuilder builder,
        string provider,
        DbProviderFactory factory,
        string connectionString,
        Action<DbCommand>? configureCommand = null,
        Action<DbParameter>? configureBinaryParameter = null) where TDelegate : class, IDriverDelegate
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        builder.UseDriverDelegate<TDelegate>();

        return builder.UseConnectionProvider(serviceProvider =>
        {
            // The type-free half of the shipped description: the parameter prefix and the binding mode,
            // with the driver's own types left unresolved because nothing here constructs one.
            DbMetadata metadata = serviceProvider.GetRequiredService<DbMetadataResolver>().ResolveWithoutTypes(provider);

            if (configureCommand is not null || configureBinaryParameter is not null)
            {
                // Copied rather than assigned: a resolved description is shared by every scheduler that
                // names the same provider, and one scheduler's seams are not another's.
                metadata = metadata with
                {
                    ConfigureCommand = configureCommand,
                    ConfigureBinaryParameter = configureBinaryParameter,
                };
            }

            return new ProviderFactoryDbProvider(metadata, factory, connectionString);
        });
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
