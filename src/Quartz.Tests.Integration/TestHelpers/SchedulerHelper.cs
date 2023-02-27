using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;
using Quartz.Tests.Integration.Utils;

namespace Quartz.Tests.Integration.TestHelpers;

public class SchedulerHelper
{
    public const string TablePrefix = "QRTZ_";

    public static Task<IScheduler> CreateScheduler(string provider, string name)
    {
        DatabaseHelper.RegisterDatabaseSettingsForProvider(provider, out var driverDelegateType);

        var serializer = new JsonObjectSerializer();
        serializer.Initialize();
        var jobStore = new JobStoreTX
        {
            DataSource = "default",
            TablePrefix = TablePrefix,
            InstanceId = "AUTO",
            DriverDelegateType = driverDelegateType,
            ObjectSerializer = serializer
        };

        DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new DefaultThreadPool(), jobStore);
        return SchedulerRepository.Instance.Lookup(name + "Scheduler");
    }
}