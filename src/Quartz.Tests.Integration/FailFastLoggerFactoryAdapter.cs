using System;
using System.Collections.Generic;

using Common.Logging;
using Common.Logging.Factory;

namespace Quartz.Tests.Integration
{
    internal class FailFastLoggerFactoryAdapter : ILoggerFactoryAdapter
    {
        private static readonly List<string> errors = new List<string>();

        public ILog GetLogger(Type type)
        {
            return new FailFastLogger(this);
        }

        public ILog GetLogger(string name)
        {
            return new FailFastLogger(this);
        }

        private void ReportError(string error)
        {
            errors.Add(error);
        }

        public static List<string> Errors
        {
            get { return errors; }
        }

        private class FailFastLogger : AbstractLogger
        {
            private readonly FailFastLoggerFactoryAdapter parent;

            public FailFastLogger(FailFastLoggerFactoryAdapter parent)
            {
                this.parent = parent;
            }

            protected override void WriteInternal(LogLevel level, object message, Exception exception)
            {
                parent.ReportError("" + message);
            }

            public override bool IsTraceEnabled
            {
                get { return false; }
            }

            public override bool IsDebugEnabled
            {
                get { return false; }
            }

            public override bool IsErrorEnabled
            {
                get { return true; }
            }

            public override bool IsFatalEnabled
            {
                get { return true; }
            }

            public override bool IsInfoEnabled
            {
                get { return false; }
            }

            public override bool IsWarnEnabled
            {
                get { return true; }
            }
        }
    }
}