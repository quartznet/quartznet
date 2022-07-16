using Microsoft.Extensions.Logging;

using Quartz.Listener;
using Quartz.Logging;

namespace Quartz.Core
{
    /// <summary>
    /// ErrorLogger - Scheduler Listener Class
    /// </summary>
    internal sealed class ErrorLogger : SchedulerListenerSupport
    {
        private readonly ILogger<ErrorLogger> logger = LogProvider.CreateLogger<ErrorLogger>();

        public override Task SchedulerError(
            string msg,
            SchedulerException cause,
            CancellationToken cancellationToken = default)
        {
            logger.LogError(cause,msg);
            return Task.CompletedTask;
        }
    }
}