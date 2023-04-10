namespace Quartz.Examples.AspNetCore
{
    public class ExampleJob : IJob, IDisposable
    {
        private readonly ILogger<ExampleJob> logger = null!;

        public string? Parameter { get; set; }

        public ExampleJob(ILogger<ExampleJob> logger)
        {
            this.logger = logger;
        }

        public ExampleJob()
        {
        }

        public async Task Execute(IJobExecutionContext context)
        {
            logger.LogInformation(context.JobDetail.Key + " job executing, triggered by " + context.Trigger.Key + ". Parameter was " + Parameter);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        public void Dispose()
        {
            logger.LogInformation("Example job disposing");
        }
    }
}