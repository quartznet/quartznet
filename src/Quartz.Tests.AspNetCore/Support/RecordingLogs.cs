using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Quartz.Tests.AspNetCore.Support;

/// <summary>
/// Everything logged through a host, as a provider a test adds to it: the category, the level, the event
/// id, the rendered message and the structured properties behind it.
/// </summary>
/// <remarks>
/// The properties as well as the text, because a template's placeholders are the part an operator's
/// pipeline matches on — asserting only on the rendered string would pass for an event that records
/// <c>{User}</c> nowhere. A concurrent queue, because a host logs from whichever thread served the
/// request.
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLogEntry> entries = new();

    /// <summary>
    /// What has been logged so far, as a snapshot taken when the property is read.
    /// </summary>
    public List<RecordedLogEntry> Entries => entries.ToList();

    /// <summary>
    /// The entries carrying <paramref name="eventId" />, which is how a test names the event it is about.
    /// </summary>
    public List<RecordedLogEntry> WithEventId(int eventId) => entries.Where(x => x.EventId.Id == eventId).ToList();

    public void Clear()
    {
        entries.Clear();
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(RecordingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, string?> properties = [];
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                foreach (KeyValuePair<string, object?> value in values)
                {
                    properties[value.Key] = value.Value?.ToString();
                }
            }

            provider.entries.Enqueue(new RecordedLogEntry(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                properties));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record RecordedLogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, string?> Properties);
