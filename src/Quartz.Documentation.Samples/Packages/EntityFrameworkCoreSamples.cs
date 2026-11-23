using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Packages;

#region sample_efcore_dbcontext

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // your own entities, then Quartz's tables: the dialect follows the context's provider
        modelBuilder.AddQuartzTables(Database.ProviderName);
    }
}

#endregion

public sealed class SchemaDbContext(DbContextOptions<SchemaDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        #region sample_efcore_schema

        modelBuilder.AddQuartzTables(Database.ProviderName, tables =>
        {
            tables.Schema = "quartz";      // PostgreSQL and SQL Server; unset: the model's default schema
            tables.TablePrefix = "QRTZ_";  // the default
        });

        #endregion
    }
}

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/entity-framework-core.md.
/// </summary>
public static class EntityFrameworkCoreSamples
{
    public static void Store(IServiceCollection services, string connectionString)
    {
        #region sample_efcore_schema_store

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);
            // the same tables: the schema, a dot, the prefix
            store.ConfigureStore(options => options.TablePrefix = "quartz.QRTZ_");
        }));

        #endregion
    }
}

#region sample_efcore_first_migration

public partial class AddQuartz : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ... the CreateTable and CreateIndex calls dotnet ef wrote ...

        // what the model cannot say: SQLite's delete triggers, MySQL's descending index column
        migrationBuilder.CompleteQuartzSchema();
    }
}

#endregion

public static class BaselineMigration
{
    #region sample_efcore_baseline

    public partial class AddQuartz : Migration
    {
        // the tables are there already, from database/tables or ProvisionSchema();
        // dotnet ef database update records this migration and runs nothing
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        // emptied too: the tables were not this migration's to drop
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }

    #endregion
}

#region sample_efcore_move

public partial class MoveToQuartzEntityFrameworkCore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // replaces everything dotnet ef wrote here; the options are AddQuartzTables's,
        // and Schema is for PostgreSQL and SQL Server only
        migrationBuilder.MoveQuartzTablesFromAppAny(tables => tables.Schema = "quartz");
    }

    // one-way: what dotnet ef wrote here would undo the repairs, and rebuild every table on SQLite
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

#endregion
