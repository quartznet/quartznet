using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quartz.Diagnostics;

public static class LogProvider
{
    private static ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Sets the current log provider based on logger factory.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    public static void SetLogProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public static ILogger CreateLogger(string category) => _loggerFactory != null ? _loggerFactory.CreateLogger(category) : NullLogger.Instance;
    public static ILogger<T> CreateLogger<T>() => _loggerFactory != null ? _loggerFactory.CreateLogger<T>() : NullLogger<T>.Instance;
}