using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quartz.Diagnostics;

/// <summary>
/// The logger factory Quartz logs to from the places that cannot be handed one.
/// </summary>
/// <remarks>
/// <para>
/// This is ambient, mutable, process-wide state, and it stays that way on purpose. Nearly everything the
/// scheduler is made of is built by a container and is injected an <see cref="ILogger" /> the ordinary
/// way. What is left over cannot be: static helpers such as <see cref="Quartz.TimeZones" />,
/// types a caller constructs directly — triggers, calendars, plugins, the jobs in <c>Quartz.Jobs</c> —
/// and anything that runs while the container is still being built. A type cannot be handed a logger by
/// a container that does not exist yet, so those sites read this instead of going unlogged.
/// </para>
/// <para>
/// It is deliberately <em>not</em> seeded from the container either, which would otherwise be the obvious
/// convenience. This slot outlives any one container: a process that builds a host, disposes it and
/// builds another — every integration test suite, and every application that reloads configuration —
/// would be left holding a disposed <see cref="ILoggerFactory" />, and the next
/// <see cref="CreateLogger{T}" /> would throw <see cref="ObjectDisposedException" /> from somewhere
/// unrelated to logging. Whoever sets this has to own the lifetime of what they set, which is a decision
/// only the application can make.
/// </para>
/// <para>
/// A logger is usually resolved once, when the type that logs is constructed, so setting the factory
/// after a scheduler has been running affects only what is created from then on.
/// </para>
/// </remarks>
public static class LogProvider
{
    private static ILoggerFactory? loggerFactory;

    /// <summary>
    /// Sets the logger factory Quartz logs to where no logger can be injected. Until this is called,
    /// those sites log to <see cref="NullLogger" />.
    /// </summary>
    /// <remarks>
    /// Pass a factory that lives at least as long as the schedulers in the process. Handing over a
    /// factory owned by a host and then disposing that host leaves this pointing at a disposed object.
    /// </remarks>
    /// <param name="loggerFactory">The logger factory.</param>
    public static void SetLogProvider(ILoggerFactory loggerFactory)
    {
        LogProvider.loggerFactory = loggerFactory;
    }

    public static ILogger CreateLogger(string category) => loggerFactory != null ? loggerFactory.CreateLogger(category) : NullLogger.Instance;
    public static ILogger<T> CreateLogger<T>() => loggerFactory != null ? loggerFactory.CreateLogger<T>() : NullLogger<T>.Instance;
}
