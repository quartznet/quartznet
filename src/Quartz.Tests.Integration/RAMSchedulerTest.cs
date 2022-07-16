using NUnit.Framework;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class RAMSchedulerTest : AbstractSchedulerTest
    {
        protected override Task<IScheduler> CreateScheduler(string name, int threadPoolSize)
        {
            var config = SchedulerBuilder.Create("AUTO", name + "Scheduler");

            config.UseDefaultThreadPool(x =>
            {
                x.MaxConcurrency = threadPoolSize;
            });

            return config.BuildScheduler();
        }
    }
}