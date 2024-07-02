using System.Text.Json;

namespace Quartz.Serialization.Json.Calendars;

public interface ICalendarSerializer
{
    ICalendar Create(JsonElement jsonElement, JsonSerializerOptions options);

    void SerializeFields(Utf8JsonWriter writer, ICalendar calendar, JsonSerializerOptions options);

    void DeserializeFields(ICalendar calendar, JsonElement jsonElement, JsonSerializerOptions options);

    string CalendarTypeName { get; }
}

public abstract class CalendarSerializer<TCalendar> : ICalendarSerializer where TCalendar : ICalendar
{
    ICalendar ICalendarSerializer.Create(JsonElement jsonElement, JsonSerializerOptions options) => Create(jsonElement, options);

    public abstract string CalendarTypeName { get; }

    void ICalendarSerializer.SerializeFields(Utf8JsonWriter writer, ICalendar calendar, JsonSerializerOptions options) => SerializeFields(writer, (TCalendar) calendar, options);

    void ICalendarSerializer.DeserializeFields(ICalendar calendar, JsonElement jsonElement, JsonSerializerOptions options) => DeserializeFields((TCalendar) calendar, jsonElement, options);

    protected abstract TCalendar Create(JsonElement jsonElement, JsonSerializerOptions options);

    protected abstract void SerializeFields(Utf8JsonWriter writer, TCalendar calendar, JsonSerializerOptions options);

    protected abstract void DeserializeFields(TCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options);
}