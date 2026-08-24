using Microsoft.Extensions.Logging;

namespace Quartz.Core;

/// <summary>
/// ErrorLogger - Scheduler Listener Class
/// </summary>
internal sealed class ErrorLogger : ISchedulerListener
{
    private readonly ILogger<ErrorLogger> logger;

    /// <remarks>
    /// The logger comes from the scheduler's resources, so an error reaches the application's logging
    /// whether or not it has anything to do with <see cref="Quartz.Diagnostics.LogProvider" />. This is
    /// the listener of last resort — a scheduler with no listeners of its own still has this one — so
    /// it is the last place that should depend on a static having been set.
    /// </remarks>
    internal ErrorLogger(ILogger<ErrorLogger> logger)
    {
        this.logger = logger;
    }

    public ValueTask SchedulerError(
        IScheduler scheduler,
        SchedulerErrorContext error,
        CancellationToken cancellationToken = default)
    {
        // Named placeholders rather than an interpolated string, so a host running several schedulers
        // can filter its log by SchedulerName or by the keys instead of reading prose. Two templates
        // because most errors carry no keys, and a line ending in three empty fields reads as though
        // the scheduler had lost them rather than never having had them.
        if (error.TriggerKey is null && error.JobKey is null && error.FireInstanceId is null)
        {
            logger.LogError(
                error.Exception,
                "{Message} (scheduler: {SchedulerName})",
                error.Message,
                scheduler.SchedulerName);
        }
        else
        {
            logger.LogError(
                error.Exception,
                "{Message} (scheduler: {SchedulerName}, trigger: {TriggerKey}, job: {JobKey}, fire instance: {FireInstanceId})",
                error.Message,
                scheduler.SchedulerName,
                error.TriggerKey,
                error.JobKey,
                error.FireInstanceId);
        }

        return default;
    }
}
