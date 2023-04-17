using Quartz.Listener;

namespace Quartz.Examples.AspNetCore
{
    public class SecondSampleTriggerListener : TriggerListenerSupport
    {
        private readonly ILogger<SecondSampleTriggerListener> logger;
        private readonly string exampleValue;

        public SecondSampleTriggerListener(ILogger<SecondSampleTriggerListener> logger, string exampleValue)
        {
            this.logger = logger;
            this.exampleValue = exampleValue;
        }

        public override string Name => "Second Sample Trigger Listener";

        public override ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Trigger {TriggerKey} fired (example value '{ExampleValue}')", trigger.Key, exampleValue);
            return default;
        }
    }
}