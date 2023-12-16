using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quartz.Logging;

public static class LogProvider
{
    private static ILoggerFactory? _loggerFactory = null;

    /// <summary>
    /// Sets the current log provider based on logger factory.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    public static void SetLogProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public static ILogger CreateLogger(string category) => _loggerFactory is not null ? _loggerFactory.CreateLogger(category) : NullLogger.Instance;
    public static ILogger<T> CreateLogger<T>() => _loggerFactory is not null ? _loggerFactory.CreateLogger<T>() : NullLogger<T>.Instance;

    internal static class Cached
    {
        internal static readonly Lazy<System.Diagnostics.DiagnosticListener> Default =
            new(() => new System.Diagnostics.DiagnosticListener(DiagnosticHeaders.DefaultListenerName));
    }
}