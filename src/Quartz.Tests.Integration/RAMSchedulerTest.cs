using System.Threading.Tasks;

using NUnit.Framework;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class RAMSchedulerTest : AbstractSchedulerTest
    {
        protected override Task<IScheduler> CreateScheduler(string name, int threadPoolSize)
        {
            var config = SchedulerBuilder.Create()
                .SetSchedulerName(name + "Scheduler")
                .SetSchedulerId("AUTO");
            
            config.UseDefaultThreadPool(x => x.SetThreadCount(threadPoolSize));

            return config.BuildScheduler();
        }
    }
}