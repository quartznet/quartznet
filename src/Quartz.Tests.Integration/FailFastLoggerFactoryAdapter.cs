using System;
using System.Collections.Generic;

using Quartz.Logging;

namespace Quartz.Tests.Integration
{
    internal class FailFastLoggerFactoryAdapter : ILogProvider
    {
        private static readonly List<string> errors = new List<string>();

        public ILog GetLogger(string name)
        {
            return new FailFastLogger(this);
        }

        public IDisposable OpenNestedContext(string message)
        {
            throw new NotImplementedException();
        }

        public IDisposable OpenMappedContext(string key, string value)
        {
            throw new NotImplementedException();
        }

        private void ReportError(string error)
        {
            errors.Add(error);
        }

        public static List<string> Errors
        {
            get { return errors; }
        }

        private class FailFastLogger : ILog
        {
            private readonly FailFastLoggerFactoryAdapter parent;

            public FailFastLogger(FailFastLoggerFactoryAdapter parent)
            {
                this.parent = parent;
            }

            public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception = null, params object[] formatParameters)
            {
                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal)
                {
                    parent.ReportError(messageFunc());
                }

                return true;
            }
        }
    }
}