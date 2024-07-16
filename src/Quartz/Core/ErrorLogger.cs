using Microsoft.Extensions.Logging;

using Quartz.Listener;
using Quartz.Diagnostics;

namespace Quartz.Core;

/// <summary>
/// ErrorLogger - Scheduler Listener Class
/// </summary>
internal sealed class ErrorLogger : SchedulerListenerSupport
{
    private readonly ILogger<ErrorLogger> logger = LogProvider.CreateLogger<ErrorLogger>();

    public override ValueTask SchedulerError(
        string msg,
        SchedulerException cause,
        CancellationToken cancellationToken = default)
    {
#pragma warning disable CA2254
        logger.LogError(cause, msg);
#pragma warning restore CA2254
        return default;
    }
}