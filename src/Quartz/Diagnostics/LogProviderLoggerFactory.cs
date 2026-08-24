using Microsoft.Extensions.Logging;

namespace Quartz.Diagnostics;

/// <summary>
/// An <see cref="ILoggerFactory" /> that hands out whatever <see cref="LogProvider" /> is pointing at.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler's parts are injected their loggers by the container they are built from. This stands
/// in for that container's factory where there is nothing behind it: a
/// <see cref="QuartzSchedulerBuilder" /> creates a container of its own, and a standalone application
/// configures its logging by calling <see cref="LogProvider.SetLogProvider" /> rather than by
/// registering providers into a container it never sees. Without the bridge every injected logger in
/// such an application would write to nothing.
/// </para>
/// <para>
/// A category resolves through <see cref="LogProvider" /> on each call rather than being captured, so
/// a factory set after this one was registered is still the one a logger created afterwards reaches.
/// </para>
/// </remarks>
internal sealed class LogProviderLoggerFactory : ILoggerFactory
{
    /// <summary>
    /// The one instance. It holds no state of its own — everything it answers with comes from
    /// <see cref="LogProvider" /> — so there is nothing for a second one to hold differently.
    /// </summary>
    internal static readonly LogProviderLoggerFactory Instance = new();

    private LogProviderLoggerFactory()
    {
    }

    public ILogger CreateLogger(string categoryName) => LogProvider.CreateLogger(categoryName);

    /// <remarks>
    /// Ignored rather than refused. The providers belong to whichever factory
    /// <see cref="LogProvider.SetLogProvider" /> was handed, which is the application's to configure;
    /// this object only forwards to it, and has nowhere to put a provider of its own.
    /// </remarks>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <remarks>
    /// Nothing to dispose, and deliberately nothing forwarded: the ambient factory outlives every
    /// container that borrows it, which is the whole reason <see cref="LogProvider" /> is not seeded
    /// from one.
    /// </remarks>
    public void Dispose()
    {
    }
}
