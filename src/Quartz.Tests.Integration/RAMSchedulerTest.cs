namespace Quartz.Tests.Integration;

[NonParallelizable]
public class RAMSchedulerTest : AbstractSchedulerTest
{
    public RAMSchedulerTest() : base("memory", "default-serializer")
    {
    }

    protected override ValueTask<IScheduler> CreateScheduler(string name, int threadPoolSize)
    {
        return QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(o => { o.InstanceId = "AUTO"; o.InstanceName = name + "Scheduler"; })
                .UseDefaultThreadPool(x =>
                {
                    x.MaxConcurrency = threadPoolSize;
                }))
            .BuildScheduler();
    }

    public RAMSchedulerTest(string provider) : base(provider, "default-serializer")
    {
    }
}