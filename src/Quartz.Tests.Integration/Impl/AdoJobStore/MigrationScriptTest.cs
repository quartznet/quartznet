#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Applies the migrations under <c>database/migrations/</c> to a 3.16-era schema and asserts the
/// result is the schema a fresh 4.0 install produces, then runs a scheduler against it.
/// </summary>
/// <remarks>
/// <para>
/// The migrated schemas are built under their own table prefixes, alongside the <c>QRTZ_</c> schema
/// the test environment already created from the current <c>database/tables/</c> script. That gives
/// isolation without a second database, and incidentally proves the "replace 'QRTZ_' with your
/// configured table prefix" instruction each script carries actually works.
/// </para>
/// <para>
/// Two upgrade routes reach 4.0 and both are covered: <c>QRTZM_</c> takes the stepped route,
/// applying the optional 3.17-3.20 migrations first, so the 4.0 script lands on a partially-migrated
/// database; <c>QRTZD_</c> takes the direct route, applying nothing but the mandatory 4.0 script to
/// an untouched 3.16 database. Either way the end state has to be table-for-table, column-for-column
/// and index-for-index what a fresh install produces. Every dialect but SQLite says in its header
/// that it serves both routes; SQLite's says the opposite, and the stepped route only reaches 4.0
/// there because <see cref="SqliteAddColumnAlreadyApplied" /> does by hand what that header tells a
/// reader to do.
/// </para>
/// <para>
/// Re-runnability is asserted on the stepped route, where every migration is applied twice. SQLite is
/// the exception <c>database/README.md</c> records: it has no conditional DDL for <c>ADD COLUMN</c>,
/// so <see cref="SqliteAddColumnAlreadyApplied" /> performs that one check on the script's behalf.
/// Nothing else is excused — an unguarded <c>CREATE INDEX</c> still has to survive the second pass.
/// </para>
/// <para>
/// Structural equality is necessary but not sufficient, so <c>UpgradedSchemaRunsScheduler</c> also
/// runs a scheduler against the migrated schema: one trigger of every persisted family, one of them
/// pinned to a preferred node, all fired, read back and shut down cleanly. That exercises the columns
/// the migrations added rather than merely observing that they exist.
/// </para>
/// <para>
/// <c>FreshInstallHonoursDropSwitch</c> covers the other end: every fresh-install script destroys an
/// existing Quartz schema before it recreates one, and every script can be told not to. Each dialect
/// says so in its own idiom -- a variable on all of them but SQLite, which has none and so delimits
/// the block to delete instead -- and the case works the switch both ways against real data.
/// </para>
/// </remarks>
[Category("migrations")]
public class MigrationScriptTest
{
    private const string FreshPrefix = "QRTZ_";

    /// <summary>Schema built by 3.16 + the optional 3.17-3.20 migrations + the 4.0 upgrade.</summary>
    private const string SteppedPrefix = "QRTZM_";

    /// <summary>Schema built by 3.16 + the 4.0 upgrade alone, which is what most databases will do.</summary>
    private const string DirectPrefix = "QRTZD_";

    /// <summary>Throwaway schema the drop-switch cases build, fill and then have the script destroy.</summary>
    private const string DropSwitchPrefix = "QRTZS_";

    /// <summary>Migrations that apply on top of the 3.16 baseline, in the order they must run.</summary>
    private static readonly (string Version, string Name)[] SteppedChain =
    [
        ("3.17", "add_misfire_orig_fire_time"),
        ("3.18", "add_execution_group"),
        ("3.19", "add_preferred_node"),
        ("3.20", "index_alignment"),
        ("4.0", "schema_30_to_40_upgrade")
    ];

    /// <summary>
    /// The mandatory upgrade on its own. Its header says it supersedes the four above, so a database
    /// that never ran any of them has to arrive at the same place.
    /// </summary>
    private static readonly (string Version, string Name)[] DirectChain =
    [
        ("4.0", "schema_30_to_40_upgrade")
    ];

    [Test]
    [Category("db-sqlite")]
    public Task SqliteSchemaMatchesFreshInstall()
    {
        return WithSqliteAsync(async (connection, _) =>
        {
            await BuildMigratedSchemaAsync(connection, "sqlite", SteppedPrefix, SteppedChain, assertRerunnable: true);
            await AssertSchemaMatchesAsync(connection, "sqlite", SteppedPrefix);
        });
    }

    [Test]
    [Category("db-sqlite")]
    public Task SqliteUpgradedSchemaRunsScheduler()
    {
        return WithSqliteAsync(async (connection, connectionString) =>
        {
            await BuildMigratedSchemaAsync(connection, "sqlite", DirectPrefix, DirectChain, assertRerunnable: false);
            await AssertSchemaMatchesAsync(connection, "sqlite", DirectPrefix);
            await MigratedSchemaWorkload.RunAsync(connection, "sqlite", connectionString, DirectPrefix);
        });
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task SqlServerSchemaMatchesFreshInstall()
    {
        await using SqlConnection connection = new SqlConnection(RequireConnectionString("MSSQL_CONNECTION_STRING"));
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "sqlServer", SteppedPrefix, SteppedChain, assertRerunnable: true);
        await AssertSchemaMatchesAsync(connection, "sqlServer", SteppedPrefix);
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task SqlServerUpgradedSchemaRunsScheduler()
    {
        string connectionString = RequireConnectionString("MSSQL_CONNECTION_STRING");

        await using SqlConnection connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "sqlServer", DirectPrefix, DirectChain, assertRerunnable: false);
        await AssertSchemaMatchesAsync(connection, "sqlServer", DirectPrefix);
        await MigratedSchemaWorkload.RunAsync(connection, "sqlServer", connectionString, DirectPrefix);
    }

    [Test]
    [Category("db-postgres")]
    public async Task PostgreSqlSchemaMatchesFreshInstall()
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(RequireConnectionString("PG_CONNECTION_STRING"));
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "postgres", SteppedPrefix, SteppedChain, assertRerunnable: true);
        await AssertSchemaMatchesAsync(connection, "postgres", SteppedPrefix);
    }

    [Test]
    [Category("db-postgres")]
    public async Task PostgreSqlUpgradedSchemaRunsScheduler()
    {
        string connectionString = RequireConnectionString("PG_CONNECTION_STRING");

        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "postgres", DirectPrefix, DirectChain, assertRerunnable: false);
        await AssertSchemaMatchesAsync(connection, "postgres", DirectPrefix);
        await MigratedSchemaWorkload.RunAsync(connection, "postgres", connectionString, DirectPrefix);
    }

    [Test]
    [Category("db-mysql")]
    public async Task MySqlSchemaMatchesFreshInstall()
    {
        await using MySqlConnection connection = new MySqlConnection(RequireConnectionString("MYSQL_CONNECTION_STRING"));
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "mysql_innodb", SteppedPrefix, SteppedChain, assertRerunnable: true);
        await AssertSchemaMatchesAsync(connection, "mysql_innodb", SteppedPrefix);
    }

    [Test]
    [Category("db-mysql")]
    public async Task MySqlUpgradedSchemaRunsScheduler()
    {
        string connectionString = RequireConnectionString("MYSQL_CONNECTION_STRING");

        await using MySqlConnection connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "mysql_innodb", DirectPrefix, DirectChain, assertRerunnable: false);
        await AssertSchemaMatchesAsync(connection, "mysql_innodb", DirectPrefix);
        await MigratedSchemaWorkload.RunAsync(connection, "mysql_innodb", connectionString, DirectPrefix);
    }

    [Test]
    [Category("db-oracle")]
    public async Task OracleSchemaMatchesFreshInstall()
    {
        await using OracleConnection connection = new OracleConnection(RequireConnectionString("ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "oracle", SteppedPrefix, SteppedChain, assertRerunnable: true);
        await AssertSchemaMatchesAsync(connection, "oracle", SteppedPrefix);
    }

    [Test]
    [Category("db-oracle")]
    public async Task OracleUpgradedSchemaRunsScheduler()
    {
        string connectionString = RequireConnectionString("ORACLE_CONNECTION_STRING");

        await using OracleConnection connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "oracle", DirectPrefix, DirectChain, assertRerunnable: false);
        await AssertSchemaMatchesAsync(connection, "oracle", DirectPrefix);
        await MigratedSchemaWorkload.RunAsync(connection, "oracle", connectionString, DirectPrefix);
    }

    [Test]
    [Category("db-firebird")]
    public async Task FirebirdSchemaMatchesFreshInstall()
    {
        await using FbConnection connection = new FbConnection(RequireConnectionString("FIREBIRD_CONNECTION_STRING"));
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "firebird", SteppedPrefix, SteppedChain, assertRerunnable: true);
        await AssertSchemaMatchesAsync(connection, "firebird", SteppedPrefix);
    }

    [Test]
    [Category("db-firebird")]
    public async Task FirebirdUpgradedSchemaRunsScheduler()
    {
        string connectionString = RequireConnectionString("FIREBIRD_CONNECTION_STRING");

        await using FbConnection connection = new FbConnection(connectionString);
        await connection.OpenAsync();

        await BuildMigratedSchemaAsync(connection, "firebird", DirectPrefix, DirectChain, assertRerunnable: false);
        await AssertSchemaMatchesAsync(connection, "firebird", DirectPrefix);
        await MigratedSchemaWorkload.RunAsync(connection, "firebird", connectionString, DirectPrefix);
    }

    [Test]
    [Category("db-sqlite")]
    public Task SqliteFreshInstallHonoursDropSwitch()
    {
        return WithSqliteAsync((connection, _) => AssertDropSwitchHonouredAsync(connection, "sqlite"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task SqlServerFreshInstallHonoursDropSwitch()
    {
        await using SqlConnection connection = new SqlConnection(RequireConnectionString("MSSQL_CONNECTION_STRING"));
        await connection.OpenAsync();

        await AssertDropSwitchHonouredAsync(connection, "sqlServer");
    }

    [Test]
    [Category("db-postgres")]
    public async Task PostgreSqlFreshInstallHonoursDropSwitch()
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(RequireConnectionString("PG_CONNECTION_STRING"));
        await connection.OpenAsync();

        await AssertDropSwitchHonouredAsync(connection, "postgres");
    }

    [Test]
    [Category("db-mysql")]
    public async Task MySqlFreshInstallHonoursDropSwitch()
    {
        await using MySqlConnection connection = new MySqlConnection(RequireConnectionString("MYSQL_CONNECTION_STRING"));
        await connection.OpenAsync();

        await AssertDropSwitchHonouredAsync(connection, "mysql_innodb");
    }

    [Test]
    [Category("db-oracle")]
    public async Task OracleFreshInstallHonoursDropSwitch()
    {
        await using OracleConnection connection = new OracleConnection(RequireConnectionString("ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();

        await AssertDropSwitchHonouredAsync(connection, "oracle");
    }

    [Test]
    [Category("db-firebird")]
    public async Task FirebirdFreshInstallHonoursDropSwitch()
    {
        await using FbConnection connection = new FbConnection(RequireConnectionString("FIREBIRD_CONNECTION_STRING"));
        await connection.OpenAsync();

        await AssertDropSwitchHonouredAsync(connection, "firebird");
    }

    /// <summary>
    /// Builds a schema from the fresh-install script exactly as it ships, puts a row in it, and runs
    /// the script's teardown twice: once with the drop switched off, which has to leave the row
    /// alone, and once as it ships, which has to take the whole schema with it.
    /// </summary>
    private static async Task AssertDropSwitchHonouredAsync(DbConnection connection, string dialect)
    {
        string script = Rewrite(FreshInstallScript(dialect), DropSwitchPrefix);

        await ExecuteScriptAsync(connection, script, dialect);
        await InsertJobDetailAsync(connection, dialect, DropSwitchPrefix);

        // What a reader who followed the header sentence ends up running.
        await ExecuteScriptAsync(connection, Teardown(script, dialect, drop: false), dialect);

        long survivors = await CountJobDetailsAsync(connection, DropSwitchPrefix);
        survivors.Should().Be(1, $"the {dialect} script's teardown has to do nothing at all once the drop is switched off, and -1 means it took the table too");

        // The teardown as it ships, which is what the switch exists to be able to decline.
        await ExecuteScriptAsync(connection, Teardown(script, dialect, drop: true), dialect);

        SchemaSnapshot afterDrop = await SchemaSnapshot.ReadAsync(connection, dialect, DropSwitchPrefix);
        afterDrop.Tables.Should().BeEmpty($"the {dialect} script drops an existing schema by default, so the switch left on has to remove every table");
    }

    /// <summary>
    /// The part of a fresh-install script that runs before the first CREATE TABLE: the switch, and
    /// the drops it guards. Running that alone is the only way to observe the switch, because the
    /// CREATE statements after it fail against the schema a declined drop deliberately left standing.
    /// </summary>
    private static string Teardown(string script, string dialect, bool drop)
    {
        string[] lines = script.Replace("\r\n", "\n").Split('\n');

        int firstCreate = Array.FindIndex(lines, l => l.TrimStart().StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase));
        firstCreate.Should().BePositive($"the {dialect} fresh-install script tears the old schema down before it creates the new one");

        string teardown = string.Join('\n', lines[..firstCreate]);
        return drop ? teardown : SwitchDropOff(teardown, dialect);
    }

    /// <summary>
    /// Turns the drop off the way the dialect's own header sentence says to, so a script that stops
    /// carrying the switch fails here rather than silently going untested.
    /// </summary>
    private static string SwitchDropOff(string teardown, string dialect)
    {
        if (dialect == "sqlite")
        {
            return RemoveDelimitedDropBlock(teardown);
        }

        (string on, string off) = dialect switch
        {
            "sqlServer" => ("@DropDb BIT = 1", "@DropDb BIT = 0"),
            "postgres" => ("DropDb INT := 1", "DropDb INT := 0"),
            "mysql_innodb" => ("SET @DropDb = 1", "SET @DropDb = 0"),
            "oracle" => ("DropDb NUMBER := 1", "DropDb NUMBER := 0"),
            "firebird" => ("DropDb INTEGER = 1", "DropDb INTEGER = 0"),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "no drop switch known for this dialect")
        };

        string switched = teardown.Replace(on, off, StringComparison.Ordinal);
        switched.Should().NotBe(teardown, $"the {dialect} script has to declare '{on}', which is the variable its header tells the reader to set to 0");

        return switched;
    }

    private const string DropBlockBegin = "BEGIN DROP TABLES";
    private const string DropBlockEnd = "END DROP TABLES";

    /// <summary>
    /// SQLite has neither variables nor a statement-level IF, so its header tells the reader to
    /// delete the drops rather than switch them off. This deletes them, which also pins that the
    /// markers the header sends the reader looking for are still there.
    /// </summary>
    private static string RemoveDelimitedDropBlock(string teardown)
    {
        string[] lines = teardown.Replace("\r\n", "\n").Split('\n');

        int begin = Array.FindIndex(lines, l => l.Contains(DropBlockBegin, StringComparison.Ordinal));
        int end = Array.FindIndex(lines, l => l.Contains(DropBlockEnd, StringComparison.Ordinal));

        begin.Should().BeGreaterThanOrEqualTo(0, $"the SQLite script's drops have to sit behind a '{DropBlockBegin}' marker, since deleting them is all a reader can do");
        end.Should().BeGreaterThan(begin, $"the SQLite script's '{DropBlockEnd}' marker has to follow its '{DropBlockBegin}' one");

        return string.Join('\n', lines[..begin].Concat(lines[(end + 1)..]));
    }

    /// <summary>One row for the drop to take away, or leave alone.</summary>
    private static async Task InsertJobDetailAsync(DbConnection connection, string dialect, string prefix)
    {
        // The flag columns are a different type in every dialect, so their literals differ too.
        (string yes, string no) = dialect switch
        {
            "postgres" => ("TRUE", "FALSE"),
            "oracle" => ("'1'", "'0'"),
            _ => ("1", "0")
        };

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {prefix}JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY) "
            + $"VALUES ('DropSwitchScheduler', 'survivor', 'DEFAULT', 'Quartz.Job.NoOpJob, Quartz', {yes}, {no}, {no}, {no})";

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Rows in the job-details table, or -1 if there is no such table any more. A drop that ran when
    /// it was told not to takes the table with the row, and the count that reports that is a better
    /// failure than the provider exception it would otherwise throw.
    /// </summary>
    private static async Task<long> CountJobDetailsAsync(DbConnection connection, string prefix)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {prefix}JOB_DETAILS";

        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        catch (DbException)
        {
            return -1;
        }
    }

    /// <summary>
    /// Creates the 3.16 schema under <paramref name="prefix" /> and walks it up the given chain,
    /// optionally applying every migration a second time to prove the guards make them no-ops.
    /// </summary>
    private static async Task BuildMigratedSchemaAsync(
        DbConnection connection,
        string dialect,
        string prefix,
        (string Version, string Name)[] chain,
        bool assertRerunnable)
    {
        await ExecuteScriptAsync(connection, BaselineScript(dialect, prefix), dialect);

        // A second pass is the idempotence assertion: every migration checks before it acts, so
        // applying one to a database that already has it has to be a no-op rather than an error.
        int passes = assertRerunnable ? 2 : 1;

        for (int pass = 0; pass < passes; pass++)
        {
            foreach ((string version, string name) in chain)
            {
                await ExecuteScriptAsync(connection, MigrationScript(version, name, dialect, prefix), dialect);
            }
        }
    }

    private static async Task AssertSchemaMatchesAsync(DbConnection connection, string dialect, string prefix)
    {
        SchemaSnapshot fresh = await SchemaSnapshot.ReadAsync(connection, dialect, FreshPrefix);
        SchemaSnapshot migrated = await SchemaSnapshot.ReadAsync(connection, dialect, prefix);

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
            if (await SqliteAddColumnAlreadyApplied(connection, batch))
            {
                continue;
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static readonly Regex SqliteAddColumn = new Regex(
        @"^\s*ALTER\s+TABLE\s+(?<table>\w+)\s+ADD\s+COLUMN\s+(?<column>\w+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Does the existence check SQLite's DDL cannot express, so that a column another migration in
    /// the chain already added is skipped rather than failing the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other dialect guards its own <c>ADD COLUMN</c>, which is what lets the same migration
    /// run twice, and what lets the 4.0 script land on a database that already took some of the
    /// optional 3.17-3.19 ones. SQLite has no conditional DDL for it, so both of those raise
    /// "duplicate column name" — a limitation <c>database/README.md</c> records and every SQLite
    /// script's header repeats, the 4.0 upgrade included: it is NOT IDEMPOTENT, and it tells a
    /// reader whose database is partially migrated to consult <c>PRAGMA table_info</c> and apply
    /// only the sections whose columns are absent. This shim is that instruction, executed, and it
    /// is the only reason the stepped and re-run passes get through. It deliberately does nothing
    /// else: an unguarded <c>CREATE INDEX</c> would still fail the re-run, which is what the second
    /// pass is there to catch.
    /// </para>
    /// </remarks>
    private static async Task<bool> SqliteAddColumnAlreadyApplied(DbConnection connection, string batch)
    {
        Match match = SqliteAddColumn.Match(batch);
        if (!match.Success)
        {
            return false;
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM pragma_table_info('{match.Groups["table"].Value}') WHERE UPPER(name) = '{match.Groups["column"].Value.ToUpperInvariant()}'";

        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
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

    private static string BaselineScript(string dialect, string prefix)
    {
        string script = File.ReadAllText(ResolveRepositoryFile("src", "Quartz.Tests.Integration", "SchemaBaselines", "3.16", $"tables_{dialect}.sql"));
        return PrepareForMigratedPrefix(script, dialect, prefix);
    }

    /// <summary>The fresh-install script exactly as it ships, teardown included.</summary>
    private static string FreshInstallScript(string dialect)
    {
        return File.ReadAllText(ResolveRepositoryFile("database", "tables", $"tables_{dialect}.sql"));
    }

    private static string CurrentTableScript(string dialect)
    {
        // Used only where the test creates the fresh schema itself (SQLite); elsewhere the test
        // environment has already created it from this same file.
        return StripDropStatements(FreshInstallScript(dialect));
    }

    private static string MigrationScript(string version, string name, string dialect, string prefix)
    {
        string script = File.ReadAllText(ResolveRepositoryFile("database", "migrations", version, $"{name}_{dialect}.sql"));
        return Rewrite(script, prefix);
    }

    private static string PrepareForMigratedPrefix(string script, string dialect, string prefix)
    {
        if (dialect == "sqlite")
        {
            // The 3.16 script's referential-integrity triggers carry no table prefix, so they would
            // collide with the current schema's and with each other's. The current script names them
            // QRTZ_DELETE_*, which the prefix substitution below retargets on its own; this baseline
            // is frozen at what 3.16 shipped, so it needs the prefix put on by hand.
            script = PrefixSqliteTriggers(script, SqliteTriggerPrefix(prefix));
        }

        if (dialect is "sqlServer")
        {
            // The script has its own switch for skipping the teardown block. Use it rather than
            // deleting DROP lines out of the middle of an IF ... BEGIN ... END, which breaks it.
            script = script.Replace("@DropDb BIT = 1", "@DropDb BIT = 0", StringComparison.Ordinal);

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
        else
        {
            script = StripDropStatements(script);
        }

        return Rewrite(script, prefix);
    }

    /// <summary>
    /// Retargets a script at the given table prefix. This is exactly the substitution every script's
    /// header tells the reader to make, so it doubles as a test of that instruction.
    /// </summary>
    private static string Rewrite(string script, string prefix)
    {
        return script
            .Replace(FreshPrefix, prefix, StringComparison.Ordinal)
            .Replace(FreshPrefix.ToLowerInvariant(), prefix.ToLowerInvariant(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Puts a prefix on the four unprefixed trigger names the 3.16 SQLite script declares, so that
    /// two schemas built from it can share one database.
    /// </summary>
    private static string PrefixSqliteTriggers(string script, string triggerPrefix)
    {
        foreach (string trigger in SqliteTriggerNames)
        {
            script = script.Replace(trigger, triggerPrefix + trigger, StringComparison.Ordinal);
        }

        return script;
    }

    /// <summary>
    /// Only the two baseline-built schemas need one: the current script names its triggers
    /// <c>QRTZ_DELETE_*</c>, which <see cref="Rewrite" /> retargets like every other object.
    /// </summary>
    private static string SqliteTriggerPrefix(string prefix) => prefix switch
    {
        SteppedPrefix => "M_",
        DirectPrefix => "D_",
        _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "no SQLite trigger prefix for this table prefix")
    };

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

    /// <summary>
    /// SQLite has no container, so the test owns the whole database file: it creates the fresh schema
    /// the comparison needs, hands the caller an open connection, and deletes the file afterwards.
    /// </summary>
    private static async Task WithSqliteAsync(Func<SqliteConnection, string, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"quartz-migration-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={file};";

        try
        {
            await using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                // The fresh schema the migrated one has to end up matching. The other dialects get
                // this from the test environment, which creates it when the container starts.
                await ExecuteScriptAsync(connection, CurrentTableScript("sqlite"), "sqlite");

                await body(connection, connectionString);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // the file is only test scratch space, leaving it behind is not worth failing over
            }
        }
    }
}
