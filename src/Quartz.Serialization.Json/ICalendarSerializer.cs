using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz
{
    public interface ICalendarSerializer
    {
        ICalendar Create(JObject source);
        void SerializeFields(JsonWriter writer, ICalendar value);
        void DeserializeFields(ICalendar value, JObject source);
    }
}