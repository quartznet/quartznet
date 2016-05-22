using System.Threading.Tasks;

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
        protected override Task<IScheduler> CreateScheduler(string name, int threadPoolSize)
        {
            DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider(TestConstants.DefaultSqlServerProvider, "Server=(local);Database=quartz;Trusted_Connection=True;"));

            var jobStore = new JobStoreTX
                               {
                                   DataSource = "default",
                                   TablePrefix = "QRTZ_",
                                   InstanceId = "AUTO",
                                   DriverDelegateType = typeof(SqlServerDelegate).AssemblyQualifiedName,
#if BINARY_SERIALIZATION
                ObjectSerializer = new BinaryObjectSerializer()
#else
                ObjectSerializer = new JsonObjectSerializer()
#endif
            };
           
            DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new DefaultThreadPool(), jobStore);
            return SchedulerRepository.Instance.Lookup(name + "Scheduler");
        }
    }
}