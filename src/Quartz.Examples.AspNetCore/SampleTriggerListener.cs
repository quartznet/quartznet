using Quartz.Listeners;

namespace Quartz.Examples.AspNetCore;

public class SampleTriggerListener : ITriggerListener
{
    private readonly ILogger<SampleTriggerListener> logger;

    public SampleTriggerListener(ILogger<SampleTriggerListener> logger)
    {
        this.logger = logger;
    }

    public string Name => "Sample Trigger Listener";

    public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Observed misfire of trigger {TriggerKey} on scheduler {SchedulerName}",
            trigger.Key,
            scheduler.SchedulerName);
        return default;
    }
}