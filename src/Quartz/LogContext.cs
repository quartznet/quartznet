using Microsoft.Extensions.Logging;

using Quartz.Logging;
using Quartz.Simpl;

namespace Quartz
{
    public static class LogContext
    {
        /// <summary>
        /// Sets the current log provider based on logger factory.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        public static void SetCurrentLogProvider(ILoggerFactory loggerFactory)
        {
            LogProvider.SetCurrentLogProvider(new MicrosoftLoggingProvider(loggerFactory));
        }
    }
}