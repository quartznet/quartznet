using System;

using Microsoft.Extensions.Logging;

using Quartz.Logging;

namespace Quartz
{
    internal class LoggingProvider : ILogProvider
    {
        private readonly ILoggerFactory loggerFactory;

        public LoggingProvider(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
        }

        public Logger GetLogger(string name)
        {
            return (level, func, exception, parameters) =>
            {
                if (func != null)
                {
                    var message = func();
                    switch (level)
                    {
                        case Quartz.Logging.LogLevel.Info:
                        {
                            loggerFactory.CreateLogger(name).LogInformation(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Debug:
                        {
                            loggerFactory.CreateLogger(name).LogDebug(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Error:
                        case Quartz.Logging.LogLevel.Fatal:
                        {
                            loggerFactory.CreateLogger(name).LogError(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Trace:
                        {
                            loggerFactory.CreateLogger(name).LogTrace(exception, message, parameters);
                            break;
                        }
                        case Quartz.Logging.LogLevel.Warn:
                        {
                            loggerFactory.CreateLogger(name).LogWarning(exception, message, parameters);
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