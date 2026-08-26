using System.Data.Common;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz;

/// <summary>
/// Strongly typed configuration for a single named ADO.NET data source.
/// </summary>
/// <remarks>
/// Registered as named options keyed by data source name, so a scheduler with several data sources
/// configures each one under its own name. Typed replacement for the
/// <c>quartz.dataSource.&lt;name&gt;.*</c> property keys.
/// </remarks>
public sealed class DataSourceOptions
{
    /// <summary>
    /// The names of the ADO.NET drivers Quartz ships a description for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DataSourceOptions.Provider"/> is a string because a driver Quartz knows nothing about
    /// is describable — register a <c>DbMetadataFactory</c>, or use <c>UseGenericDatabase</c> — so the
    /// set is not closed and cannot be an enum. These are the values that work out of the box, in one
    /// place, rather than spread across a documentation table and nine property-file sections.
    /// </para>
    /// <para>
    /// Configuring the database through <c>UseSqlServer</c>, <c>UsePostgres</c> and their siblings sets
    /// the provider as well as the driver delegate, so most applications never name one.
    /// </para>
    /// </remarks>
    public static class Providers
    {
        /// <summary>Microsoft SQL Server, through <c>Microsoft.Data.SqlClient</c>.</summary>
        public const string SqlServer = "SqlServer";

        /// <summary>PostgreSQL, through <c>Npgsql</c>.</summary>
        public const string Npgsql = "Npgsql";

        /// <summary>MySQL, through <c>MySql.Data</c>.</summary>
        public const string MySql = "MySql";

        /// <summary>MySQL, through <c>MySqlConnector</c>.</summary>
        public const string MySqlConnector = "MySqlConnector";

        /// <summary>Oracle, through the managed ODP.NET driver.</summary>
        public const string Oracle = "OracleODPManaged";

        /// <summary>SQLite, through <c>Microsoft.Data.Sqlite</c>.</summary>
        public const string Sqlite = "SQLite-Microsoft";

        /// <summary>SQLite, through <c>System.Data.SQLite</c>.</summary>
        public const string SystemDataSqlite = "SQLite";

        /// <summary>Firebird, through <c>FirebirdSql.Data.FirebirdClient</c>.</summary>
        public const string Firebird = "Firebird";
    }

    /// <summary>
    /// The Quartz provider name identifying the ADO.NET driver.
    /// </summary>
    /// <remarks>
    /// The names Quartz ships a description for are on <see cref="Providers"/>. Any other name works
    /// too, as long as something in the container describes it — see <c>UseGenericDatabase</c>.
    /// </remarks>
    public string Provider { get; set; } = "";

    /// <summary>
    /// The driver's own <see cref="DbProviderFactory"/> — normally its <c>Instance</c> singleton —
    /// which Quartz asks for connections rather than constructing the types
    /// <see cref="Provider"/> names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the registration a trimmed or ahead-of-time-compiled application uses. Naming a driver
    /// resolves its connection, command and parameter types with <c>Type.GetType</c>, which a trimmer
    /// cannot see through, so it removes what those names point at; a factory hands back an instance of
    /// each instead. <see cref="Provider"/> is still set — it decides how parameters are spelled — but
    /// only the half of its description that names no type is read.
    /// </para>
    /// <para>
    /// Settable from code only, since a configuration binder has no way to produce one:
    /// <c>UseSqlServer(SqlClientFactory.Instance, connectionString)</c> and its siblings are how it is
    /// normally set.
    /// </para>
    /// </remarks>
    public DbProviderFactory? ProviderFactory { get; set; }

    /// <summary>
    /// The driver description, supplied in code rather than resolved from <see cref="Provider"/>.
    /// </summary>
    /// <remarks>
    /// For a driver Quartz ships no description for and an application would rather describe than name:
    /// <c>UseGenericDatabase(factory, connectionString, metadata)</c> sets this. Set, it is the
    /// description, and <see cref="Provider"/> is not consulted — there is nothing left to look up.
    /// </remarks>
    public DbMetadata? ProviderMetadata { get; set; }

    /// <summary>
    /// Applied to every command Quartz mints for this data source.
    /// </summary>
    /// <remarks>
    /// Copied onto the driver description as <see cref="DbMetadata.ConfigureCommand"/>, which says what
    /// it is for. Oracle is the driver that needs it: it binds parameters by position unless
    /// <c>OracleCommand.BindByName</c> is set, and Quartz cannot name <c>OracleCommand</c>.
    /// </remarks>
    public Action<DbCommand>? ConfigureCommand { get; set; }

    /// <summary>
    /// Applied to every parameter Quartz binds a blob to.
    /// </summary>
    /// <remarks>
    /// Copied onto the driver description as <see cref="DbMetadata.ConfigureBinaryParameter"/>, which
    /// says what it is for. Oracle is the driver that needs it: <see cref="System.Data.DbType.Binary"/>
    /// means <c>OracleDbType.Raw</c> there, which caps a job data map at two kilobytes.
    /// </remarks>
    public Action<DbParameter>? ConfigureBinaryParameter { get; set; }

    /// <summary>
    /// The connection string used to reach the database.
    /// </summary>
    /// <remarks>
    /// Takes precedence over <see cref="ConnectionStringName"/> when both are set.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The name of a connection string to resolve from <c>IConfiguration</c>'s connection strings.
    /// </summary>
    public string? ConnectionStringName { get; set; }

    /// <summary>
    /// Whether connections come from a <c>DbDataSource</c> registered in the container rather than
    /// from a connection string Quartz holds.
    /// </summary>
    /// <remarks>
    /// This is the third way a data source can say where its connections come from, alongside
    /// <see cref="ConnectionString"/> and <see cref="ConnectionStringName"/>, and it wins over both.
    /// It is a setting rather than a builder method because it answers the same question they do.
    /// </remarks>
    public bool UseRegisteredDataSource { get; set; }

    /// <summary>
    /// The service key the <c>DbDataSource</c> is registered under, for an application that registers
    /// more than one. Setting it implies <see cref="UseRegisteredDataSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unkeyed <see cref="System.Data.Common.DbDataSource"/> is the container's one data source, so a
    /// process talking to two databases — a scheduler per tenant, or a reporting scheduler beside the
    /// application's own — has to key them apart. This says which key is this data source's.
    /// </para>
    /// <para>
    /// A service key can be any object, so this is settable from code only: a configuration binder has
    /// no way to produce one. Configuration that needs to name a keyed data source therefore says so in
    /// a <c>UseDataSource</c> callback rather than in a <c>Quartz:DataSource:&lt;name&gt;</c> section.
    /// </para>
    /// </remarks>
    public object? DataSourceServiceKey { get; set; }

    /// <summary>
    /// Supplies the <c>DbDataSource</c> directly, for a data source that is built rather than
    /// registered. Wins over <see cref="UseRegisteredDataSource"/> and
    /// <see cref="DataSourceServiceKey"/>.
    /// </summary>
    /// <remarks>
    /// The factory runs once, when the store's connection provider is first resolved, and is given the
    /// container's service provider. Like <see cref="DataSourceServiceKey"/> it is settable from code
    /// only.
    /// </remarks>
    public Func<IServiceProvider, DbDataSource>? DataSourceFactory { get; set; }
}
