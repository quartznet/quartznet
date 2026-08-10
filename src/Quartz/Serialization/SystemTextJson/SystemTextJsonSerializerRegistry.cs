using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Serialization.SystemTextJson.Calendars;
using Quartz.Serialization.SystemTextJson.Triggers;
using Quartz.Util;

namespace Quartz.Serialization.SystemTextJson;

/// <summary>
/// The trigger and calendar serializers a <see cref="Quartz.Impl.SystemTextJsonObjectSerializer"/>
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
/// </remarks>
public sealed class SystemTextJsonSerializerRegistry
{
    private readonly SerializerMap<ITriggerSerializer> triggerSerializers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SerializerMap<ICalendarSerializer> calendarSerializers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a registry holding the serializers for the built-in trigger and calendar types.
    /// </summary>
    public SystemTextJsonSerializerRegistry()
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
    public SystemTextJsonSerializerRegistry AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        ArgumentNullException.ThrowIfNull(serializer);

        // Found by its JSON discriminator, and also by its type name.
        triggerSerializers.Add(serializer, serializer.TriggerTypeName, typeof(TTrigger).AssemblyQualifiedNameWithoutVersion());
        return this;
    }

    /// <summary>
    /// Adds a serializer for a custom calendar type.
    /// </summary>
    public SystemTextJsonSerializerRegistry AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer) where TCalendar : ICalendar
    {
        ArgumentNullException.ThrowIfNull(serializer);

        calendarSerializers.Add(serializer, typeof(TCalendar).AssemblyQualifiedNameWithoutVersion(), serializer.CalendarTypeName);
        return this;
    }

    internal ITriggerSerializer GetTriggerSerializer(string? typeName)
    {
        return triggerSerializers.Get(typeName, "Don't know how to handle");
    }

    internal ICalendarSerializer GetCalendarSerializer(string? typeName)
    {
        return calendarSerializers.Get(typeName, "Don't know how to handle");
    }
}
