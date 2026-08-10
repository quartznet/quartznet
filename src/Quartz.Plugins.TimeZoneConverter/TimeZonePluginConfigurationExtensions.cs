using Quartz.Plugins.TimeZoneConverter;

namespace Quartz;

/// <summary>
/// Adds the TimeZoneConverter plugin, which resolves time zone identifiers across operating systems.
/// </summary>
public static class TimeZonePluginConfigurationExtensions
{
    /// <summary>
    /// Resolves time zone identifiers using TimeZoneConverter, so Windows and IANA identifiers both
    /// work regardless of the host operating system.
    /// </summary>
    public static IQuartzBuilder UseTimeZoneConverter(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddPlugin<TimeZoneConverterPlugin>();
    }
}
