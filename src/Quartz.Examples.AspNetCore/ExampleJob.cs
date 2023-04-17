namespace Quartz.Examples.AspNetCore
{
    public class ExampleJob : IJob, IDisposable
    {
        private readonly ILogger<ExampleJob> logger;

        public ExampleJob(ILogger<ExampleJob> logger)
        {
            this.logger = logger;
        }

        public async ValueTask Execute(IJobExecutionContext context)
        {
            logger.LogInformation(context.JobDetail.Key + " job executing, triggered by " + context.Trigger.Key);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        public void Dispose()
        {
            logger.LogInformation("Example job disposing");
        }
    }
}