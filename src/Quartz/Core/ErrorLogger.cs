using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;

namespace Quartz.Core;

/// <summary>
/// ErrorLogger - Scheduler Listener Class
/// </summary>
internal sealed class ErrorLogger : ISchedulerListener
{
    private readonly ILogger<ErrorLogger> logger = LogProvider.CreateLogger<ErrorLogger>();

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
