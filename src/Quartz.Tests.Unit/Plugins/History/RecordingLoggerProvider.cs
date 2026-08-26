using Microsoft.Extensions.Logging;

namespace Quartz.Tests.Unit.Plugin.History;

internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public List<LogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(RecordingLoggerProvider provider) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            provider.Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }
}

internal sealed class LogEntry
{
    public LogLevel Level { get; }
    public EventId EventId { get; }
    public string Message { get; }
    public Exception Exception { get; }

    public LogEntry(LogLevel level, EventId eventId, string message, Exception exception)
    {
        Level = level;
        EventId = eventId;
        Message = message;
        Exception = exception;
    }
}
