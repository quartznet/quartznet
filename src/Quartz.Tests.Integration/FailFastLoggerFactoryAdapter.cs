using System;
using System.Collections.Generic;

using Quartz.Logging;

namespace Quartz.Tests.Integration
{
    internal class FailFastLoggerFactoryAdapter : ILogProvider
    {
        private static readonly List<string> errors = new List<string>();
        private static readonly IDisposable NoopDisposableInstance = new DisposableAction();

        public Logger GetLogger(string name)
        {
            return Log;
        }

        public IDisposable OpenNestedContext(string message)
        {
            return NoopDisposableInstance;
        }

        public IDisposable OpenMappedContext(string key, string value)
        {
            return NoopDisposableInstance;
        }

        private static bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception, object[] formatparameters)
        {
            if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal)
            {
                var message = messageFunc == null ? string.Empty : messageFunc();
                errors.Add(message);
            }

            return true;
        }

        public static List<string> Errors => errors;

        private class DisposableAction : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}