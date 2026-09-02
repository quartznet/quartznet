using Newtonsoft.Json;

namespace Quartz.Serialization.Newtonsoft;

internal sealed class StringKeyDirtyFlagMapConverter(NewtonsoftJsonSerializerRegistry registry) : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        // A JobDataMap is what a persistent store puts in a column, so an entry the reader could never
        // turn back into a value is refused here — before a byte of it is written, and while there is
        // still someone to tell. A SchedulerContext is not stored, so it is written as it always was.
        if (value is JobDataMap jobDataMap)
        {
            JobDataValues.Refuse(jobDataMap, registry);
        }

        JobDataValues.WriteMap(writer, (IDictionary<string, object?>) value!, serializer);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        IDictionary<string, object?> innerMap = JobDataValues.ReadMap(reader, serializer);

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
