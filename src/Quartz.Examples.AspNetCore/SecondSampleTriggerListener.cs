using Quartz.Listeners;

namespace Quartz.Examples.AspNetCore;

public class SecondSampleTriggerListener : ITriggerListener
{
    private readonly ILogger<SecondSampleTriggerListener> logger;
    private readonly string exampleValue;

    public SecondSampleTriggerListener(ILogger<SecondSampleTriggerListener> logger, string exampleValue)
    {
        this.logger = logger;
        this.exampleValue = exampleValue;
    }

    public string Name => "Second Sample Trigger Listener";

    public ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Trigger {TriggerKey} fired (example value '{ExampleValue}')", trigger.Key, exampleValue);
        return default;
    }
}