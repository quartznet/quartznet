using System;

using Microsoft.Extensions.Logging;

using Quartz.Logging;

namespace Quartz.Simpl
{
    internal class MicrosoftLoggingProvider : ILogProvider
    {
        private readonly ILoggerFactory loggerFactory;

        public MicrosoftLoggingProvider(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
        }

        public Logger GetLogger(string name)
        {
            var logger = loggerFactory.CreateLogger(name);
            return (level, func, exception, parameters) =>
            {
                if (func != null)
                {
                    var message = func();
                    switch (level)
                    {
                        case Quartz.Logging.LogLevel.Info:
                        {
                            logger.LogInformation(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Debug:
                        {
                            logger.LogDebug(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Error:
                        case Quartz.Logging.LogLevel.Fatal:
                        {
                            logger.LogError(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Trace:
                        {
                            logger.LogTrace(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Warn:
                        {
                            logger.LogWarning(exception, message, parameters);
                            break;
                        }
                    }
                }

                return true;
            };
        }

        public IDisposable? OpenNestedContext(string message)
        {
            return null;
        }

        public IDisposable? OpenMappedContext(string key, object value, bool destructure = false)
        {
            return null;
        }
    }
}