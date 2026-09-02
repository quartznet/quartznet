using System.Text.Json;

namespace Quartz.Serialization.SystemTextJson.Calendars;

/// <summary>
/// How one calendar type is written to and read from the store's JSON.
/// </summary>
/// <remarks>
/// Implemented by deriving from <see cref="CalendarSerializer{TCalendar}" /> rather than directly:
/// the typed base is what makes a mismatched pairing a compile error rather than an
/// <see cref="InvalidCastException" /> on the first calendar that round-trips.
/// </remarks>
public interface ICalendarSerializer
{
    /// <summary>
    /// Builds the calendar the payload describes, before its fields are read into it.
    /// </summary>
    ICalendar Create(JsonElement jsonElement, JsonSerializerOptions options);

    /// <summary>
    /// Writes the calendar's own fields. The type, description, time zone and base calendar are
    /// written around this by the converter.
    /// </summary>
    void SerializeFields(Utf8JsonWriter writer, ICalendar calendar, JsonSerializerOptions options);

    /// <summary>
    /// Reads the calendar's own fields back.
    /// </summary>
    void DeserializeFields(ICalendar calendar, JsonElement jsonElement, JsonSerializerOptions options);

    /// <summary>
    /// The discriminator written into the payload, and matched against when reading one.
    /// </summary>
    string CalendarTypeName { get; }
}

/// <summary>
/// The base class for a serializer of a calendar type of your own.
/// </summary>
/// <remarks>
/// Register one with <c>AddCalendarSerializer</c> inside <c>UseSystemTextJsonSerializer(configure)</c>;
/// without it the first store write of that calendar fails, naming the type.
/// </remarks>
/// <typeparam name="TCalendar">The calendar type this serializer is for.</typeparam>
public abstract class CalendarSerializer<TCalendar> : ICalendarSerializer where TCalendar : ICalendar
{
    ICalendar ICalendarSerializer.Create(JsonElement jsonElement, JsonSerializerOptions options) => Create(jsonElement, options);

    /// <inheritdoc />
    public abstract string CalendarTypeName { get; }

    void ICalendarSerializer.SerializeFields(Utf8JsonWriter writer, ICalendar calendar, JsonSerializerOptions options) => SerializeFields(writer, (TCalendar) calendar, options);

    void ICalendarSerializer.DeserializeFields(ICalendar calendar, JsonElement jsonElement, JsonSerializerOptions options) => DeserializeFields((TCalendar) calendar, jsonElement, options);

    /// <summary>
    /// Builds the calendar the payload describes, before its fields are read into it.
    /// </summary>
    protected abstract TCalendar Create(JsonElement jsonElement, JsonSerializerOptions options);

    /// <summary>
    /// Writes the calendar's own fields.
    /// </summary>
    protected abstract void SerializeFields(Utf8JsonWriter writer, TCalendar calendar, JsonSerializerOptions options);

    /// <summary>
    /// Reads the calendar's own fields back.
    /// </summary>
    protected abstract void DeserializeFields(TCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options);
}