using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;

using MySqlConnector;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Applies every migration under <c>database/migrations/</c> to a 3.16-era schema and asserts the
/// result is the schema a fresh install produces.
/// </summary>
/// <remarks>
/// <para>
/// The migrated schema is built under the <c>QRTZM_</c> table prefix, alongside the <c>QRTZ_</c>
/// schema the test environment already created from the current <c>database/tables/</c> script.
/// That gives isolation without a second database, and incidentally proves the "replace 'QRTZ_'
/// with your configured table prefix" instruction each script carries actually works.
/// </para>
/// <para>
/// Two things are asserted: the migrated schema matches a fresh install table-for-table,
/// column-for-column and index-for-index; and every migration is re-runnable, since each one is
/// applied twice. SQLite is the exception to the second: it has no conditional DDL, so its
/// ADD COLUMN migrations are only applied once.
/// </para>
/// <para>
/// Not covered yet: running a scheduler against the migrated schema to prove it is functional and
/// not merely structurally identical.
/// </para>
/// </remarks>
[Category("migrations")]
public class MigrationScriptTest
{
    private const string FreshPrefix = "QRTZ_";
    private const string MigratedPrefix = "QRTZM_";

    /// <summary>Migrations that apply on top of the 3.16 baseline, in the order they must run.</summary>
    private static readonly (string Version, string Name)[] Migrations =
    [
        ("3.17", "add_misfire_orig_fire_time"),
        ("3.18", "add_execution_group"),
        ("3.19", "add_preferred_node"),
        ("3.20", "index_alignment"),
    ];

    [Test]
    [Category("db-sqlite")]
    public async Task SqliteSchemaMatchesFreshInstall()
    {
        string file = Path.Combine(Path.GetTempPath(), $"quartz-migration-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={file};";

        try
        {
            await using SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // The fresh schema the migrated one has to end up matching. The other dialects get
            // this from the test environment, which creates it when the container starts.
            await ExecuteScriptAsync(connection, CurrentTableScript("sqlite"), "sqlite");

            // The 3.16-era schema the migrations are applied on top of.
            await ExecuteScriptAsync(connection, BaselineScript("sqlite"), "sqlite");

            await RunMigrationChainAsync(connection, "sqlite");
            await AssertSchemaMatchesAsync(connection, "sqlite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task SqlServerSchemaMatchesFreshInstall()
    {
        await using SqlConnection connection = new SqlConnection(RequireConnectionString("MSSQL_CONNECTION_STRING"));
        await connection.OpenAsync();

        await ExecuteScriptAsync(connection, BaselineScript("sqlServer"), "sqlServer");
        await RunMigrationChainAsync(connection, "sqlServer");
        await AssertSchemaMatchesAsync(connection, "sqlServer");
    }

    [Test]
    [Category("db-postgres")]
    public async Task PostgreSqlSchemaMatchesFreshInstall()
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(RequireConnectionString("PG_CONNECTION_STRING"));
        await connection.OpenAsync();

        await ExecuteScriptAsync(connection, BaselineScript("postgres"), "postgres");
        await RunMigrationChainAsync(connection, "postgres");
        await AssertSchemaMatchesAsync(connection, "postgres");
    }

    [Test]
    [Category("db-mysql")]
    public async Task MySqlSchemaMatchesFreshInstall()
    {
        await using MySqlConnection connection = new MySqlConnection(RequireConnectionString("MYSQL_CONNECTION_STRING"));
        await connection.OpenAsync();

        await ExecuteScriptAsync(connection, BaselineScript("mysql_innodb"), "mysql_innodb");
        await RunMigrationChainAsync(connection, "mysql_innodb");
        await AssertSchemaMatchesAsync(connection, "mysql_innodb");
    }

    [Test]
    [Category("db-oracle")]
    public async Task OracleSchemaMatchesFreshInstall()
    {
        await using OracleConnection connection = new OracleConnection(RequireConnectionString("ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();

        await ExecuteScriptAsync(connection, BaselineScript("oracle"), "oracle");
        await RunMigrationChainAsync(connection, "oracle");
        await AssertSchemaMatchesAsync(connection, "oracle");
    }

    [Test]
    [Category("db-firebird")]
    public async Task FirebirdSchemaMatchesFreshInstall()
    {
        await using FbConnection connection = new FbConnection(RequireConnectionString("FIREBIRD_CONNECTION_STRING"));
        await connection.OpenAsync();

        await ExecuteScriptAsync(connection, BaselineScript("firebird"), "firebird");
        await RunMigrationChainAsync(connection, "firebird");
        await AssertSchemaMatchesAsync(connection, "firebird");
    }

    /// <summary>
    /// Applies every migration in version order, then applies them all a second time to prove the
    /// guards make them no-ops.
    /// </summary>
    private static async Task RunMigrationChainAsync(DbConnection connection, string dialect)
    {
        foreach ((string version, string name) in Migrations)
        {
            await ExecuteScriptAsync(connection, MigrationScript(version, name, dialect), dialect);
        }

        // Idempotency. SQLite has no conditional DDL, so its ADD COLUMN statements are expected to
        // fail on a second run; the index migration still has to be re-runnable.
        foreach ((string version, string name) in Migrations)
        {
            if (dialect == "sqlite" && name != "index_alignment")
            {
                continue;
            }

            await ExecuteScriptAsync(connection, MigrationScript(version, name, dialect), dialect);
        }
    }

    private static async Task AssertSchemaMatchesAsync(DbConnection connection, string dialect)
    {
        SchemaSnapshot fresh = await SchemaSnapshot.ReadAsync(connection, dialect, FreshPrefix);
        SchemaSnapshot migrated = await SchemaSnapshot.ReadAsync(connection, dialect, MigratedPrefix);

        fresh.Tables.Should().NotBeEmpty("the fresh install schema must exist for the comparison to mean anything");

        migrated.Tables.Should().BeEquivalentTo(fresh.Tables, "the migrated schema should have the same tables as a fresh install");
        migrated.Columns.Should().BeEquivalentTo(fresh.Columns, "the migrated schema should have the same columns as a fresh install");
        migrated.Indexes.Should().BeEquivalentTo(fresh.Indexes, "the migrated schema should have the same indexes as a fresh install");
    }

    /// <summary>Column and index inventory for one table prefix, with the prefix normalized away.</summary>
    private sealed record SchemaSnapshot(
        IReadOnlyCollection<string> Tables,
        IReadOnlyCollection<string> Columns,
        IReadOnlyCollection<string> Indexes)
    {
        public static async Task<SchemaSnapshot> ReadAsync(DbConnection connection, string dialect, string prefix)
        {
            (string tableSql, string columnSql, string indexSql) = Queries(dialect, prefix);

            List<string> tables = await QueryAsync(connection, tableSql, prefix);
            List<string> columns = await QueryAsync(connection, columnSql, prefix);
            List<string> indexes = await QueryAsync(connection, indexSql, prefix);

            return new SchemaSnapshot(tables, columns, indexes);
        }

        private static async Task<List<string>> QueryAsync(DbConnection connection, string sql, string prefix)
        {
            List<string> rows = [];

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;

            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                StringBuilder row = new StringBuilder();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (i > 0)
                    {
                        row.Append('|');
                    }

                    row.Append(reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()?.Trim().ToUpperInvariant());
                }

                // Strip the prefix so QRTZ_TRIGGERS and QRTZM_TRIGGERS compare equal.
                rows.Add(row.ToString().Replace(prefix.ToUpperInvariant(), "", StringComparison.Ordinal));
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        /// <summary>
        /// Per-dialect introspection. Index queries deliberately exclude primary keys and unique
        /// constraints: those come from the table definition, not from the migrations under test.
        /// </summary>
        private static (string Tables, string Columns, string Indexes) Queries(string dialect, string prefix)
        {
            // '_' is a single-character wildcard in LIKE, so an unescaped 'QRTZ_%' also matches
            // QRTZM_TRIGGERS. Escape it, or the fresh snapshot silently swallows the migrated one.
            string p = prefix.ToUpperInvariant().Replace("_", "!_", StringComparison.Ordinal);

            return dialect switch
            {
                "sqlite" => (
                    $"SELECT UPPER(name) FROM sqlite_master WHERE type = 'table' AND UPPER(name) LIKE '{p}%' ESCAPE '!'",
                    $"SELECT UPPER(m.name), UPPER(c.name), UPPER(c.type), c.\"notnull\" FROM sqlite_master m JOIN pragma_table_info(m.name) c WHERE m.type = 'table' AND UPPER(m.name) LIKE '{p}%' ESCAPE '!'",
                    $"SELECT UPPER(i.tbl_name), UPPER(i.name), UPPER(c.name), c.seqno FROM sqlite_master i JOIN pragma_index_info(i.name) c WHERE i.type = 'index' AND i.sql IS NOT NULL AND UPPER(i.tbl_name) LIKE '{p}%' ESCAPE '!'"),

                "sqlServer" => (
                    $"SELECT UPPER(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                    $"SELECT UPPER(TABLE_NAME), UPPER(COLUMN_NAME), UPPER(DATA_TYPE), IS_NULLABLE, ISNULL(CHARACTER_MAXIMUM_LENGTH, -1) FROM INFORMATION_SCHEMA.COLUMNS WHERE UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                    $"""
                     SELECT UPPER(t.name), UPPER(i.name), UPPER(c.name), ic.key_ordinal
                     FROM sys.indexes i
                     JOIN sys.tables t ON t.object_id = i.object_id
                     JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                     JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
                     WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND UPPER(t.name) LIKE '{p}%' ESCAPE '!'
                     """),

                "postgres" => (
                    $"SELECT UPPER(table_name) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                    $"SELECT UPPER(table_name), UPPER(column_name), UPPER(data_type), is_nullable, COALESCE(character_maximum_length, -1) FROM information_schema.columns WHERE table_schema = 'public' AND UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                    $"""
                     SELECT UPPER(t.relname), UPPER(i.relname), UPPER(a.attname), k.ord
                     FROM pg_class t
                     JOIN pg_index ix ON t.oid = ix.indrelid
                     JOIN pg_class i ON i.oid = ix.indexrelid
                     JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON TRUE
                     JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                     WHERE ix.indisprimary = FALSE AND ix.indisunique = FALSE AND UPPER(t.relname) LIKE '{p}%' ESCAPE '!'
                     """),

                "mysql_innodb" => (
                    $"SELECT UPPER(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                    $"SELECT UPPER(TABLE_NAME), UPPER(COLUMN_NAME), UPPER(DATA_TYPE), IS_NULLABLE, IFNULL(CHARACTER_MAXIMUM_LENGTH, -1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'",
                    $"SELECT UPPER(TABLE_NAME), UPPER(INDEX_NAME), UPPER(COLUMN_NAME), SEQ_IN_INDEX FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME <> 'PRIMARY' AND NON_UNIQUE = 1 AND UPPER(TABLE_NAME) LIKE '{p}%' ESCAPE '!'"),

                "oracle" => (
                    $"SELECT UPPER(table_name) FROM user_tables WHERE UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                    $"SELECT UPPER(table_name), UPPER(column_name), UPPER(data_type), nullable, NVL(data_length, -1) FROM user_tab_columns WHERE UPPER(table_name) LIKE '{p}%' ESCAPE '!'",
                    $"""
                     SELECT UPPER(ic.table_name), UPPER(ic.index_name), UPPER(ic.column_name), ic.column_position
                     FROM user_ind_columns ic
                     JOIN user_indexes i ON i.index_name = ic.index_name
                     WHERE i.uniqueness = 'NONUNIQUE' AND UPPER(ic.table_name) LIKE '{p}%' ESCAPE '!'
                     """),

                "firebird" => (
                    $"SELECT TRIM(UPPER(rdb$relation_name)) FROM rdb$relations WHERE rdb$view_blr IS NULL AND rdb$system_flag = 0 AND TRIM(UPPER(rdb$relation_name)) LIKE '{p}%' ESCAPE '!'",
                    $"""
                     SELECT TRIM(UPPER(rf.rdb$relation_name)), TRIM(UPPER(rf.rdb$field_name)), TRIM(UPPER(f.rdb$field_type)), COALESCE(rf.rdb$null_flag, 0), COALESCE(f.rdb$character_length, -1)
                     FROM rdb$relation_fields rf
                     JOIN rdb$fields f ON f.rdb$field_name = rf.rdb$field_source
                     WHERE TRIM(UPPER(rf.rdb$relation_name)) LIKE '{p}%' ESCAPE '!'
                     """,
                    $"""
                     SELECT TRIM(UPPER(i.rdb$relation_name)), TRIM(UPPER(i.rdb$index_name)), TRIM(UPPER(s.rdb$field_name)), s.rdb$field_position
                     FROM rdb$indices i
                     JOIN rdb$index_segments s ON s.rdb$index_name = i.rdb$index_name
                     WHERE COALESCE(i.rdb$unique_flag, 0) = 0 AND i.rdb$system_flag = 0 AND TRIM(UPPER(i.rdb$relation_name)) LIKE '{p}%' ESCAPE '!'
                     """),

                _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "no introspection queries for this dialect")
            };
        }
    }

    /// <summary>
    /// Runs a script, splitting it into the batches the dialect's own client would split it into.
    /// </summary>
    private static async Task ExecuteScriptAsync(DbConnection connection, string script, string dialect)
    {
        if (dialect != "sqlite")
        {
            // Let the database's own client handle its batch separator.
            await TestcontainersDatabaseEnvironment.ExecuteScriptAsync(dialect, script);
            return;
        }

        foreach (string batch in SplitBatches(script, dialect))
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<string> SplitBatches(string script, string dialect)
    {
        // The generated scripts use each dialect's own batch separator, matching what the native
        // client (sqlcmd, psql, sqlplus, isql) expects.
        string[] separators = dialect switch
        {
            "sqlServer" => ["\nGO\n", "\nGO\r\n"],
            "oracle" => ["\n/\n", "\n/\r\n"],
            _ => []
        };

        // Comments have to go first: they can contain semicolons, which would otherwise split a
        // statement in half.
        string stripped = StripComments(script);

        IEnumerable<string> batches = separators.Length > 0
            ? stripped.Split(separators.Select(s => s.Replace("\r\n", "\n")).Distinct().ToArray(), StringSplitOptions.None)
            : SplitOnSemicolons(stripped, dialect);

        foreach (string batch in batches)
        {
            string trimmed = batch.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>
    /// Splits on statement-terminating semicolons for the dialects whose scripts have no batch
    /// separator. Firebird's SET TERM blocks are handled by honouring the declared terminator.
    /// </summary>
    private static IEnumerable<string> SplitOnSemicolons(string script, string dialect)
    {
        if (dialect == "firebird")
        {
            return SplitFirebird(script);
        }

        if (dialect == "postgres")
        {
            return SplitPostgres(script);
        }

        if (dialect == "sqlite")
        {
            return SplitSqlite(script);
        }

        return script.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// SQLite trigger bodies are <c>BEGIN ... END;</c> blocks whose inner semicolons do not end the
    /// statement, so track the block depth rather than splitting on every semicolon.
    /// </summary>
    private static IEnumerable<string> SplitSqlite(string script)
    {
        List<string> statements = [];
        StringBuilder current = new StringBuilder();
        int depth = 0;

        foreach (string line in script.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            current.AppendLine(line);

            if (trimmed.EndsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
                continue;
            }

            if (trimmed.StartsWith("END", StringComparison.OrdinalIgnoreCase) && depth > 0)
            {
                depth--;
            }

            if (depth == 0 && trimmed.EndsWith(';'))
            {
                statements.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.ToString().Trim().Length > 0)
        {
            statements.Add(current.ToString());
        }

        return statements;
    }

    /// <summary>
    /// PostgreSQL DO $$ ... $$ blocks contain semicolons that do not end the statement.
    /// </summary>
    private static IEnumerable<string> SplitPostgres(string script)
    {
        List<string> statements = [];
        StringBuilder current = new StringBuilder();
        bool inDollarBlock = false;

        foreach (string line in script.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Contains("$$", StringComparison.Ordinal))
            {
                // A line may both open and close, but the generated scripts never do that.
                inDollarBlock = !inDollarBlock;
            }

            current.AppendLine(line);

            if (!inDollarBlock && line.TrimEnd().EndsWith(';'))
            {
                statements.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.ToString().Trim().Length > 0)
        {
            statements.Add(current.ToString());
        }

        return statements;
    }

    /// <summary>
    /// Firebird scripts switch the terminator with SET TERM so that EXECUTE BLOCK bodies can use
    /// semicolons. The .NET provider has no SET TERM, so honour it here and strip it out.
    /// </summary>
    private static IEnumerable<string> SplitFirebird(string script)
    {
        List<string> statements = [];
        StringBuilder current = new StringBuilder();
        string terminator = ";";

        foreach (string line in script.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("SET TERM", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    terminator = parts[2];
                }

                continue;
            }

            current.AppendLine(line);

            if (trimmed.EndsWith(terminator, StringComparison.Ordinal))
            {
                string statement = current.ToString().TrimEnd();
                statements.Add(statement[..^terminator.Length]);
                current.Clear();
            }
        }

        if (current.ToString().Trim().Length > 0)
        {
            statements.Add(current.ToString());
        }

        return statements;
    }

    /// <summary>
    /// Drops whole-line comments and trailing <c>-- ...</c> comments. Trailing comments matter
    /// because the index scripts annotate statements with the index that covers them.
    /// </summary>
    private static string StripComments(string batch)
    {
        List<string> kept = [];

        foreach (string line in batch.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            // A '--' inside a string literal would be mangled here, but no script has one.
            int comment = line.IndexOf("--", StringComparison.Ordinal);
            kept.Add(comment >= 0 ? line[..comment] : line);
        }

        return string.Join('\n', kept);
    }

    private static string BaselineScript(string dialect)
    {
        string script = File.ReadAllText(ResolveRepositoryFile("src", "Quartz.Tests.Integration", "SchemaBaselines", "3.16", $"tables_{dialect}.sql"));
        return PrepareForMigratedPrefix(script, dialect);
    }

    private static string CurrentTableScript(string dialect)
    {
        // Used only where the test creates the fresh schema itself (SQLite); elsewhere the test
        // environment has already created it from this same file.
        return StripDropStatements(File.ReadAllText(ResolveRepositoryFile("database", "tables", $"tables_{dialect}.sql")));
    }

    private static string MigrationScript(string version, string name, string dialect)
    {
        string script = File.ReadAllText(ResolveRepositoryFile("database", "migrations", version, $"{name}_{dialect}.sql"));
        return Rewrite(script);
    }

    private static string PrepareForMigratedPrefix(string script, string dialect)
    {
        if (dialect is "sqlServer")
        {
            // The script has its own switch for skipping the teardown block. Use it rather than
            // deleting DROP lines out of the middle of an IF ... BEGIN ... END, which breaks it.
            script = script.Replace("@DropDb BIT = 1", "@DropDb BIT = 0", StringComparison.Ordinal);
        }
        else
        {
            script = StripDropStatements(script);
        }

        if (dialect is "sqlServer")
        {
            // The baseline creates and switches databases; the test works inside the existing one.
            script = string.Join('\n', script
                .Replace("\r\n", "\n")
                .Split('\n')
                .Where(l => !l.TrimStart().StartsWith("USE ", StringComparison.OrdinalIgnoreCase)
                            && !l.TrimStart().StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase)
                            && !l.TrimStart().StartsWith("ALTER DATABASE", StringComparison.OrdinalIgnoreCase)
                            && !l.Contains("enter_db_name_here", StringComparison.OrdinalIgnoreCase)
                            && !l.Contains("enter_path_here", StringComparison.OrdinalIgnoreCase)));
        }

        return Rewrite(script);
    }

    /// <summary>
    /// Retargets a script at the <c>QRTZM_</c> prefix. This is exactly the substitution every
    /// script's header tells the reader to make, so it doubles as a test of that instruction.
    /// </summary>
    private static string Rewrite(string script)
    {
        script = script
            .Replace(FreshPrefix, MigratedPrefix, StringComparison.Ordinal)
            .Replace(FreshPrefix.ToLowerInvariant(), MigratedPrefix.ToLowerInvariant(), StringComparison.Ordinal);

        // The SQLite script's referential-integrity triggers have fixed names that carry no table
        // prefix, so they would collide with the fresh schema's.
        foreach (string trigger in SqliteTriggerNames)
        {
            script = script.Replace(trigger, "M_" + trigger, StringComparison.Ordinal);
        }

        return script;
    }

    private static readonly string[] SqliteTriggerNames =
    [
        "DELETE_SIMPLE_TRIGGER", "DELETE_SIMPROP_TRIGGER", "DELETE_CRON_TRIGGER", "DELETE_BLOB_TRIGGER"
    ];

    private static string StripDropStatements(string script)
    {
        IEnumerable<string> lines = script
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("DROP TABLE", StringComparison.OrdinalIgnoreCase));

        return string.Join('\n', lines);
    }

    private static string RequireConnectionString(string variable)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(value))
        {
            Assert.Ignore($"{variable} is not set; the database container for this test is not running.");
        }

        return value;
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate required script file.", relativePath);
    }
}
