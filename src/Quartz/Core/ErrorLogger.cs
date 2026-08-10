using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;
using Quartz.Listeners;

namespace Quartz.Core;

/// <summary>
/// ErrorLogger - Scheduler Listener Class
/// </summary>
internal sealed class ErrorLogger : ISchedulerListener
{
    private readonly ILogger<ErrorLogger> logger = LogProvider.CreateLogger<ErrorLogger>();

    public ValueTask SchedulerError(
        string message,
        SchedulerException exception,
        CancellationToken cancellationToken = default)
    {
#pragma warning disable CA2254
        logger.LogError(exception, message);
#pragma warning restore CA2254
        return default;
    }
}