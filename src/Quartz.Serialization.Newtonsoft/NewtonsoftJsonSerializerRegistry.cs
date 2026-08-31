using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Serialization.Newtonsoft.Calendars;
using Quartz.Serialization.Newtonsoft.Triggers;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// The trigger and calendar serializers a <see cref="Quartz.Impl.NewtonsoftJsonObjectSerializer"/>
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
/// Lookups mirror the System.Text.Json registry: case-insensitive, with a calendar indexed both under
/// its assembly-qualified type name — which is what payloads written by 3.x carry, so that key always
/// stays registered — and, when the serializer provides one, under its
/// <see cref="ICalendarSerializer.CalendarTypeName"/> discriminator.
/// </para>
/// <para>
/// A registry is also where an application declares a job data value type of its own, through
/// <see cref="AddJobDataValueType{T}" />. The serializer writes the value types a
/// <see cref="JobDataMap" /> has an accessor for and refuses the rest, so that a value nothing can read
/// back is refused while there is still someone to tell rather than stored and failed on later.
/// </para>
/// </remarks>
public sealed class NewtonsoftJsonSerializerRegistry
{
    private readonly SerializerMap<ITriggerSerializer> triggerSerializers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SerializerMap<ICalendarSerializer> calendarSerializers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Type> jobDataValueTypes = [];

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

        // Found by its JSON discriminator, and also by its type name.
        triggerSerializers.Add(serializer, serializer.TriggerTypeName, typeof(TTrigger).AssemblyQualifiedNameWithoutVersion());
        return this;
    }

    /// <summary>
    /// Adds a serializer for a custom calendar type.
    /// </summary>
    /// <remarks>
    /// The serializer is the typed <see cref="CalendarSerializer{TCalendar}" /> for this very calendar,
    /// so a mismatched pairing is a compile error rather than an <see cref="InvalidCastException" /> on
    /// the first calendar that round-trips. <typeparamref name="TCalendar" /> is inferred from the
    /// argument, so the type argument can be left off.
    /// </remarks>
    public NewtonsoftJsonSerializerRegistry AddCalendarSerializer<TCalendar>(CalendarSerializer<TCalendar> serializer) where TCalendar : ICalendar
    {
        ArgumentNullException.ThrowIfNull(serializer);

        // The assembly-qualified name is what 3.x-written payloads carry, so it is always registered;
        // the discriminator is a second key, never a replacement — see the remarks above.
        if (string.IsNullOrEmpty(serializer.CalendarTypeName))
        {
            calendarSerializers.Add(serializer, typeof(TCalendar).AssemblyQualifiedNameWithoutVersion());
        }
        else
        {
            calendarSerializers.Add(serializer, typeof(TCalendar).AssemblyQualifiedNameWithoutVersion(), serializer.CalendarTypeName);
        }

        return this;
    }

    /// <summary>
    /// Declares a job data value type of the application's own, which this serializer otherwise refuses
    /// to write.
    /// </summary>
    /// <typeparam name="T">
    /// The exact runtime type of the values to accept. A declaration does not extend to a derived type,
    /// because it is the runtime type of the stored value that is looked up — declare each type you
    /// actually put in a map.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// A <see cref="JobDataMap" /> accepts the types <see cref="DataMapExtensions" /> declares an
    /// accessor for, plus a <c>Dictionary&lt;string, string&gt;</c>; anything else is refused when the
    /// job or trigger is stored, rather than written as a blob that fails to load later. This is how an
    /// application says a type of its own is one Json.NET can read back — a class with a constructor
    /// Json.NET can call, and a versioning commitment for as long as the value sits in the database.
    /// </para>
    /// <para>
    /// It is this package's counterpart to
    /// <c>SystemTextJsonSerializerRegistry.AddTypeInfoResolver</c>, and the narrower of the two: Json.NET
    /// needs no metadata handed to it, so nothing is registered here but the permission. A blob holding a
    /// declared type is readable by this serializer only — the System.Text.Json reader hands any object
    /// back as a <c>Dictionary&lt;string, string&gt;</c> — so a value that has to survive a change of
    /// serializer belongs in a string the job writes itself.
    /// </para>
    /// </remarks>
    public NewtonsoftJsonSerializerRegistry AddJobDataValueType<T>()
    {
        jobDataValueTypes.Add(typeof(T));
        return this;
    }

    /// <summary>
    /// Whether the application has declared <paramref name="type" /> through
    /// <see cref="AddJobDataValueType{T}" />, which is what makes it a job data value this serializer
    /// will write.
    /// </summary>
    internal bool DeclaresJobDataValueType(Type type)
    {
        return jobDataValueTypes.Contains(type);
    }

    internal ITriggerSerializer GetTriggerSerializer(string? typeName)
    {
        return triggerSerializers.Get(typeName, "Don't know how to handle");
    }

    internal ICalendarSerializer GetCalendarSerializer(string typeName)
    {
        return calendarSerializers.Get(typeName, "don't know how to handle");
    }
}
