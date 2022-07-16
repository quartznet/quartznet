using Quartz.Listener;

namespace Quartz.Examples.AspNetCore
{
    public class SecondSampleJobListener : JobListenerSupport
    {
        private readonly ILogger<SecondSampleJobListener> logger;

        public SecondSampleJobListener(ILogger<SecondSampleJobListener> logger)
        {
            this.logger = logger;
        }

        public override string Name => "Second Sample Job Listener";

        public override Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Job {JobName} executed", context.JobDetail.Key);
            return Task.CompletedTask;
        }
    }
}