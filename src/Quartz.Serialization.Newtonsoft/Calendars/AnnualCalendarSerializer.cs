using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft.Calendars;

internal sealed class AnnualCalendarSerializer : CalendarSerializer<AnnualCalendar>
{
    public override string CalendarTypeName => "AnnualCalendar";

    protected override AnnualCalendar Create(JObject source)
    {
        return new AnnualCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, AnnualCalendar calendar)
    {
        // The payload keeps its date shape: each MonthDay is written pinned to the fixed year.
        writer.WriteDateOnlyArray("ExcludedDays", calendar.DaysExcluded.Select(static day => day.ToDateOnly()));
    }

    protected override void DeserializeFields(AnnualCalendar calendar, JObject source)
    {
        // Payloads written before 4.0 carry full timestamps here rather than dates.
        foreach (var day in source["ExcludedDays"]!.GetDateOnlyArray())
        {
            calendar.AddExcludedDay(MonthDay.From(day));
        }
    }
}
