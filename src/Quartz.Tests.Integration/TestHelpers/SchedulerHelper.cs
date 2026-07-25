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
        string schedulerName = GetSchedulerName(provider, name);

        return QuartzSchedulerBuilder.Create()
            .Configure(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = schedulerName;
                    options.GenerateInstanceId = true;
                });
                q.UseDefaultThreadPool();
                q.UsePersistentStore(store =>
                {
                    UseDatabase(store, provider);
                    store.UseNewtonsoftJsonSerializer();
                    store.Configure(options => options.TablePrefix = TablePrefix);
                });
            })
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
