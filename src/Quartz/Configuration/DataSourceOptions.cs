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
}
