using System.Collections.Specialized;
using System.Globalization;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class RAMSchedulerTest : AbstractSchedulerTest
    {
        protected override Task<IScheduler> CreateScheduler(string name, int threadPoolSize)
        {
            NameValueCollection config = new NameValueCollection();
            config["quartz.scheduler.instanceName"] = name + "Scheduler";
            config["quartz.scheduler.instanceId"] = "AUTO";
            config["quartz.threadPool.threadCount"] = threadPoolSize.ToString(CultureInfo.InvariantCulture);
            config["quartz.threadPool.type"] = typeof (DefaultThreadPool).AssemblyQualifiedName;
            config["quartz.serializer.type"] = "binary";
            return new StdSchedulerFactory(config).GetScheduler();
        }
    }
}