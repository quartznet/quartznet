using System.Text.Json;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.Dashboard.Services;

/// <summary>
/// The <see cref="JsonSerializerOptions"/> the dashboard reads and writes Quartz types with, built once.
/// </summary>
/// <remarks>
/// <para>
/// Only the HTTP-backed client needs these: it is the one that has a wire between it and a scheduler.
/// The in-process client passes triggers, calendars and job data maps straight through.
/// </para>
/// <para>
/// A singleton because the options are derived purely from the container's
/// <see cref="SystemTextJsonSerializerRegistry"/>, which is itself a singleton. Building them per request
/// would allocate a fresh set of converters for every Blazor circuit and, since System.Text.Json caches
/// type metadata per options instance, re-run converter and metadata resolution for <c>ITrigger</c>,
/// <c>ICalendar</c> and <c>JobDataMap</c> on the first serialize in each scope.
/// </para>
/// </remarks>
internal sealed class DashboardSerializerOptions
{
    public DashboardSerializerOptions(SystemTextJsonSerializerRegistry serializerRegistry)
    {
        ArgumentNullException.ThrowIfNull(serializerRegistry);

        JsonSerializerOptions deserializer = new(JsonSerializerDefaults.Web);
        deserializer.AddQuartzConverters(serializerRegistry, newtonsoftCompatibilityMode: false);
        Deserializer = deserializer;
    }

    /// <summary>
    /// Options carrying the Quartz converters, used for both reading and writing scheduler types.
    /// </summary>
    public JsonSerializerOptions Deserializer { get; }
}
