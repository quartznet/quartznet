using System;
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
            var result = rootElement.GetJobDataMap(options);
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
        // A refusal already says which entry it is about and what to do; wrapping it in a second
        // exception of the same type would only bury that behind "Failed to serialize JobDataMap".
        catch (JsonSerializationException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to serialize JobDataMap to json", e);
        }
    }
}