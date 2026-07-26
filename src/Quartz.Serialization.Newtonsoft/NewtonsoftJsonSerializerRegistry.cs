using Quartz.Calendars;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Triggers;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// The trigger and calendar serializers a <see cref="Quartz.Simpl.NewtonsoftJsonObjectSerializer"/>
/// knows about.
/// </summary>
/// <remarks>
/// <para>
/// A new instance already knows every trigger and calendar type Quartz ships with, so registering a
/// custom one adds to that set rather than replacing it.
/// </para>
/// <para>
/// These registrations used to live in process-global dictionaries inside the converters, which meant
/// two schedulers in one process could not serialize different custom types — whichever registered last
/// won, silently. Owning them per instance is what makes the choice belong to the scheduler that made it.
/// Registrations are expected to be complete before the owning serializer is initialized; the maps are
/// not synchronized for concurrent writes, exactly as the statics they replace were not.
/// </para>
/// <para>
/// The lookup rules deliberately match what the Newtonsoft converters have always done, which is not
/// quite what the System.Text.Json ones do: a calendar is found by its assembly-qualified type name
/// only, case-sensitively, because <see cref="ICalendarSerializer"/> here carries no calendar type name
/// of its own.
/// </para>
/// </remarks>
public sealed class NewtonsoftJsonSerializerRegistry
{
    private readonly Dictionary<string, ITriggerSerializer> triggerSerializers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICalendarSerializer> calendarSerializers = new();

    /// <summary>
    /// Creates a registry holding the serializers for the built-in trigger and calendar types.
    /// </summary>
    public NewtonsoftJsonSerializerRegistry()
    {
        AddTriggerSerializer<CalendarIntervalTriggerImpl>(new CalendarIntervalTriggerSerializer());
        AddTriggerSerializer<CronTriggerImpl>(new CronTriggerSerializer());
        AddTriggerSerializer<DailyTimeIntervalTriggerImpl>(new DailyTimeIntervalTriggerSerializer());
        AddTriggerSerializer<SimpleTriggerImpl>(new SimpleTriggerSerializer());
        AddTriggerSerializer<RecurrenceTriggerImpl>(new RecurrenceTriggerSerializer());

        AddCalendarSerializer<BaseCalendar>(new BaseCalendarSerializer());
        AddCalendarSerializer<AnnualCalendar>(new AnnualCalendarSerializer());
        AddCalendarSerializer<CronCalendar>(new CronCalendarSerializer());
        AddCalendarSerializer<DailyCalendar>(new DailyCalendarSerializer());
        AddCalendarSerializer<HolidayCalendar>(new HolidayCalendarSerializer());
        AddCalendarSerializer<MonthlyCalendar>(new MonthlyCalendarSerializer());
        AddCalendarSerializer<WeeklyCalendar>(new WeeklyCalendarSerializer());
    }

    /// <summary>
    /// Adds a serializer for a custom trigger type.
    /// </summary>
    public NewtonsoftJsonSerializerRegistry AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        ArgumentNullException.ThrowIfNull(serializer);

        triggerSerializers[serializer.TriggerTypeForJson] = serializer;

        // Support also type name
        triggerSerializers[typeof(TTrigger).AssemblyQualifiedNameWithoutVersion()] = serializer;
        return this;
    }

    /// <summary>
    /// Adds a serializer for a custom calendar type.
    /// </summary>
    public NewtonsoftJsonSerializerRegistry AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        calendarSerializers[typeof(TCalendar).AssemblyQualifiedNameWithoutVersion()] = serializer;
        return this;
    }

    internal ITriggerSerializer GetTriggerSerializer(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || !triggerSerializers.TryGetValue(typeName!, out var converter))
        {
            throw new ArgumentException($"Don't know how to handle {typeName}", nameof(typeName));
        }

        return converter;
    }

    internal ICalendarSerializer GetCalendarSerializer(string typeName)
    {
        if (!calendarSerializers.TryGetValue(typeName, out var converter))
        {
            throw new ArgumentException($"don't know how to handle {typeName}", nameof(typeName));
        }

        return converter;
    }
}
