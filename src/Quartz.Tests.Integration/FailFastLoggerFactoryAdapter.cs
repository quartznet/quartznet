using System;
using System.Collections.Generic;

using Quartz.Logging;
using Quartz.Logging.LogProviders;

namespace Quartz.Tests.Integration
{
    internal class FailFastLoggerFactoryAdapter : LogProviderBase
    {
        private static readonly List<string> errors = new List<string>();

        public override Logger GetLogger(string name)
        {
            return Log;
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

        public static List<string> Errors
        {
            get { return errors; }
        }
    }
}