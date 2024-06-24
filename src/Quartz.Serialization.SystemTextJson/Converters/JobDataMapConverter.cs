using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Util;

namespace Quartz.Converters;

internal sealed class JobDataMapConverter : JsonConverter<JobDataMap>
{
    public override JobDataMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
            var result = rootElement.GetJobDataMap();
            return result;
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to parse JobDataMap from json", e);
        }
    }

    public override void Write(Utf8JsonWriter writer, JobDataMap value, JsonSerializerOptions options)
    {
        try
        {
            writer.WriteJobDataMapValue(value, options);
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to serialize JobDataMap to json", e);
        }
    }
}