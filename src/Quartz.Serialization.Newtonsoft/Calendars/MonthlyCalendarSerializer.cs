using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft.Calendars;

internal sealed class MonthlyCalendarSerializer : CalendarSerializer<MonthlyCalendar>
{
    public override string CalendarTypeName => "MonthlyCalendar";

    protected override MonthlyCalendar Create(JObject source)
    {
        return new MonthlyCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, MonthlyCalendar calendar)
    {
        writer.WriteArray("ExcludedDays", calendar.DaysExcluded.Order(), static (w, v) => w.WriteValue(v));
    }

    protected override void DeserializeFields(MonthlyCalendar calendar, JObject source)
    {
        foreach (int day in source["ExcludedDays"]!.GetDayOfMonthArray())
        {
            calendar.AddExcludedDay(day);
        }
    }
}
