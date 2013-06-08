using System.Threading;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class AdoSchedulerTest : AbstractSchedulerTest
    {
        protected override IScheduler CreateScheduler(string name, int threadPoolSize)
        {
            DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider("SqlServer-20", "Server=(local);Database=quartz;Trusted_Connection=True;"));

            var jobStore = new JobStoreTX
                               {
                                   DataSource = "default",
                                   TablePrefix = "QRTZ_",
                                   InstanceId = "AUTO",
                                   DriverDelegateType = typeof(SqlServerDelegate).AssemblyQualifiedName
                               };
           
            DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new SimpleThreadPool(threadPoolSize, ThreadPriority.Normal), jobStore);
            return SchedulerRepository.Instance.Lookup(name + "Scheduler");
        }
    }
}