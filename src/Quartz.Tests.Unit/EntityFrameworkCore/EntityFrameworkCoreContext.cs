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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

using Quartz.EntityFrameworkCore;

namespace Quartz.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// A context whose model is whatever the test builds, under one of the providers Quartz has a model for.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here opens a connection unless the test runs SQL: the model, the differ and the SQL generator are
/// the provider's own services, which is what <c>dotnet ef migrations add</c> and <c>Migrate()</c> use, and a
/// connection string is only something to parse.
/// </para>
/// <para>
/// Each instance builds its own model. EF Core caches a model per context type, which would hand every test
/// the first one built; <see cref="PerInstanceModelCacheKeyFactory" /> keys the cache on the instance instead.
/// </para>
/// </remarks>
internal sealed class EntityFrameworkCoreContext : DbContext
{
    private readonly Action<ModelBuilder, DatabaseFacade> build;

    private EntityFrameworkCoreContext(DbContextOptions<EntityFrameworkCoreContext> options, Action<ModelBuilder, DatabaseFacade> build)
        : base(options)
    {
        this.build = build;
    }

    /// <summary>The dialects with a model, as the schema model keys them.</summary>
    public static readonly string[] Dialects = ["postgres", "sqlServer", "mysql_innodb", "sqlite", "oracle", "firebird"];

    /// <summary>The dialects AppAny.Quartz.EntityFrameworkCore.Migrations has a model of, to move from.</summary>
    public static readonly string[] AppAnyDialects = ["postgres", "sqlServer", "mysql_innodb", "sqlite"];

    /// <summary>The key this instance's model is cached under, which no other instance shares.</summary>
    public Guid ModelKey { get; } = Guid.NewGuid();

    /// <summary>The design-time model, which is what the differ and the scaffolder read.</summary>
    public IModel DesignModel => this.GetService<IDesignTimeModel>().Model;

    /// <summary>A context with Quartz's tables and nothing else, under the dialect's provider.</summary>
    public static EntityFrameworkCoreContext Quartz(string dialect, Action<QuartzTablesOptions>? configure = null, string? connectionString = null) =>
        For(dialect, (model, database) => model.AddQuartzTables(database.ProviderName, configure), connectionString);

    /// <summary>A context with the tables as a release left them, for the differences between releases.</summary>
    public static EntityFrameworkCoreContext QuartzAsOf(string dialect, string version, Action<QuartzTablesOptions>? configure = null, string? connectionString = null) =>
        For(dialect, (model, database) =>
        {
            QuartzDialect quartz = QuartzDialect.ForProvider(database.ProviderName);
            QuartzModel.Add(model, quartz, QuartzTableNaming.Create(quartz, configure), Version.Parse(version));
        }, connectionString);

    public static EntityFrameworkCoreContext For(string dialect, Action<ModelBuilder, DatabaseFacade> build, string? connectionString = null)
    {
        DbContextOptionsBuilder<EntityFrameworkCoreContext> options = new();

        _ = dialect switch
        {
            "postgres" => options.UseNpgsql(connectionString ?? "Host=localhost;Database=quartz"),
            "sqlServer" => options.UseSqlServer(connectionString ?? "Server=localhost;Database=quartz"),
            "mysql_innodb" => options.UseMySQL(connectionString ?? "server=localhost;database=quartz"),
            "sqlite" => options.UseSqlite(connectionString ?? "Data Source=:memory:"),
            "oracle" => options.UseOracle(connectionString ?? "User Id=quartz;Password=quartz;Data Source=localhost:1521/XEPDB1"),
            "firebird" => options.UseFirebird(connectionString ?? "database=localhost:quartz.fdb;user=sysdba;password=masterkey"),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Quartz.EntityFrameworkCore has no model for this dialect"),
        };

        options.ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>();
        return new EntityFrameworkCoreContext(options.Options, build);
    }

    /// <summary>What it takes to bring a database with <paramref name="source" />'s model to this one's.</summary>
    public IReadOnlyList<MigrationOperation> DifferencesFrom(EntityFrameworkCoreContext? source) =>
        this.GetService<IMigrationsModelDiffer>().GetDifferences(source?.DesignModel.GetRelationalModel(), DesignModel.GetRelationalModel());

    /// <summary>The statements this provider writes for the operations, against this context's model.</summary>
    public IReadOnlyList<MigrationCommand> Commands(IReadOnlyList<MigrationOperation> operations) =>
        this.GetService<IMigrationsSqlGenerator>().Generate(operations, DesignModel, MigrationsSqlGenerationOptions.Default);

    /// <summary>The statements, as text.</summary>
    public List<string> Sql(IReadOnlyList<MigrationOperation> operations) =>
        [.. Commands(operations).Select(c => c.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal).Trim())];

    /// <summary>A migration's operations, built the way a migration's <c>Up</c> builds them.</summary>
    public IReadOnlyList<MigrationOperation> Migration(Action<MigrationBuilder> up)
    {
        MigrationBuilder migrationBuilder = new(Database.ProviderName);
        up(migrationBuilder);
        return migrationBuilder.Operations;
    }

    /// <summary>
    /// Runs the operations against the context's database, in the transaction a migration runs in.
    /// </summary>
    public Task ExecuteAsync(IReadOnlyList<MigrationOperation> operations) =>
        this.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(Commands(operations), this.GetService<IRelationalConnection>());

    /// <summary>
    /// What an initial migration with <c>CompleteQuartzSchema</c> at its end does to an empty database: the
    /// operations <c>dotnet ef migrations add</c> scaffolds from the model, then the call.
    /// </summary>
    public Task MigrateAsync(Action<QuartzTablesOptions>? configure = null) =>
        ExecuteAsync([.. DifferencesFrom(null), .. Migration(m => m.CompleteQuartzSchema(configure))]);

    protected override void OnModelCreating(ModelBuilder modelBuilder) => build(modelBuilder, Database);

    private sealed class PerInstanceModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((EntityFrameworkCoreContext) context).ModelKey, designTime);
    }
}

/// <summary>An operation as one line, the same however it was made: by the differ, or by a migration.</summary>
internal static class MigrationOperationText
{
    public static string Describe(MigrationOperation operation) => operation switch
    {
        AddColumnOperation x => $"AddColumn {Qualified(x.Schema, x.Table)}.{x.Name} {Column(x)}",
        AlterColumnOperation x => $"AlterColumn {Qualified(x.Schema, x.Table)}.{x.Name} {Column(x.OldColumn)} -> {Column(x)}",
        CreateTableOperation x => $"CreateTable {Qualified(x.Schema, x.Name)}",
        CreateIndexOperation x => $"CreateIndex {x.Name} on {Qualified(x.Schema, x.Table)} ({IndexColumns(x)})",
        DropIndexOperation x => $"DropIndex {x.Name} on {Qualified(x.Schema, x.Table)}",
        AddForeignKeyOperation x => $"AddForeignKey {x.Name} on {Qualified(x.Schema, x.Table)} ({string.Join(", ", x.Columns)}) "
                                    + $"-> {Qualified(x.PrincipalSchema, x.PrincipalTable)} ({string.Join(", ", x.PrincipalColumns ?? [])}) on delete {x.OnDelete}",
        DropForeignKeyOperation x => $"DropForeignKey {x.Name} on {Qualified(x.Schema, x.Table)}",
        AddPrimaryKeyOperation x => $"AddPrimaryKey {x.Name} on {Qualified(x.Schema, x.Table)} ({string.Join(", ", x.Columns)})",
        DropPrimaryKeyOperation x => $"DropPrimaryKey {x.Name} on {Qualified(x.Schema, x.Table)}",
        RenameTableOperation x => $"RenameTable {Qualified(x.Schema, x.Name)} -> {Qualified(x.NewSchema, x.NewName)}",
        EnsureSchemaOperation x => $"EnsureSchema {x.Name}",
        SqlOperation x => "Sql " + x.Sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal),
        _ => operation.GetType().Name,
    };

    private static string Qualified(string? schema, string? name) => schema is null ? name ?? "" : $"{schema}.{name}";

    private static string Column(ColumnOperation column) =>
        column.ColumnType + (column.IsNullable ? " NULL" : " NOT NULL")
        + (column.DefaultValue is { } value ? $" DEFAULT {value}" : "")
        + (column.DefaultValueSql is { } sql ? $" DEFAULT ({sql})" : "");

    private static string IndexColumns(CreateIndexOperation index) => string.Join(", ", index.Columns.Select((column, i) =>
        index.IsDescending is { } descending && (descending.Length == 0 || descending[i]) ? column + " DESC" : column));
}
