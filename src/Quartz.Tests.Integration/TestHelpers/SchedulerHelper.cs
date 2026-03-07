using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;
using Quartz.Tests.Integration.Utils;

namespace Quartz.Tests.Integration.TestHelpers;

public class SchedulerHelper
{
    public const string TablePrefix = "QRTZ_";

    public static string GetSchedulerName(string provider, string name)
    {
        var suffix = DatabaseHelper.GetDataSourceName(provider);
        return $"{name}Scheduler_{suffix}";
    }

    public static Task<IScheduler> CreateScheduler(string provider, string name)
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out var driverDelegateType, out var dataSourceName);

        var serializer = new JsonObjectSerializer();
        serializer.Initialize();
        var jobStore = new JobStoreTX
        {
            DataSource = dataSourceName,
            TablePrefix = TablePrefix,
            InstanceId = "AUTO",
            DriverDelegateType = driverDelegateType,
            ObjectSerializer = serializer
        };

        var schedulerName = GetSchedulerName(provider, name);
        DirectSchedulerFactory.Instance.CreateScheduler(schedulerName, "AUTO", new DefaultThreadPool(), jobStore);
        return Task.FromResult(SchedulerRepository.Instance.Lookup(schedulerName));
    }
}
