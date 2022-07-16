using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Integration.TestHelpers;

public class SchedulerHelper
{
    public const string TablePrefix = "QRTZ_";

    public static Task<IScheduler> CreateScheduler(string name)
    {
        DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider(TestConstants.DefaultSqlServerProvider, TestConstants.SqlServerConnectionString));

        var serializer = new JsonObjectSerializer();
        serializer.Initialize();
        var jobStore = new JobStoreTX
        {
            DataSource = "default",
            TablePrefix = TablePrefix,
            InstanceId = "AUTO",
            DriverDelegateType = typeof(SqlServerDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer
        };

        DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new DefaultThreadPool(), jobStore);
        return SchedulerRepository.Instance.Lookup(name + "Scheduler");
    }
}