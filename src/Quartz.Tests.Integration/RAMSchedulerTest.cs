using System.Threading.Tasks;

using NUnit.Framework;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class RAMSchedulerTest : AbstractSchedulerTest
    {
        protected override Task<IScheduler> CreateScheduler(string name, int threadPoolSize)
        {
            var builder = SchedulerBuilder.Create()
                .WithName(name + "Scheduler")
                .WithId("AUTO")
                .WithDefaultThreadPool(x => x.WithThreadCount(threadPoolSize));

            return builder.Build();
        }
    }
}