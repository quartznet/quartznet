using Quartz.Listeners;

namespace Quartz.Examples.AspNetCore;

public class SecondSampleJobListener : IJobListener
{
    private readonly ILogger<SecondSampleJobListener> logger;

    public SecondSampleJobListener(ILogger<SecondSampleJobListener> logger)
    {
        this.logger = logger;
    }

    public string Name => "Second Sample Job Listener";

    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Job {JobName} executed", context.JobDetail.Key);
        return default;
    }
}