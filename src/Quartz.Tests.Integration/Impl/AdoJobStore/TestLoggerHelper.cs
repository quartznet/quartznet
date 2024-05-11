using MELT;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

public class TestLoggerHelper
{
    ITestLoggerFactory loggerFactory;

    public void SetTestLoggerProvider()
    {
        loggerFactory = TestLoggerFactory.Create();
        LogProvider.SetLogProvider(loggerFactory);
    }

    public ITestLoggerFactory LoggerFactory => loggerFactory;

    public IEnumerable<LogEntry> LogEntries => loggerFactory.Sink.LogEntries;

    public void ClearLogs()
    {
        loggerFactory.GetTestLoggerSink().Clear();
    }
}