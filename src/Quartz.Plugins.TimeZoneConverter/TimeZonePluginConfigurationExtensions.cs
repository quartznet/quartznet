using Quartz.Plugins.TimeZoneConverter;

namespace Quartz;

/// <summary>
/// Adds TimeZoneConverter to Quartz's time zone lookup, so that a time zone id resolves whichever
/// operating system the process is running on.
/// </summary>
public static class TimeZonePluginConfigurationExtensions
{
    /// <summary>
    /// Resolves time zone identifiers using TimeZoneConverter, so Windows and IANA identifiers both
    /// work regardless of the host operating system.
    /// </summary>
    /// <remarks>
    /// The registration is process-wide and takes effect immediately, rather than when the scheduler is
    /// built: <see cref="TimeZones.FindById" /> is reached from places that have no scheduler in scope,
    /// and a <see cref="TriggerBuilder" /> call made while the application is still starting is one of
    /// them. Calling
    /// this for a second scheduler is a no-op, and nothing removes the resolver again, so a scheduler
    /// shutting down cannot take time zone resolution away from the ones still running.
    /// </remarks>
    public static IQuartzBuilder UseTimeZoneConverter(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        TimeZoneConverterResolver.Register();
        return builder;
    }
}
