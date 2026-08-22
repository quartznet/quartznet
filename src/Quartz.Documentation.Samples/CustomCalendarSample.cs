// The system-text-json page shows this sample with its using directives, so the region has to start at
// the top of a file — which is why these two types sit in the global namespace rather than in
// Quartz.Documentation.Samples with everything else.

#region sample_stj_custom_calendar

using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson.Calendars;

public sealed class CustomCalendar : BaseCalendar
{
    public bool SomeCustomProperty { get; set; } = true;
}

// JSON serialization support
public sealed class CustomCalendarSerializer : CalendarSerializer<CustomCalendar>
{
    public override string CalendarTypeName => "CustomCalendar";

    protected override CustomCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new CustomCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, CustomCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteBoolean("SomeCustomProperty", calendar.SomeCustomProperty);
    }

    protected override void DeserializeFields(CustomCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.SomeCustomProperty = jsonElement.GetProperty("SomeCustomProperty").GetBoolean();
    }
}

#endregion
