using Quartz.Listeners;

namespace Quartz.Examples.AspNetCore;

public class SampleSchedulerListener : ISchedulerListener
{
    private readonly ILogger<SampleSchedulerListener> logger;

    public SampleSchedulerListener(ILogger<SampleSchedulerListener> logger)
    {
        this.logger = logger;
    }

    public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Observed scheduler start");
        return default;
    }
}