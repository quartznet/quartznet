using System.Collections.Specialized;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Integration.Impl
{
    [TestFixture]
    public class RemoteSchedulerSmokeTest
    {
        [Test]
        [Explicit("Needs server from example 13 running to work")]
        public async Task Test()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.scheduler.instanceName"] = "RemoteClient";

            // set thread pool info
            properties["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "5";
            properties["quartz.threadPool.threadPriority"] = "Normal";

            // set remoting exporter
            properties["quartz.scheduler.proxy"] = "true";
            properties["quartz.scheduler.proxy.address"] = "tcp://127.0.0.1:555/QuartzScheduler";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            SmokeTestPerformer performer = new SmokeTestPerformer();
            await performer.Test(sched, true, true);
        }
    }
}