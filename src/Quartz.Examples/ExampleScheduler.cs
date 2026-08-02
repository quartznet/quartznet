namespace Quartz.Examples;

/// <summary>
/// Builds the scheduler the examples share.
/// </summary>
/// <remarks>
/// The examples have no host, so they use <see cref="QuartzSchedulerBuilder"/>, which creates a
/// container of its own and configures it with the same API an application would use under a host.
/// </remarks>
internal static class ExampleScheduler
{
    public static ValueTask<IScheduler> Create(string instanceName = "ExampleDefaultQuartzScheduler")
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        builder.ConfigureScheduler(options => options.InstanceName = instanceName)
            .UseDefaultThreadPool(maxConcurrency: 10)
            .UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(60));

        return builder.BuildScheduler();
    }
}
