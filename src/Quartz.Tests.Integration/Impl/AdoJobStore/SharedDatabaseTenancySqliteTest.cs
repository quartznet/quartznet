using Microsoft.Data.Sqlite;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The same two tenants on a SQLite file, which needs no container and so runs wherever the unit tests
/// do. "One database" is at its most literal here — one file, both schedulers — and SQLite forces
/// <c>AcquireTriggersWithinLock</c> on, so the two tenants also contend for the store's own lock rather
/// than merely for rows.
/// </summary>
[Category("db-sqlite")]
[NonParallelizable]
public sealed class SharedDatabaseTenancySqliteTest : SharedDatabaseTenancyTestBase
{
    private SqliteTestDatabase database;

    protected override void UseDatabase(IPersistentStoreBuilder store)
    {
        store.UseSqlite(ConnectionString);
    }

    protected override async Task PrepareDatabase()
    {
        database = new SqliteTestDatabase("tenancy-shared");

        await using SqliteConnection connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = new SqliteCommand(LoadTableScript(), connection);
        await command.ExecuteNonQueryAsync();
    }

    protected override Task CleanUpDatabase()
    {
        // The file is this fixture's alone, so it goes rather than its rows.
        database?.Dispose();

        return Task.CompletedTask;
    }

    private string ConnectionString => database.ConnectionString;

    private static string LoadTableScript()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "database", "tables", "tables_sqlite.sql");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate database/tables/tables_sqlite.sql from " + AppContext.BaseDirectory);
    }
}
