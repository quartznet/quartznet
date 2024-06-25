using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft;

public interface ICalendarSerializer
{
    ICalendar Create(JObject source);
    void SerializeFields(JsonWriter writer, ICalendar value);
    void DeserializeFields(ICalendar value, JObject source);
}

/// <summary>
/// Convenience base class to strongly type a calendar serializer.
/// </summary>
/// <typeparam name="TCalendar"></typeparam>
public abstract class CalendarSerializer<TCalendar> : ICalendarSerializer where TCalendar : ICalendar
{
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