using Newtonsoft.Json;

namespace Quartz.Serialization.Newtonsoft;

internal sealed class StringKeyDirtyFlagMapConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var map = new Dictionary<string, object?>((IDictionary<string, object?>) value!);
        serializer.Serialize(writer, map);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        IDictionary<string, object?> innerMap = serializer.Deserialize<IDictionary<string, object?>>(reader)!;

        // The declared type decides what comes back; unconditionally returning a JobDataMap would hand
        // job code the wrong runtime type for a SchedulerContext-typed read.
        if (objectType == typeof(SchedulerContext))
        {
            return new SchedulerContext(innerMap);
        }

        return new JobDataMap(innerMap);
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(JobDataMap) || objectType == typeof(SchedulerContext);
    }
}
