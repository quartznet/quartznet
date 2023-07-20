using Quartz.Listener;

namespace Quartz.Examples.AspNetCore;

public class SampleJobListener : JobListenerSupport
{
    private readonly ILogger<SampleJobListener> logger;

    public SampleJobListener(ILogger<SampleJobListener> logger)
    {
        this.logger = logger;
    }

    public override string Name => "Sample Job Listener";

    public override ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("The job is about to be executed, prepare yourself!");
        return default;
    }
}