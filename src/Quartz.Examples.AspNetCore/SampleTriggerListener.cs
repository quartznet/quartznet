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

    public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Observed trigger fire by trigger {TriggerKey}", trigger.Key);
        return default;
    }
}