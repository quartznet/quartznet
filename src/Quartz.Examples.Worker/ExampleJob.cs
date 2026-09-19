namespace Quartz.Examples.Worker;

// The job says when it runs, where the job is written. The generator turns these two attributes into
// the AddJob<ExampleJob> and AddTrigger<ExampleJob> calls that Program.cs used to spell by hand, and
// q.AddDeclaredJobs() is what runs them.
[QuartzJob(Name = "ExampleJob", Description = "my awesome declared job")]
[CronTrigger("0/10 * * * * ?", Description = "my awesome declared trigger")]
public class ExampleJob : IJob, IDisposable
{
    private readonly ILogger<ExampleJob> logger;

    public ExampleJob(ILogger<ExampleJob> logger)
    {
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("{Job} job executing, triggered by {Trigger}", context.JobDetail.Key, context.Trigger.Key);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        logger.LogInformation("Example job disposing");
    }
}