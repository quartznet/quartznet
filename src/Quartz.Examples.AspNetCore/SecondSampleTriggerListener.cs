using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

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

        public override Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Trigger {TriggerKey} fired (example value '{ExampleValue}')", trigger.Key, exampleValue);
            return Task.CompletedTask;
        }
    }
}