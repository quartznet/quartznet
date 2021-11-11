using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Util;

namespace Quartz.Converters;

internal class JobDataMapConverter : JsonConverter<JobDataMap>
{
    public override JobDataMap? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
        var result = rootElement.GetJobDataMap();
        return result;
    }

    public override void Write(Utf8JsonWriter writer, JobDataMap value, JsonSerializerOptions options)
    {
        writer.WriteJobDataMapValue(value);
    }
}