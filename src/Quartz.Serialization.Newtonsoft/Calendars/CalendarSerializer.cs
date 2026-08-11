using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft.Calendars;

public interface ICalendarSerializer
{
    ICalendar Create(JObject source);
    void SerializeFields(JsonWriter writer, ICalendar value);
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
/// <typeparam name="TCalendar"></typeparam>
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

    protected abstract void SerializeFields(JsonWriter writer, TCalendar calendar);

    protected abstract void DeserializeFields(TCalendar calendar, JObject source);

    protected abstract TCalendar Create(JObject source);
}
