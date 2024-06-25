using System.Text.Json;

namespace Quartz.Serialization.SystemTextJson;

internal interface ICalendarSerializer
{
    ICalendar Create(JsonElement jsonElement);

    void SerializeFields(Utf8JsonWriter writer, ICalendar calendar);

    void DeserializeFields(ICalendar calendar, JsonElement jsonElement);

    string CalendarTypeName { get; }
}

internal abstract class CalendarSerializer<TCalendar> : ICalendarSerializer where TCalendar : ICalendar
{
    ICalendar ICalendarSerializer.Create(JsonElement jsonElement) => Create(jsonElement);

    public abstract string CalendarTypeName { get; }

    void ICalendarSerializer.SerializeFields(Utf8JsonWriter writer, ICalendar calendar) => SerializeFields(writer, (TCalendar) calendar);

    void ICalendarSerializer.DeserializeFields(ICalendar calendar, JsonElement jsonElement) => DeserializeFields((TCalendar) calendar, jsonElement);

    protected abstract TCalendar Create(JsonElement jsonElement);

    protected abstract void SerializeFields(Utf8JsonWriter writer, TCalendar calendar);

    protected abstract void DeserializeFields(TCalendar calendar, JsonElement jsonElement);
}