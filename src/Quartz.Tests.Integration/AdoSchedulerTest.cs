using System;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration
{
#if BINARY_SERIALIZATION
    [TestFixture(typeof(BinaryObjectSerializer))]
#endif
    [TestFixture(typeof(JsonObjectSerializer))]
    public class AdoSchedulerTest : AbstractSchedulerTest
    {
        private readonly IObjectSerializer serializer;

        public AdoSchedulerTest(Type serializerType)
        {
            serializer = (IObjectSerializer) Activator.CreateInstance(serializerType);
            serializer.Initialize();
        }

        protected override Task<IScheduler> CreateScheduler(string name, int threadPoolSize)
        {
            DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider(TestConstants.DefaultSqlServerProvider, "Server=(local);Database=quartz;Trusted_Connection=True;"));

            var jobStore = new JobStoreTX
            {
                DataSource = "default",
                TablePrefix = "QRTZ_",
                InstanceId = "AUTO",
                DriverDelegateType = typeof(SqlServerDelegate).AssemblyQualifiedName,
                ObjectSerializer = serializer
            };

            DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new DefaultThreadPool(), jobStore);
            return SchedulerRepository.Instance.Lookup(name + "Scheduler");
        }
    }
}