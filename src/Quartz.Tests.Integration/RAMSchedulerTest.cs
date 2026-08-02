namespace Quartz.Tests.Integration;

[NonParallelizable]
public class RAMSchedulerTest : AbstractSchedulerTest
{
    public RAMSchedulerTest() : base("memory", "default-serializer")
    {
    }

    protected override ValueTask<IScheduler> CreateScheduler(string name, int threadPoolSize)
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        builder.ConfigureScheduler(o => { o.InstanceId = "AUTO"; o.InstanceName = name + "Scheduler"; })
            .UseDefaultThreadPool(x =>
            {
                x.MaxConcurrency = threadPoolSize;
            });

        return builder.BuildScheduler();
    }

    public RAMSchedulerTest(string provider) : base(provider, "default-serializer")
    {
    }
}