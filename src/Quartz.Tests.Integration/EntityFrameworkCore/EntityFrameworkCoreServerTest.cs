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

#nullable enable

using System.Data.Common;

using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.MySql;
using AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL;
using AppAny.Quartz.EntityFrameworkCore.Migrations.SqlServer;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

using MySqlConnector;

using FirebirdSql.Data.FirebirdClient;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Tests.Integration.Weasel;
using Quartz.Tests.Unit.EntityFrameworkCore;

namespace Quartz.Tests.Integration.EntityFrameworkCore;

/// <summary>
/// Quartz.EntityFrameworkCore's schema in a real database of each server dialect it has a model for: an empty
/// database migrated, and a database the scripts created and then baselined, each ending with a scheduler running
/// on the tables. <see cref="EntityFrameworkCoreAppAnyServerTest" /> adds a schema AppAny 0.6.3 created, moved, on
/// the three server databases AppAny had a model of.
/// </summary>
/// <remarks>
/// <para>
/// Each test has an empty database of its own on the leg's container, with a fresh install under <c>QRTZ_</c>
/// beside the schema under test, so that <see cref="SchemaSnapshot" /> compares the two in one catalog and
/// <see cref="MigratedSchemaWorkload" /> can show the prefix kept them apart. The migrations run as
/// <c>Migrate()</c> runs them: the provider's SQL for the model's operations, in one transaction.
/// </para>
/// <para>
/// SQLite runs the same steps in <c>Quartz.Tests.Unit</c>, where the coverage gate sees them.
/// </para>
/// </remarks>
public abstract class EntityFrameworkCoreServerTest
{
    private protected abstract string Dialect { get; }

    private protected abstract Task<IQuartzTestDatabase> CreateDatabaseAsync();

    /// <summary>The prefix as Quartz's options take it; PostgreSQL, Oracle and Firebird fold it either way.</summary>
    private protected static Action<QuartzTablesOptions> Prefix(string prefix) => t => t.TablePrefix = prefix;

    [Test]
    public async Task AnEmptyDatabaseMigratedIsTheFreshInstallAndRunsTheScheduler()
    {
        await using IQuartzTestDatabase database = await CreateDatabaseAsync();
        await database.ProvisionAsync("QRTZ_");

        await using (EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(Dialect, Prefix("QRTZE_"), database.ConnectionString))
        {
            await context.MigrateAsync(Prefix("QRTZE_"));
        }

        await using DbConnection connection = await database.OpenAsync();

        (await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZE_")).Should().BeEquivalentTo(
            await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZ_"),
            "a migrated schema is the fresh install's: tables, columns with their types, lengths and nullability, "
            + "indexes, and keys read by what they are, since EF Core names the keys");

        await AssertAcquisitionIndexIsDescendingAsync(database, "QRTZE_");
        await MigratedSchemaWorkload.RunAsync(connection, Dialect, database.ConnectionString, "QRTZE_");
    }

    /// <summary>
    /// A database the 4.3 scripts created, whose first migration was recorded without running, as the
    /// documentation's baseline says: the next migration EF Core scaffolds only adds, and brings it to the fresh
    /// install.
    /// </summary>
    [Test]
    public async Task AScriptCreatedDatabaseBaselinedTakesAnAddOnlyMigration()
    {
        await using IQuartzTestDatabase database = await CreateDatabaseAsync();
        await database.ProvisionAsync("QRTZ_");
        await database.RunBaselineAsync("4.3", "QRTZB_");

        await using EntityFrameworkCoreContext baselined = EntityFrameworkCoreContext.QuartzAsOf(Dialect, "4.3", Prefix("QRTZB_"), database.ConnectionString);
        await using EntityFrameworkCoreContext current = EntityFrameworkCoreContext.Quartz(Dialect, Prefix("QRTZB_"), database.ConnectionString);

        IReadOnlyList<MigrationOperation> upgrade = current.DifferencesFrom(baselined);
        upgrade.Select(o => o.GetType().Name).Distinct().Should().BeSubsetOf(
            [nameof(AddColumnOperation), nameof(CreateTableOperation), nameof(CreateIndexOperation)]);

        await current.ExecuteAsync([.. upgrade, .. current.Migration(m => m.CompleteQuartzSchema(Prefix("QRTZB_")))]);

        await using DbConnection connection = await database.OpenAsync();

        (await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZB_")).WithBlobsAsMediumBlobs(Dialect).Should().BeEquivalentTo(
            await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZ_"));

        await MigratedSchemaWorkload.RunAsync(connection, Dialect, database.ConnectionString, "QRTZB_");
    }

    private protected virtual Task AssertAcquisitionIndexIsDescendingAsync(IQuartzTestDatabase database, string tablePrefix) => Task.CompletedTask;
}

/// <summary>
/// The server databases AppAny 0.6.3 had a model of, which also take a schema AppAny created and move it.
/// </summary>
public abstract class EntityFrameworkCoreAppAnyServerTest : EntityFrameworkCoreServerTest
{
    /// <summary>
    /// A schema AppAny 0.6.3 created, moved by <c>MoveQuartzTablesFromAppAny</c>: what it leaves is exactly what
    /// the move says it leaves, and a scheduler runs on it.
    /// </summary>
    [Test]
    public async Task AnAppAnySchemaMovedRunsTheScheduler()
    {
        await using IQuartzTestDatabase database = await CreateDatabaseAsync();
        await database.ProvisionAsync("QRTZ_");

        await using (EntityFrameworkCoreContext appAny = EntityFrameworkCoreContext.For(
                         Dialect, (model, _) => model.AddQuartz(UseAppAny("QRTZA_")), database.ConnectionString))
        {
            // EnsureCreated's own step: the model's tables, created whether or not the database has others.
            await appAny.ExecuteAsync(appAny.DifferencesFrom(null));
        }

        await using DbConnection connection = await database.OpenAsync();

        await BeforeTheMoveAsync(connection, database.ConnectionString, "QRTZA_");

        await using (EntityFrameworkCoreContext ours = EntityFrameworkCoreContext.Quartz(Dialect, Prefix("QRTZA_"), database.ConnectionString))
        {
            await ours.ExecuteAsync(ours.Migration(m => m.MoveQuartzTablesFromAppAny(Prefix("QRTZA_"))));
        }

        SchemaSnapshot moved = await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZA_");
        SchemaSnapshot fresh = await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZ_");

        moved.Tables.Should().BeEquivalentTo(fresh.Tables);
        moved.Indexes.Should().BeEquivalentTo(fresh.Indexes, "the move creates the history indexes AppAny lacks");
        moved.PrimaryKeys.Should().BeEquivalentTo(fresh.PrimaryKeys);
        Keys(moved.Columns.Except(fresh.Columns)).Should().BeEquivalentTo(LeftColumns,
            "the columns the move leaves as AppAny declared them, which AppAnyModelDiffTest lists as left");
        Keys(moved.ForeignKeys.Except(fresh.ForeignKeys)).Should().BeEquivalentTo(LeftForeignKeys,
            "the foreign keys the move leaves cascading, which AppAnyModelDiffTest lists as left");

        await AssertAcquisitionIndexIsDescendingAsync(database, "QRTZA_");
        await MigratedSchemaWorkload.RunAsync(connection, Dialect, database.ConnectionString, "QRTZA_");
    }

    /// <summary>AppAny's model for this dialect, under a prefix and in the connection's own schema.</summary>
    private protected abstract Action<QuartzModelBuilder> UseAppAny(string prefix);

    /// <summary>The columns, as <c>TABLE|COLUMN</c>, that differ from the fresh install after the move.</summary>
    private protected virtual string[] LeftColumns => [];

    /// <summary>The foreign keys, by their table, that differ from the fresh install after the move.</summary>
    private protected virtual string[] LeftForeignKeys => [];

    /// <summary>What is true of AppAny's schema before it is moved.</summary>
    private protected virtual Task BeforeTheMoveAsync(DbConnection connection, string connectionString, string tablePrefix) => Task.CompletedTask;

    /// <summary>The leading <c>TABLE|COLUMN</c> or <c>TABLE(</c> of each row a snapshot reads.</summary>
    private static IEnumerable<string> Keys(IEnumerable<string> rows) => rows.Select(r =>
    {
        int paren = r.IndexOf('(', StringComparison.Ordinal);
        string[] cells = r.Split('|');
        return paren >= 0 && (cells.Length < 2 || paren < r.IndexOf('|', StringComparison.Ordinal)) ? r[..paren] : $"{cells[0]}|{cells[1]}";
    }).Distinct();
}

[Category("db-postgres")]
public sealed class PostgresEntityFrameworkCoreTest : EntityFrameworkCoreAppAnyServerTest
{
    private protected override string Dialect => "postgres";

    private protected override async Task<IQuartzTestDatabase> CreateDatabaseAsync() => new PostgresQuartzTestDatabase(await PostgresWeaselDatabase.CreateAsync());

    private protected override Action<QuartzModelBuilder> UseAppAny(string prefix) => q => q.UsePostgreSql(prefix.ToLowerInvariant(), schema: null);
}

[Category("db-sqlserver")]
public sealed class SqlServerEntityFrameworkCoreTest : EntityFrameworkCoreAppAnyServerTest
{
    private protected override string Dialect => "sqlServer";

    private protected override async Task<IQuartzTestDatabase> CreateDatabaseAsync() => new SqlServerQuartzTestDatabase(await SqlServerWeaselDatabase.CreateAsync());

    private protected override Action<QuartzModelBuilder> UseAppAny(string prefix) => q => q.UseSqlServer(prefix, schema: null);

    private protected override string[] LeftColumns =>
        ["CRON_TRIGGERS|TIME_ZONE_ID", "SIMPLE_TRIGGERS|REPEAT_COUNT", "SIMPLE_TRIGGERS|TIMES_TRIGGERED", "TRIGGERS|MISFIRE_INSTR"];

    /// <summary>
    /// AppAny declares <c>STR_PROP_1</c> to <c>STR_PROP_3</c> <c>NOT NULL</c> on SQL Server, and Quartz writes
    /// <c>NULL</c> to <c>STR_PROP_2</c> and <c>STR_PROP_3</c> for a calendar-interval trigger and for a recurrence
    /// trigger, so scheduling either fails until the move repairs the columns. The workload's calendar-interval
    /// trigger is the first of the two it stores.
    /// </summary>
    private protected override async Task BeforeTheMoveAsync(DbConnection connection, string connectionString, string tablePrefix)
    {
        Func<Task> run = () => MigratedSchemaWorkload.RunAsync(connection, Dialect, connectionString, tablePrefix);

        (await run.Should().ThrowAsync<Exception>()).Which.ToString().Should().MatchRegex(
            @"Cannot insert the value NULL into column 'STR_PROP_2', table '[^']*SIMPROP_TRIGGERS'",
            "the insert of a trigger that leaves a string property empty fails on the NOT NULL column");
    }
}

[Category("db-mysql")]
public sealed class MySqlEntityFrameworkCoreTest : EntityFrameworkCoreAppAnyServerTest
{
    private protected override string Dialect => "mysql_innodb";

    private protected override async Task<IQuartzTestDatabase> CreateDatabaseAsync() => new MySqlQuartzTestDatabase(await MySqlWeaselDatabase.CreateAsync());

    private protected override Action<QuartzModelBuilder> UseAppAny(string prefix) => q => q.UseMySql(prefix);

    private protected override string[] LeftColumns =>
        ["FIRED_TRIGGERS|IS_NONCONCURRENT", "JOB_DETAILS|JOB_DATA", "TRIGGERS|JOB_DATA", "BLOB_TRIGGERS|BLOB_DATA", "CALENDARS|CALENDAR"];

    private protected override string[] LeftForeignKeys => ["TRIGGERS", "SIMPLE_TRIGGERS", "SIMPROP_TRIGGERS", "CRON_TRIGGERS", "BLOB_TRIGGERS"];

    /// <summary>
    /// Oracle's MySQL provider creates <c>IDX_QRTZ_T_NFT_ST</c> without its descending <c>PRIORITY</c>, which the
    /// snapshot does not read; <c>CompleteQuartzSchema</c> creates it again with it.
    /// </summary>
    private protected override async Task AssertAcquisitionIndexIsDescendingAsync(IQuartzTestDatabase database, string tablePrefix)
    {
        object? collation = await database.ScalarAsync(
            "SELECT COLLATION FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() "
            + $"AND INDEX_NAME = 'IDX_{tablePrefix}T_NFT_ST' AND COLUMN_NAME = 'PRIORITY'");

        collation.Should().Be("D", "acquisition orders by NEXT_FIRE_TIME ASC, PRIORITY DESC, and the index has to say the same");
    }
}

[Category("db-oracle")]
public sealed class OracleEntityFrameworkCoreTest : EntityFrameworkCoreServerTest
{
    private protected override string Dialect => "oracle";

    private protected override async Task<IQuartzTestDatabase> CreateDatabaseAsync() => new OracleQuartzTestDatabase(await OracleWeaselDatabase.CreateAsync());
}

/// <summary>
/// Firebird, on whichever server the leg runs: 4 by default, 3, 4 or 5 on the image legs, which run this namespace.
/// The test prefixes are six characters, the longest Firebird 3's 31 bytes leave room for, except where a test
/// raises the limit.
/// </summary>
[Category("db-firebird")]
public sealed class FirebirdEntityFrameworkCoreTest : EntityFrameworkCoreServerTest
{
    private protected override string Dialect => "firebird";

    private protected override async Task<IQuartzTestDatabase> CreateDatabaseAsync() => new FirebirdQuartzTestDatabase(await FirebirdWeaselDatabase.CreateAsync("UTF8"));

    /// <summary>
    /// A database only Firebird 4 or later opens takes 63-character names, which <c>MaxIdentifierLength = 63</c>
    /// says. Two prefixes, one byte past Firebird 3's six and the longest 63 leaves room for, migrate to the fresh
    /// install's schema and run the scheduler. Firebird 3 refuses the first name past its 31 bytes and keeps nothing
    /// of the migration.
    /// </summary>
    [TestCase("QRTZ63_")]
    [TestCase("QUARTZ_FOR_FIREBIRD_4_AND_5_REPORTING_")]
    public async Task APrefixPastFirebird3sLimitMigratesUnderTheRaisedLimit(string prefix)
    {
        Action<QuartzTablesOptions> options = t =>
        {
            t.TablePrefix = prefix;
            t.MaxIdentifierLength = 63;
        };

        FirebirdWeaselDatabase firebird = await FirebirdWeaselDatabase.CreateAsync("UTF8");
        await using FirebirdQuartzTestDatabase database = new(firebird);
        await database.ProvisionAsync("QRTZ_");

        await using (EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(Dialect, options, database.ConnectionString))
        {
            if (await firebird.ServerMajorVersionAsync() == 3)
            {
                Func<Task> migrate = () => context.MigrateAsync(options);

                await migrate.Should().ThrowAsync<FbException>().WithMessage("*Name longer than database column size*",
                    "Firebird 3 refuses a name past its 31 bytes, which is why the limit is raised only for a database it never opens");
                (await database.ScalarAsync($"SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME STARTING WITH '{prefix}'"))
                    .Should().Be(0L, "the migration's transaction took back the tables it had created");
                return;
            }

            await context.MigrateAsync(options);
        }

        await using DbConnection connection = await database.OpenAsync();

        (await SchemaSnapshot.ReadAsync(connection, Dialect, prefix)).Should().BeEquivalentTo(
            await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZ_"));
        (await database.ScalarAsync($"SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IDX_{prefix}FT_INST_JOB_REQ_RCVRY'"))
            .Should().Be(1L, "the longest name is in the catalog whole");

        await MigratedSchemaWorkload.RunAsync(connection, Dialect, database.ConnectionString, prefix);
    }
}

/// <summary>A database of a test's own, as the tests here need it, over the Weasel tests' helpers.</summary>
internal interface IQuartzTestDatabase : IAsyncDisposable
{
    string ConnectionString { get; }

    Task ProvisionAsync(string tablePrefix);

    /// <summary>A released version's <c>tables_&lt;dialect&gt;.sql</c>, from <c>SchemaBaselines</c>, under a prefix.</summary>
    Task RunBaselineAsync(string version, string tablePrefix);

    Task<DbConnection> OpenAsync();

    Task<object?> ScalarAsync(string sql);
}

internal abstract class QuartzTestDatabase : IQuartzTestDatabase
{
    public abstract string ConnectionString { get; }

    public abstract Task ProvisionAsync(string tablePrefix);

    public abstract Task RunBaselineAsync(string version, string tablePrefix);

    public abstract Task<DbConnection> OpenAsync();

    public abstract Task<object?> ScalarAsync(string sql);

    public abstract ValueTask DisposeAsync();

    /// <summary>A baseline script with its <c>QRTZ_</c> names under another prefix, in either case.</summary>
    protected static async Task<string> BaselineAsync(string version, string dialect, string tablePrefix)
    {
        string relative = Path.Combine("src", "Quartz.Tests.Integration", "SchemaBaselines", version, $"tables_{dialect}.sql");

        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, relative);
            if (File.Exists(candidate))
            {
                return (await File.ReadAllTextAsync(candidate))
                    .Replace("QRTZ_", tablePrefix.ToUpperInvariant(), StringComparison.Ordinal)
                    .Replace("qrtz_", tablePrefix.ToLowerInvariant(), StringComparison.Ordinal);
            }
        }

        throw new FileNotFoundException($"{relative} is not under any directory above {AppContext.BaseDirectory}");
    }
}

internal sealed class PostgresQuartzTestDatabase(PostgresWeaselDatabase database) : QuartzTestDatabase
{
    public override string ConnectionString => database.ConnectionString;

    public override Task ProvisionAsync(string tablePrefix) => database.ProvisionAsync(tablePrefix);

    public override async Task RunBaselineAsync(string version, string tablePrefix) =>
        await database.ExecuteAsync(await BaselineAsync(version, "postgres", tablePrefix));

    public override async Task<DbConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public override Task<object?> ScalarAsync(string sql) => database.ScalarAsync(sql);

    public override ValueTask DisposeAsync() => database.DisposeAsync();
}

internal sealed class SqlServerQuartzTestDatabase(SqlServerWeaselDatabase database) : QuartzTestDatabase
{
    public override string ConnectionString => database.ConnectionString;

    public override Task ProvisionAsync(string tablePrefix) => database.ProvisionAsync(tablePrefix);

    public override async Task RunBaselineAsync(string version, string tablePrefix) =>
        await database.ExecuteAsync((await BaselineAsync(version, "sqlServer", tablePrefix))
            .Replace("[enter_db_name_here]", $"[{database.Name}]", StringComparison.Ordinal));

    public override async Task<DbConnection> OpenAsync()
    {
        SqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public override Task<object?> ScalarAsync(string sql) => database.ScalarAsync(sql);

    public override ValueTask DisposeAsync() => database.DisposeAsync();
}

internal sealed class OracleQuartzTestDatabase(OracleWeaselDatabase database) : QuartzTestDatabase
{
    public override string ConnectionString => database.ConnectionString;

    public override Task ProvisionAsync(string tablePrefix) => database.ProvisionAsync(tablePrefix);

    /// <summary>Through the container's own <c>sqlplus</c>, as this user, which is what understands the script.</summary>
    public override async Task RunBaselineAsync(string version, string tablePrefix) =>
        await database.RunScriptAsync(await BaselineAsync(version, "oracle", tablePrefix));

    public override async Task<DbConnection> OpenAsync()
    {
        OracleConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public override Task<object?> ScalarAsync(string sql) => database.ScalarAsync(sql);

    public override ValueTask DisposeAsync() => database.DisposeAsync();
}

internal sealed class FirebirdQuartzTestDatabase(FirebirdWeaselDatabase database) : QuartzTestDatabase
{
    public override string ConnectionString => database.ConnectionString;

    public override Task ProvisionAsync(string tablePrefix) => database.ProvisionAsync(tablePrefix);

    /// <summary>
    /// Through the server's own isql, which is what understands the script, without the bare <c>DROP</c> lines that
    /// fail on an empty database.
    /// </summary>
    public override async Task RunBaselineAsync(string version, string tablePrefix) =>
        await database.RunIsqlAsync(string.Join('\n', (await BaselineAsync(version, "firebird", tablePrefix)).Split('\n')
            .Where(line => !line.TrimStart().StartsWith("DROP ", StringComparison.OrdinalIgnoreCase))));

    public override async Task<DbConnection> OpenAsync()
    {
        FbConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public override Task<object?> ScalarAsync(string sql) => database.ScalarAsync(sql);

    public override ValueTask DisposeAsync() => database.DisposeAsync();
}

internal sealed class MySqlQuartzTestDatabase(MySqlWeaselDatabase database) : QuartzTestDatabase
{
    public override string ConnectionString => database.ConnectionString;

    public override Task ProvisionAsync(string tablePrefix) => database.ProvisionAsync(tablePrefix);

    /// <summary>Through the container's own <c>mysql</c> client, which is what understands the script.</summary>
    public override async Task RunBaselineAsync(string version, string tablePrefix) =>
        await TestcontainersDatabaseEnvironment.ExecuteScriptAsync(
            "mysql_innodb", $"USE `{database.Name}`;\n{await BaselineAsync(version, "mysql_innodb", tablePrefix)}");

    public override async Task<DbConnection> OpenAsync()
    {
        MySqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public override Task<object?> ScalarAsync(string sql) => database.ScalarAsync(sql);

    public override ValueTask DisposeAsync() => database.DisposeAsync();
}
