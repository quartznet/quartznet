using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;
using Quartz.Tests.Integration.Utils;

namespace Quartz.Tests.Integration.TestHelpers;

public class SchedulerHelper
{
    public const string TablePrefix = "QRTZ_";

    public static async ValueTask<IScheduler> CreateScheduler(string provider, string name)
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out var driverDelegateType, out string dataSourceName);

        var serializer = new NewtonsoftJsonObjectSerializer();
        serializer.Initialize();
        var jobStore = new JobStoreTX
        {
            DataSource = dataSourceName,
            TablePrefix = TablePrefix,
            InstanceId = "AUTO",
            DriverDelegateType = driverDelegateType,
            ObjectSerializer = serializer
        };

        string schedulerName = GetSchedulerName(provider, name);
        await DirectSchedulerFactory.Instance.CreateScheduler(schedulerName, "AUTO", new DefaultThreadPool(), jobStore);
        return SchedulerRepository.Instance.Lookup(schedulerName);
    }

    public static string GetSchedulerName(string provider, string name)
    {
        string providerSuffix = DatabaseHelper.GetDataSourceName(provider).Replace('-', '_');
        return $"{name}_{providerSuffix}_Scheduler";
    }
}
