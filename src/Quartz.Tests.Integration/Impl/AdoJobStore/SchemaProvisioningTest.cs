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

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A store creating its own schema, in a real database of every dialect, and the schema it creates
/// compared with the one <c>database/tables/tables_&lt;dialect&gt;.sql</c> creates.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="MigrationScriptTest" />, and asking the same question of a third route to a
/// schema. That one proves a migrated schema is the schema a fresh install produces; this one proves a
/// provisioned schema is, table for table, column for column and index for index — which is the whole
/// contract, because the store validates and then runs against whatever provisioning left behind.
/// <see cref="SchemaSnapshot" /> is shared so the two comparisons are the same comparison.
/// </para>
/// <para>
/// The provisioned schemas are built under table prefixes of their own, beside the <c>QRTZ_</c> schema
/// the test environment created from the current fresh-install script. So the prefix substitution is
/// under test too: an object the generated script still spells <c>QRTZ_</c> would land in the fresh
/// schema and be found there by the comparison.
/// </para>
/// <para>
/// Three things are asserted per dialect, in one case each because they are one story: the schema a
/// scheduler provisions matches a fresh install, provisioning it a second time changes nothing, and a
/// workload runs against it. A separate case starts two schedulers at once against one empty prefix,
/// which is what a cluster coming up from an empty database does — only one of them can create the
/// tables, and both have to start.
/// </para>
/// <para>
/// The unit-test project has the SQLite half of this without a container, in
/// <c>SchemaProvisioningSqliteTest</c>; what it cannot do is compare the catalog with a fresh install's,
/// which is where a column that matches by name but not by type shows up.
/// </para>
/// </remarks>
/// <remarks>
/// Not parallelizable, unlike the rest of this assembly. Two of these cases create a schema at the
/// same moment on purpose, and Firebird serializes DDL through its own catalog and answers whoever
/// loses with a deadlock rather than a wait — which lands on whatever other fixture happened to be
/// running a script at the time. The store copes with losing that race; a fresh-install script run by
/// <c>MigrationScriptTest</c> does not, and should not have to.
/// </remarks>
[Category("provisioning")]
[NonParallelizable]
public class SchemaProvisioningTest
{
    private const string FreshPrefix = "QRTZ_";

    /// <summary>The schema a scheduler provisions for itself, compared with the fresh one.</summary>
    private const string ProvisionedPrefix = "QRTZP_";

    /// <summary>The empty prefix two schedulers start against at the same time.</summary>
    private const string RacePrefix = "QRTZC_";

    [Test]
    [Category("db-sqlite")]
    public Task SqliteProvisionsASchemaMatchingAFreshInstall()
    {
        return WithSqliteAsync((connection, connectionString) =>
            AssertProvisionedSchemaAsync(connection, "sqlite", connectionString));
    }

    [Test]
    [Category("db-sqlite")]
    public Task SqliteProvisionsOnceWhenTwoSchedulersStartTogether()
    {
        return WithSqliteAsync((connection, connectionString) =>
            AssertRaceAsync(connection, "sqlite", connectionString));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task SqlServerProvisionsASchemaMatchingAFreshInstall()
    {
        string connectionString = RequireConnectionString("MSSQL_CONNECTION_STRING");

        await using SqlConnection connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await AssertProvisionedSchemaAsync(connection, "sqlServer", connectionString);
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task SqlServerProvisionsOnceWhenTwoSchedulersStartTogether()
    {
        string connectionString = RequireConnectionString("MSSQL_CONNECTION_STRING");

        await using SqlConnection connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await AssertRaceAsync(connection, "sqlServer", connectionString);
    }

    [Test]
    [Category("db-postgres")]
    public async Task PostgreSqlProvisionsASchemaMatchingAFreshInstall()
    {
        string connectionString = RequireConnectionString("PG_CONNECTION_STRING");

        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await AssertProvisionedSchemaAsync(connection, "postgres", connectionString);
    }

    [Test]
    [Category("db-postgres")]
    public async Task PostgreSqlProvisionsOnceWhenTwoSchedulersStartTogether()
    {
        string connectionString = RequireConnectionString("PG_CONNECTION_STRING");

        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await AssertRaceAsync(connection, "postgres", connectionString);
    }

    [Test]
    [Category("db-mysql")]
    public async Task MySqlProvisionsASchemaMatchingAFreshInstall()
    {
        string connectionString = RequireConnectionString("MYSQL_CONNECTION_STRING");

        await using MySqlConnection connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await AssertProvisionedSchemaAsync(connection, "mysql_innodb", connectionString);
    }

    [Test]
    [Category("db-mysql")]
    public async Task MySqlProvisionsOnceWhenTwoSchedulersStartTogether()
    {
        string connectionString = RequireConnectionString("MYSQL_CONNECTION_STRING");

        await using MySqlConnection connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await AssertRaceAsync(connection, "mysql_innodb", connectionString);
    }

    [Test]
    [Category("db-oracle")]
    public async Task OracleProvisionsASchemaMatchingAFreshInstall()
    {
        string connectionString = RequireConnectionString("ORACLE_CONNECTION_STRING");

        await using OracleConnection connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        await AssertProvisionedSchemaAsync(connection, "oracle", connectionString);
    }

    [Test]
    [Category("db-oracle")]
    public async Task OracleProvisionsOnceWhenTwoSchedulersStartTogether()
    {
        string connectionString = RequireConnectionString("ORACLE_CONNECTION_STRING");

        await using OracleConnection connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        await AssertRaceAsync(connection, "oracle", connectionString);
    }

    [Test]
    [Category("db-firebird")]
    public async Task FirebirdProvisionsASchemaMatchingAFreshInstall()
    {
        string connectionString = RequireConnectionString("FIREBIRD_CONNECTION_STRING");

        await using FbConnection connection = new FbConnection(connectionString);
        await connection.OpenAsync();

        await AssertProvisionedSchemaAsync(connection, "firebird", connectionString);
    }

    [Test]
    [Category("db-firebird")]
    public async Task FirebirdProvisionsOnceWhenTwoSchedulersStartTogether()
    {
        string connectionString = RequireConnectionString("FIREBIRD_CONNECTION_STRING");

        await using FbConnection connection = new FbConnection(connectionString);
        await connection.OpenAsync();

        await AssertRaceAsync(connection, "firebird", connectionString);
    }

    /// <summary>
    /// Starts a scheduler that provisions, compares what it created with the fresh schema, starts a
    /// second one over the same prefix to show the second pass changes nothing, and then runs a
    /// workload against the result.
    /// </summary>
    private static async Task AssertProvisionedSchemaAsync(DbConnection connection, string dialect, string connectionString)
    {
        await StartAndShutDownAsync(dialect, connectionString, ProvisionedPrefix, $"Provisioning_{dialect}_first");

        SchemaSnapshot fresh = await SchemaSnapshot.ReadAsync(connection, dialect, FreshPrefix);
        SchemaSnapshot provisioned = await SchemaSnapshot.ReadAsync(connection, dialect, ProvisionedPrefix);

        fresh.Tables.Should().NotBeEmpty(
            "the fresh install schema must exist for the comparison to mean anything");

        provisioned.Tables.Should().BeEquivalentTo(fresh.Tables,
            "a provisioned schema has to have the same tables as a fresh install — the store validates "
            + "for them a moment later, and runs against them for the rest of its life");
        provisioned.Columns.Should().BeEquivalentTo(fresh.Columns,
            "validation is table-level, so a column that provisioning got wrong gets past startup and "
            + "fails on the first statement that names it");
        provisioned.Indexes.Should().BeEquivalentTo(fresh.Indexes,
            "an index missing from a provisioned schema is a scheduler that works and then does not, at "
            + "whatever number of triggers the scans stop being free");

        // A second scheduler over the same prefix: every statement is guarded, so this has to be a
        // no-op rather than an error, and it must not have altered anything on the way through.
        await StartAndShutDownAsync(dialect, connectionString, ProvisionedPrefix, $"Provisioning_{dialect}_second");

        SchemaSnapshot afterSecondPass = await SchemaSnapshot.ReadAsync(connection, dialect, ProvisionedPrefix);

        afterSecondPass.Tables.Should().BeEquivalentTo(provisioned.Tables);
        afterSecondPass.Columns.Should().BeEquivalentTo(provisioned.Columns);
        afterSecondPass.Indexes.Should().BeEquivalentTo(provisioned.Indexes,
            "provisioning creates what is missing and touches nothing else, so running it against a "
            + "schema it already created leaves that schema exactly as it was");

        // Shape is not behaviour: one trigger of every persisted family, fired and read back.
        await MigratedSchemaWorkload.RunAsync(connection, dialect, connectionString, ProvisionedPrefix);
    }

    /// <summary>
    /// Two schedulers over one empty prefix at once, which is what a cluster coming up against an
    /// empty database does. Only one of them can create the tables; both have to start.
    /// </summary>
    /// <remarks>
    /// They carry different scheduler names, so at the row level they are two tenants rather than two
    /// nodes — <c>SCHED_NAME</c> is what makes a cluster a cluster. At the schema level the two
    /// arrangements are the same one, and it is the schema this is about: two stores creating one
    /// table set at the same moment.
    /// </remarks>
    private static async Task AssertRaceAsync(DbConnection connection, string dialect, string connectionString)
    {
        // Clustered so that both take database locks, which is the arrangement a node of a real cluster
        // is in. SQLite is left out of it: it serializes every operation through an in-process gate of
        // its own, and clustering it is not something to encourage.
        bool clustered = dialect != "sqlite";

        // Built concurrently, because provisioning happens while the job store is initialized -- which
        // is during construction, not during Start.
        Task<IScheduler> first = BuildScheduler(dialect, connectionString, RacePrefix, $"Race_{dialect}_one", clustered);
        Task<IScheduler> second = BuildScheduler(dialect, connectionString, RacePrefix, $"Race_{dialect}_two", clustered);

        IScheduler[] schedulers = await Task.WhenAll(first, second);

        try
        {
            await Task.WhenAll(schedulers.Select(s => s.Start().AsTask()));

            schedulers.Should().AllSatisfy(s => s.Status.Should().Be(SchedulerStatus.Running),
                "whichever store lost the race sees its create fail against tables the other had just "
                + "made, finds that the schema validates, and carries on — provisioning that only the "
                + "winner of a race can start under is provisioning nobody can deploy");
        }
        finally
        {
            foreach (IScheduler scheduler in schedulers)
            {
                await scheduler.Shutdown(waitForJobsToComplete: false);
            }
        }

        SchemaSnapshot fresh = await SchemaSnapshot.ReadAsync(connection, dialect, FreshPrefix);
        SchemaSnapshot raced = await SchemaSnapshot.ReadAsync(connection, dialect, RacePrefix);

        raced.Tables.Should().BeEquivalentTo(fresh.Tables,
            "the store that lost the race must not have half-created a second copy of anything");
        raced.Columns.Should().BeEquivalentTo(fresh.Columns);
        raced.Indexes.Should().BeEquivalentTo(fresh.Indexes);
    }

    private static async Task StartAndShutDownAsync(string dialect, string connectionString, string tablePrefix, string schedulerName)
    {
        IScheduler scheduler = await BuildScheduler(dialect, connectionString, tablePrefix, schedulerName, clustered: false);

        try
        {
            await scheduler.Start();
            scheduler.Status.Should().Be(SchedulerStatus.Running,
                "a scheduler that provisioned its own schema has to be a scheduler that then validates it");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static async Task<IScheduler> BuildScheduler(
        string dialect,
        string connectionString,
        string tablePrefix,
        string schedulerName,
        bool clustered)
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        builder.ConfigureScheduler(o =>
        {
            o.InstanceName = schedulerName;
            o.InstanceId = schedulerName;
        });

        builder.UseDefaultThreadPool(x => x.MaxConcurrency = 2);

        builder.UsePersistentStore(store =>
        {
            store.ConfigureStore(o => o.TablePrefix = tablePrefix);
            store.ProvisionSchema();

            if (clustered)
            {
                // Two schedulers over one table set is a cluster, whatever their instance names say.
                // Clustering also makes them take database locks, which is the arrangement where a
                // half-created schema would show.
                store.UseClustering(cluster => cluster.CheckinInterval = TimeSpan.FromSeconds(10));
            }

            MigratedSchemaWorkload.UseDialect(store, dialect, connectionString);
            store.UseSystemTextJsonSerializer();
        });

        return await builder.BuildScheduler();
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

    /// <summary>
    /// SQLite has no container, so the test owns the whole database file: it creates the fresh schema
    /// the comparison needs, hands the caller an open connection, and deletes the file afterwards.
    /// </summary>
    private static async Task WithSqliteAsync(Func<SqliteConnection, string, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"quartz-provisioning-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={file};";

        try
        {
            await using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                await CreateFreshSqliteSchemaAsync(connection);
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

    /// <summary>
    /// The schema a fresh install produces, which on the other five dialects the test environment
    /// creates when the container starts.
    /// </summary>
    private static async Task CreateFreshSqliteSchemaAsync(SqliteConnection connection)
    {
        string script = await File.ReadAllTextAsync(
            ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));

        foreach (string statement in SqliteStatements(script))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Splits the SQLite script on statement-terminating semicolons. A trigger body is a
    /// <c>BEGIN … END;</c> block whose inner semicolons do not end the statement, so the block depth
    /// is tracked rather than every semicolon being a break.
    /// </summary>
    private static IEnumerable<string> SqliteStatements(string script)
    {
        List<string> statements = [];
        System.Text.StringBuilder current = new();
        int depth = 0;

        foreach (string line in script.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

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

        return statements.Where(s => s.Trim().Length > 0);
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
