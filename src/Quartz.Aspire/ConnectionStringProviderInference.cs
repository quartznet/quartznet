using System.Data.Common;
using System.Text.RegularExpressions;

namespace Quartz;

/// <summary>
/// Works out which ADO.NET driver a connection string is for, from the shape of the string alone.
/// </summary>
/// <remarks>
/// <para>
/// An Aspire connection name arrives as a string and nothing else: the AppHost injects
/// <c>ConnectionStrings__quartz</c> and the worker reads it back, with no note of which resource wrote
/// it. Quartz needs to know, because the driver delegate decides the SQL — so this reads the string's
/// keywords and matches them against the shapes the databases Aspire ships a resource for produce.
/// </para>
/// <para>
/// <strong>It refuses rather than guesses.</strong> Zero matches and two matches are both errors naming
/// <see cref="QuartzAspireSettings.Provider"/>, because the failure a guess produces is a scheduler that
/// starts, connects, and issues SQL the database cannot run — discovered at the first trigger
/// acquisition rather than at startup, and not obviously about the connection string when it is.
/// </para>
/// <para>
/// The shapes are matched on keywords, never on substrings: the string is parsed with
/// <see cref="DbConnectionStringBuilder"/>, which is where quoting, escaping and repeated keywords are
/// somebody else's problem, and each keyword is then compared with spaces and underscores removed so
/// that <c>User ID</c>, <c>userid</c> and <c>User_Id</c> are one keyword.
/// </para>
/// </remarks>
internal static class ConnectionStringProviderInference
{
    /// <summary>
    /// How every driver but Npgsql spells "the machine holding the database".
    /// </summary>
    private static readonly string[] ServerKeywords = ["server", "datasource", "address", "addr", "networkaddress"];

    /// <summary>
    /// How a driver spells "the database on it".
    /// </summary>
    private static readonly string[] CatalogKeywords = ["initialcatalog", "database", "attachdbfilename"];

    /// <summary>
    /// The keywords that say authentication is Windows', which only SQL Server has.
    /// </summary>
    private static readonly string[] IntegratedSecurityKeywords = ["integratedsecurity", "trustedconnection"];

    /// <summary>
    /// How a driver spells "the login".
    /// </summary>
    private static readonly string[] UserKeywords = ["uid", "userid", "username", "user", "usr"];

    /// <summary>
    /// File extensions that mean a SQLite database file.
    /// </summary>
    private static readonly string[] SqliteExtensions = [".db", ".db3", ".sqlite", ".sqlite3"];

    /// <summary>
    /// Oracle's EZ-connect form — <c>host:port</c>, optionally with <c>/service</c>. It is what Aspire's
    /// Oracle resource injects, and nothing else in this table produces a colon-then-digits data source.
    /// </summary>
    private static readonly Regex EasyConnect = new(
        @"^[A-Za-z0-9_.\-]+:\d{1,5}(/[A-Za-z0-9_.\-]+)?$",
        RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Works out which provider <paramref name="connectionString"/> is for.
    /// </summary>
    /// <param name="connectionString">The connection string, as the AppHost injected it.</param>
    /// <param name="connectionName">The Aspire connection name, for the message when this fails.</param>
    /// <returns>One of the names on <see cref="DataSourceOptions.Providers"/>.</returns>
    /// <exception cref="SchedulerConfigException">
    /// The string matched no shape, or more than one.
    /// </exception>
    public static string Infer(string connectionString, string connectionName)
    {
        Keywords keywords = Keywords.Parse(connectionString);

        List<string> matches = [];

        if (IsNpgsql(keywords))
        {
            matches.Add(DataSourceOptions.Providers.Npgsql);
        }

        if (IsSqlServer(keywords))
        {
            matches.Add(DataSourceOptions.Providers.SqlServer);
        }

        if (IsMySqlConnector(keywords))
        {
            matches.Add(DataSourceOptions.Providers.MySqlConnector);
        }

        if (IsSqlite(keywords))
        {
            matches.Add(DataSourceOptions.Providers.Sqlite);
        }

        if (IsOracle(keywords))
        {
            matches.Add(DataSourceOptions.Providers.Oracle);
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        throw new SchedulerConfigException(Explain(connectionName, matches));
    }

    /// <summary>
    /// PostgreSQL, through Npgsql: <c>Host</c> is the keyword Npgsql leads with and no other driver in
    /// this table uses, and <c>Database</c> is its catalog.
    /// </summary>
    /// <remarks>
    /// <c>Uid</c> disqualifies it. That is the MySQL and SQL Server spelling of the login; Npgsql's own
    /// is <c>Username</c>, and every tool that writes an Npgsql string writes that one. A string saying
    /// <c>Host</c> and <c>Uid</c> together is a MySQL string written the long way round, and refusing it
    /// is better than reading it as PostgreSQL.
    /// </remarks>
    private static bool IsNpgsql(Keywords keywords)
    {
        return keywords.Has("host") && keywords.Has("database") && !keywords.Has("uid");
    }

    /// <summary>
    /// Microsoft SQL Server: a server keyword together with a catalog or an integrated-security keyword.
    /// </summary>
    /// <remarks>
    /// <c>Port</c> and <c>Host</c> disqualify it, and that is the rule that keeps a MySQL string out.
    /// <c>Microsoft.Data.SqlClient</c> accepts neither keyword — passing one to
    /// <c>SqlConnectionStringBuilder</c> throws — because SQL Server writes the port into the server
    /// itself, as <c>Server=host,1433</c>. Aspire's SQL Server and MySQL resources otherwise inject
    /// almost the same string, so without this they would be ambiguous with each other.
    /// </remarks>
    private static bool IsSqlServer(Keywords keywords)
    {
        return keywords.HasAny(ServerKeywords)
               && (keywords.HasAny(CatalogKeywords) || keywords.HasAny(IntegratedSecurityKeywords))
               && !keywords.Has("port")
               && !keywords.Has("host");
    }

    /// <summary>
    /// MySQL, through MySqlConnector: a server keyword, an explicit port and a login.
    /// </summary>
    /// <remarks>
    /// <c>Host</c> disqualifies it, so that PostgreSQL's shape cannot also read as MySQL's. MySqlConnector
    /// does accept <c>Host</c> as a synonym for <c>Server</c>, so a MySQL string written that way is
    /// refused rather than misread — which is the trade this whole table makes.
    /// </remarks>
    private static bool IsMySqlConnector(Keywords keywords)
    {
        return keywords.HasAny(ServerKeywords)
               && !keywords.Has("host")
               && keywords.Has("port")
               && keywords.HasAny(UserKeywords);
    }

    /// <summary>
    /// SQLite: a data source naming a database file, or an in-memory database.
    /// </summary>
    private static bool IsSqlite(Keywords keywords)
    {
        return keywords.Value("datasource") is { } dataSource && IsSqliteTarget(dataSource, keywords);
    }

    /// <summary>
    /// Oracle: a data source holding a TNS descriptor or an EZ-connect string.
    /// </summary>
    /// <remarks>
    /// Both put the host and the port inside the data source, so a <c>Host</c> or <c>Port</c> keyword
    /// beside one says the string is not Oracle's. A bare TNS alias — <c>Data Source=orcl</c> — is
    /// deliberately not recognised: it is indistinguishable from a SQL Server instance name, and
    /// guessing between the two is exactly what this refuses to do.
    /// </remarks>
    private static bool IsOracle(Keywords keywords)
    {
        if (keywords.Value("datasource") is not { } dataSource)
        {
            return false;
        }

        if (keywords.Has("host") || keywords.Has("port"))
        {
            return false;
        }

        string trimmed = dataSource.Trim();

        bool tnsDescriptor = trimmed.StartsWith('(')
                             && trimmed.Contains("description", StringComparison.OrdinalIgnoreCase);

        return tnsDescriptor || EasyConnect.IsMatch(trimmed);
    }

    private static bool IsSqliteTarget(string dataSource, Keywords keywords)
    {
        if (string.Equals(keywords.Value("mode"), "memory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string trimmed = dataSource.Trim();

        return string.Equals(trimmed, ":memory:", StringComparison.OrdinalIgnoreCase)
               || SqliteExtensions.Any(extension => trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What to say when the string matched nothing, or matched twice.
    /// </summary>
    /// <remarks>
    /// Both messages name the property that ends the argument and the class holding the values it takes,
    /// because the reader of this message is looking at a stack trace rather than at this file.
    /// </remarks>
    private static string Explain(string connectionName, List<string> matches)
    {
        string problem = matches.Count == 0
            ? "matches none of the connection-string shapes Quartz recognises"
            : $"matches {string.Join(" and ", matches)}, and Quartz will not choose between them";

        return $"The connection string named '{connectionName}' {problem}, so Quartz cannot tell which "
               + "database it is for. Say so: set QuartzAspireSettings.Provider to one of the names on "
               + "DataSourceOptions.Providers - for instance "
               + $"builder.AddQuartzPersistentStore(\"{connectionName}\", settings => settings.Provider = "
               + "DataSourceOptions.Providers.Npgsql), or the configuration key "
               + $"Aspire:Quartz:{connectionName}:Provider. Inferring it would pick the driver delegate "
               + "that writes the SQL, and the wrong one fails at the first trigger acquisition rather "
               + "than here.";
    }

    /// <summary>
    /// A connection string's keywords, normalized so that spelling variations are one keyword.
    /// </summary>
    private sealed class Keywords
    {
        private readonly Dictionary<string, string> values;

        private Keywords(Dictionary<string, string> values) => this.values = values;

        /// <summary>
        /// Parses a connection string, treating one it cannot parse as having no keywords at all.
        /// </summary>
        /// <remarks>
        /// A malformed string is a real possibility — it arrives from an environment variable — and the
        /// message this produces then is the one that helps, naming the connection and the property that
        /// settles it, rather than <see cref="DbConnectionStringBuilder"/>'s complaint about a character.
        /// The driver reports the syntax error itself on the first connection, in its own words.
        /// </remarks>
        public static Keywords Parse(string connectionString)
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);

            DbConnectionStringBuilder parsed = new();

            try
            {
                parsed.ConnectionString = connectionString;
            }
            catch (ArgumentException)
            {
                return new Keywords(values);
            }

            foreach (string key in parsed.Keys.Cast<string>())
            {
                values[Normalize(key)] = parsed[key]?.ToString() ?? "";
            }

            return new Keywords(values);
        }

        public bool Has(string keyword) => values.ContainsKey(keyword);

        public bool HasAny(string[] keywords) => keywords.Any(values.ContainsKey);

        public string? Value(string keyword) => values.GetValueOrDefault(keyword);

        /// <summary>
        /// Lower-cased with spaces and underscores removed, which is how the drivers themselves treat a
        /// keyword: <c>User ID</c>, <c>userid</c> and <c>User_Id</c> all name one thing.
        /// </summary>
        private static string Normalize(string keyword)
        {
            return keyword
                .Replace(" ", "", StringComparison.Ordinal)
                .Replace("_", "", StringComparison.Ordinal)
                .ToLowerInvariant();
        }
    }
}
