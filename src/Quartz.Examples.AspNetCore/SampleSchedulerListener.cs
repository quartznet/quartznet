using Quartz.Listener;

namespace Quartz.Examples.AspNetCore;

public class SampleSchedulerListener : SchedulerListenerSupport
{
    private readonly ILogger<SampleSchedulerListener> logger;

    public SampleSchedulerListener(ILogger<SampleSchedulerListener> logger)
    {
        this.logger = logger;
    }

    public override ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Observed scheduler start");
        return default;
    }
}