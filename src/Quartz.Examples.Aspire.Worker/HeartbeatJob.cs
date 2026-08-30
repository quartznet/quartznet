namespace Quartz.Examples.Aspire.Worker;

/// <summary>
/// Something for the scheduler to do, so the dashboard has spans, measurements and log lines to show.
/// </summary>
public sealed class HeartbeatJob : IJob
{
    private readonly ILogger<HeartbeatJob> logger;

    public HeartbeatJob(ILogger<HeartbeatJob> logger)
    {
        this.logger = logger;
    }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Heartbeat fired at {FireTime}, next at {NextFireTime}",
            context.FireTimeUtc,
            context.NextFireTimeUtc);

        return default;
    }
}
