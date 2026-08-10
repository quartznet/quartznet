using Quartz.Listeners;

namespace Quartz.Examples.AspNetCore;

public class SampleJobListener : IJobListener
{
    private readonly ILogger<SampleJobListener> logger;

    public SampleJobListener(ILogger<SampleJobListener> logger)
    {
        this.logger = logger;
    }

    public string Name => "Sample Job Listener";

    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("The job is about to be executed, prepare yourself!");
        return default;
    }
}