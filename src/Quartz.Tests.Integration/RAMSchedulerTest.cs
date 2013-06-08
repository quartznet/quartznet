using System.Collections.Specialized;
using System.Globalization;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class RAMSchedulerTest : AbstractSchedulerTest
    {
        protected override IScheduler CreateScheduler(string name, int threadPoolSize)
        {
            NameValueCollection config = new NameValueCollection();
            config["quartz.scheduler.instanceName"] = name + "Scheduler";
            config["quartz.scheduler.instanceId"] = "AUTO";
            config["quartz.threadPool.threadCount"] = threadPoolSize.ToString(CultureInfo.InvariantCulture);
            config["quartz.threadPool.type"] = typeof (SimpleThreadPool).AssemblyQualifiedName;
            return new StdSchedulerFactory(config).GetScheduler();
        }
    }
}