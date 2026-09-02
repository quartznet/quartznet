using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quartz.Diagnostics;

/// <summary>
/// The logger factory Quartz logs to from the places that cannot be handed one.
/// </summary>
/// <remarks>
/// <para>
/// This is ambient, mutable, process-wide state, and it stays that way on purpose. Everything the
/// scheduler is made of is built by a container and is injected an <see cref="ILogger" /> the ordinary
/// way — the scheduler and its loop, the job store and everything it owns, the thread pool, the job
/// factory, the type loader, the instance id generator — so a hosted application gets all of that
/// without touching this slot at all.
/// </para>
/// <para>
/// What is left over cannot be injected anything, and this is the whole of it: the broadcast listeners
/// and <see cref="Quartz.Listeners.JobChainingJobListener" />, which a caller constructs and hands over
/// already built; <see cref="Quartz.Impl.Triggers.CronTriggerImpl" />, which is a trigger and may have
/// been deserialized out of a job store; the static helpers <see cref="Quartz.TimeZones" />,
/// <c>MisfireInstructionNames</c>, <c>FileUtil</c> and <c>QuartzEnvironment</c>; and the
/// types in the satellite packages a caller constructs directly, such as the jobs in
/// <c>Quartz.Jobs</c>. A type cannot be handed a logger by a container it never meets, so those sites
/// read this instead of going unlogged.
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
    /// <para>
    /// An application on <c>AddQuartz</c> does not need this: the scheduler and everything it is built
    /// from log through the container's <see cref="ILoggerFactory" />. What this reaches is the list of
    /// leftovers above — a listener you constructed, a trigger, a static helper, a job from
    /// <c>Quartz.Jobs</c>.
    /// </para>
    /// <para>
    /// A standalone <see cref="QuartzSchedulerBuilder" /> is the exception, and calling this is how it
    /// is meant to be configured: the container it builds for itself has no logging providers of its
    /// own, so it forwards to this. Registering a provider on its <c>Services</c> instead takes that
    /// over.
    /// </para>
    /// <para>
    /// Pass a factory that lives at least as long as the schedulers in the process. Handing over a
    /// factory owned by a host and then disposing that host leaves this pointing at a disposed object.
    /// </para>
    /// </remarks>
    /// <param name="loggerFactory">The logger factory.</param>
    public static void SetLogProvider(ILoggerFactory loggerFactory)
    {
        LogProvider.loggerFactory = loggerFactory;
    }

    public static ILogger CreateLogger(string category) => loggerFactory != null ? loggerFactory.CreateLogger(category) : NullLogger.Instance;
    public static ILogger<T> CreateLogger<T>() => loggerFactory != null ? loggerFactory.CreateLogger<T>() : NullLogger<T>.Instance;
}
