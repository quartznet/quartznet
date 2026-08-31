#nullable enable

using Quartz.Tests.Integration.Utils;

namespace Quartz.Tests.Integration.TestHelpers;

public class SchedulerHelper
{
    public const string TablePrefix = "QRTZ_";

    /// <summary>
    /// Builds a database-backed scheduler through the container, so the store gets the real scheduler's
    /// signaler, name and instance id rather than stand-ins.
    /// </summary>
    public static ValueTask<IScheduler> CreateScheduler(string provider, string name)
    {
        return CreateScheduler(provider, options =>
        {
            options.InstanceName = GetSchedulerName(provider, name);
            options.GenerateInstanceId = true;
        });
    }

    /// <summary>
    /// Builds a database-backed scheduler with explicit scheduler options.
    /// </summary>
    public static ValueTask<IScheduler> CreateScheduler(
        string provider,
        Action<QuartzSchedulerOptions> configureScheduler,
        Action<AdoJobStoreOptions>? configureStore = null)
    {
        return QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(configureScheduler)
                .UseDefaultThreadPool()
                .UsePersistentStore(store =>
                {
                    UseDatabase(store, provider);
                    store.UseNewtonsoftJsonSerializer();
                    store.ConfigureStore(options =>
                    {
                        options.TablePrefix = TablePrefix;
                        configureStore?.Invoke(options);
                    });
                }))
            .BuildScheduler();
    }

    private static void UseDatabase(IPersistentStoreBuilder store, string provider)
    {
        switch (provider)
        {
            case TestConstants.DefaultSqlServerProvider:
                store.UseSqlServer(TestConstants.SqlServerConnectionString);
                break;
            case TestConstants.PostgresProvider:
                store.UsePostgres(TestConstants.PostgresConnectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), "Unknown database type " + provider);
        }
    }

    public static string GetSchedulerName(string provider, string name)
    {
        string providerSuffix = DatabaseHelper.GetDataSourceName(provider).Replace('-', '_');
        return $"{name}_{providerSuffix}_Scheduler";
    }
}
