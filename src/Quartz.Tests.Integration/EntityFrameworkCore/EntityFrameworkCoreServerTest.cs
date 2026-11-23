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

using Npgsql;

using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Tests.Integration.Weasel;
using Quartz.Tests.Unit.EntityFrameworkCore;

namespace Quartz.Tests.Integration.EntityFrameworkCore;

/// <summary>
/// Quartz.EntityFrameworkCore's schema in a real database of each server dialect it has a model for: an empty
/// database migrated, a database the scripts created and then baselined, and a schema AppAny 0.6.3 created,
/// moved. Each ends with a scheduler running on the tables.
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

    /// <summary>The prefix as Quartz's options take it; PostgreSQL folds it either way.</summary>
    private static Action<QuartzTablesOptions> Prefix(string prefix) => t => t.TablePrefix = prefix;

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

        (await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZB_")).Should().BeEquivalentTo(
            await SchemaSnapshot.ReadAsync(connection, Dialect, "QRTZ_"));

        await MigratedSchemaWorkload.RunAsync(connection, Dialect, database.ConnectionString, "QRTZB_");
    }

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

    private protected virtual Task AssertAcquisitionIndexIsDescendingAsync(IQuartzTestDatabase database, string tablePrefix) => Task.CompletedTask;

    /// <summary>The leading <c>TABLE|COLUMN</c> or <c>TABLE(</c> of each row a snapshot reads.</summary>
    private static IEnumerable<string> Keys(IEnumerable<string> rows) => rows.Select(r =>
    {
        int paren = r.IndexOf('(', StringComparison.Ordinal);
        string[] cells = r.Split('|');
        return paren >= 0 && (cells.Length < 2 || paren < r.IndexOf('|', StringComparison.Ordinal)) ? r[..paren] : $"{cells[0]}|{cells[1]}";
    }).Distinct();
}

[Category("db-postgres")]
public sealed class PostgresEntityFrameworkCoreTest : EntityFrameworkCoreServerTest
{
    private protected override string Dialect => "postgres";

    private protected override async Task<IQuartzTestDatabase> CreateDatabaseAsync() => new PostgresQuartzTestDatabase(await PostgresWeaselDatabase.CreateAsync());

    private protected override Action<QuartzModelBuilder> UseAppAny(string prefix) => q => q.UsePostgreSql(prefix.ToLowerInvariant(), schema: null);
}

[Category("db-sqlserver")]
public sealed class SqlServerEntityFrameworkCoreTest : EntityFrameworkCoreServerTest
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
public sealed class MySqlEntityFrameworkCoreTest : EntityFrameworkCoreServerTest
{
    private protected override string Dialect => "mysql_innodb";

    private protected override async Task<IQuartzTestDatabase> CreateDatabaseAsync() => new MySqlQuartzTestDatabase(await MySqlWeaselDatabase.CreateAsync());

    private protected override Action<QuartzModelBuilder> UseAppAny(string prefix) => q => q.UseMySql(prefix);

    private protected override string[] LeftColumns => ["FIRED_TRIGGERS|IS_NONCONCURRENT"];

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
