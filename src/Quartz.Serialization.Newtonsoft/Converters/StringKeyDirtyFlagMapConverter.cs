using Newtonsoft.Json;

using Quartz.Util;

namespace Quartz.Converters;

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
        JobDataMap map = new JobDataMap(innerMap);
        return map;
    }

    public override bool CanConvert(Type objectType)
    {
        return typeof(StringKeyDirtyFlagMap).IsAssignableFrom(objectType);
    }
}