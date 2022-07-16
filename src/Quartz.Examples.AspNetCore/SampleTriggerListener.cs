using Quartz.Listener;

namespace Quartz.Examples.AspNetCore
{
    public class SampleTriggerListener : TriggerListenerSupport
    {
        private readonly ILogger<SampleTriggerListener> logger;

        public SampleTriggerListener(ILogger<SampleTriggerListener> logger)
        {
            this.logger = logger;
        }

        public override string Name => "Sample Trigger Listener";

        public override Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Observed trigger fire by trigger {TriggerKey}", trigger.Key);
            return Task.CompletedTask;
        }
    }
}