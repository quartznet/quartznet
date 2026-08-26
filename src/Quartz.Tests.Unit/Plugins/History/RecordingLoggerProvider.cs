using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Quartz.Tests.Unit.Plugin.History;

/// <summary>
/// Records everything logged through it, so a test can assert on the text a sink would render, the
/// level and the event id.
/// </summary>
/// <remarks>
/// The recording is a concurrent queue rather than a list because not every caller logs from one
/// thread — <c>NativeJob</c> relays a spawned process's two output streams from a thread each — and a
/// test helper that corrupts itself under the thing it is watching is worse than no helper.
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> entries = new();

    /// <summary>
    /// What has been logged so far, as a snapshot taken when the property is read.
    /// </summary>
    public List<LogEntry> Entries => entries.ToList();

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
            provider.entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
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
