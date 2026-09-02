using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft.Calendars;

/// <summary>
/// Reads and writes one kind of calendar's own fields, beside the ones every calendar has.
/// </summary>
/// <remarks>
/// Register an implementation with <c>NewtonsoftJsonSerializerRegistry.AddCalendarSerializer</c>, or
/// derive from <see cref="CalendarSerializer{TCalendar}" /> to be handed the calendar already typed.
/// A calendar with no registered serializer cannot be stored: the first store write fails.
/// </remarks>
public interface ICalendarSerializer
{
    /// <summary>
    /// Builds the calendar instance the stored fields are then read into.
    /// </summary>
    /// <param name="source">The stored JSON for one calendar.</param>
    ICalendar Create(JObject source);

    /// <summary>
    /// Writes this kind of calendar's own fields. The description, time zone and chained base calendar
    /// are written around this call rather than by it.
    /// </summary>
    /// <param name="writer">The writer positioned inside the calendar's JSON object.</param>
    /// <param name="value">The calendar being stored.</param>
    void SerializeFields(JsonWriter writer, ICalendar value);

    /// <summary>
    /// Reads back what <see cref="SerializeFields" /> wrote, onto the instance
    /// <see cref="Create" /> returned.
    /// </summary>
    /// <param name="value">The calendar being rebuilt.</param>
    /// <param name="source">The stored JSON for that calendar.</param>
    void DeserializeFields(ICalendar value, JObject source);

    /// <summary>
    /// The serializer-neutral name for the calendar type, matching the discriminator the
    /// System.Text.Json package writes for the same calendar. When non-empty, the registry indexes
    /// the serializer under this name as well as under the calendar's assembly-qualified type name,
    /// so a payload written by either package resolves. The default is empty: the serializer then
    /// answers only to the assembly-qualified name, which is what payloads written by 3.x carry.
    /// </summary>
    string CalendarTypeName => "";
}

/// <summary>
/// Convenience base class to strongly type a calendar serializer.
/// </summary>
/// <typeparam name="TCalendar">The calendar type this serializer reads and writes.</typeparam>
public abstract class CalendarSerializer<TCalendar> : ICalendarSerializer where TCalendar : ICalendar
{
    /// <inheritdoc cref="ICalendarSerializer.CalendarTypeName" />
    public virtual string CalendarTypeName => "";

    ICalendar ICalendarSerializer.Create(JObject source)
    {
        return Create(source);
    }

    void ICalendarSerializer.SerializeFields(JsonWriter writer, ICalendar value)
    {
        SerializeFields(writer, (TCalendar) value);
    }

    void ICalendarSerializer.DeserializeFields(ICalendar value, JObject source)
    {
        DeserializeFields((TCalendar) value, source);
    }

    /// <summary>
    /// Writes this kind of calendar's own fields, with the calendar already typed.
    /// </summary>
    /// <param name="writer">The writer positioned inside the calendar's JSON object.</param>
    /// <param name="calendar">The calendar being stored.</param>
    protected abstract void SerializeFields(JsonWriter writer, TCalendar calendar);

    /// <summary>
    /// Reads back what <see cref="SerializeFields(JsonWriter, TCalendar)" /> wrote.
    /// </summary>
    /// <param name="calendar">The calendar being rebuilt.</param>
    /// <param name="source">The stored JSON for that calendar.</param>
    protected abstract void DeserializeFields(TCalendar calendar, JObject source);

    /// <summary>
    /// Builds the calendar instance the stored fields are then read into.
    /// </summary>
    /// <param name="source">The stored JSON for one calendar.</param>
    protected abstract TCalendar Create(JObject source);
}
