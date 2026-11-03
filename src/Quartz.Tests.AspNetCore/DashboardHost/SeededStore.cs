using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.DashboardHost;

/// <summary>
/// A SQLite database another process has scheduled into: one scheduler, one durable job and its trigger,
/// which a store-attached target shows as a window.
/// </summary>
internal sealed class SeededStore : IDisposable
{
    public const string SchedulerName = "reporting";

    public static readonly JobKey Job = new("nightly", "batch");

    private readonly SqliteTestDatabase database;

    private SeededStore(SqliteTestDatabase database)
    {
        this.database = database;
    }

    public string ConnectionString => database.ConnectionString;

    public static async Task<SeededStore> Create()
    {
        SqliteTestDatabase database = new("dashboard-host");

        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "node-a";
            });

            quartz.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        // Creating the scheduler provisions the schema; scheduling puts its name where a window is found.
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity("at-midnight", "nightly").ForJob(Job).StartAt(DateTimeOffset.UtcNow.AddYears(1)).Build());
        await scheduler.Shutdown();

        return new SeededStore(database);
    }

    public void Dispose()
    {
        database.Dispose();
    }
}
