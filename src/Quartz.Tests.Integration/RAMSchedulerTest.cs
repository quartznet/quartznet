namespace Quartz.Tests.Integration;

[NonParallelizable]
public class RAMSchedulerTest : AbstractSchedulerTest
{
    public RAMSchedulerTest() : base("memory", "default-serializer")
    {
    }

    protected override ValueTask<IScheduler> CreateScheduler(string name, int threadPoolSize)
    {
        var config = QuartzSchedulerBuilder.Create().ConfigureScheduler(o => { o.InstanceId = "AUTO"; o.InstanceName = name + "Scheduler"; });

        config.UseDefaultThreadPool(x =>
        {
            x.MaxConcurrency = threadPoolSize;
        });

        return config.BuildScheduler();
    }

    public RAMSchedulerTest(string provider) : base(provider, "default-serializer")
    {
    }
}