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
        return QuartzSchedulerBuilder.Create()
            .Configure(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = instanceName);
                q.UseDefaultThreadPool(maxConcurrency: 10);
                q.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(60));
            })
            .BuildScheduler();
    }
}
