namespace Quartz.Examples.Worker;

public class ExampleJob : IJob, IDisposable
{
    private readonly ILogger<ExampleJob> logger;

    public ExampleJob(ILogger<ExampleJob> logger)
    {
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context)
    {
        logger.LogInformation("{Job} job executing, triggered by {Trigger}", context.JobDetail.Key, context.Trigger.Key);
        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        logger.LogInformation("Example job disposing");
    }
}