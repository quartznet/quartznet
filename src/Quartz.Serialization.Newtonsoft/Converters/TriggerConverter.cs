using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Triggers;
using Quartz.Util;

namespace Quartz.Converters;

internal sealed class TriggerConverter : JsonConverter
{
    private static readonly Dictionary<string, ITriggerSerializer> converters = new(StringComparer.OrdinalIgnoreCase);

    static TriggerConverter()
    {
        AddTriggerSerializer<CalendarIntervalTriggerImpl>(new CalendarIntervalTriggerSerializer());
        AddTriggerSerializer<CronTriggerImpl>(new CronTriggerSerializer());
        AddTriggerSerializer<DailyTimeIntervalTriggerImpl>(new DailyTimeIntervalTriggerSerializer());
        AddTriggerSerializer<SimpleTriggerImpl>(new SimpleTriggerSerializer());
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        try
        {
            var trigger = (ITrigger) value!;

            writer.WriteStartObject();
            var type = value!.GetType().AssemblyQualifiedNameWithoutVersion();
            var triggerSerializer = GetTriggerSerializer(type);

            writer.WritePropertyName("TriggerType");
            writer.WriteValue(triggerSerializer.TriggerTypeForJson);

            writer.WriteKey("Key", trigger.Key);
            writer.WriteKey("JobKey", trigger.JobKey);

            writer.WritePropertyName("Description");
            writer.WriteValue(trigger.Description);

            writer.WritePropertyName("CalendarName");
            writer.WriteValue(trigger.CalendarName);

            writer.WritePropertyName("JobDataMap");
            writer.WriteJobDataMapValue(trigger.JobDataMap);

            writer.WritePropertyName("MisfireInstruction");
            writer.WriteValue(trigger.MisfireInstruction);

            writer.WritePropertyName("StartTimeUtc");
            writer.WriteValue(trigger.StartTimeUtc);

            writer.WritePropertyName("EndTimeUtc");
            writer.WriteValue(trigger.EndTimeUtc);

            writer.WritePropertyName("Priority");
            writer.WriteValue(trigger.Priority);

            writer.WritePropertyName("NextFireTimeUtc");
            writer.WriteValue(trigger.GetNextFireTimeUtc());

            writer.WritePropertyName("PreviousFireTimeUtc");
            writer.WriteValue(trigger.GetPreviousFireTimeUtc());

            triggerSerializer.SerializeFields(writer, trigger);
            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to serialize ITrigger to json", e);
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        try
        {
            var source = JObject.Load(reader);
            var type = source["TriggerType"]!.Value<string>()!;

            var triggerSerializer = GetTriggerSerializer(type);
            var scheduleBuilder = triggerSerializer.CreateScheduleBuilder(source);

            var key = source.GetTriggerKey("Key");
            var jobKey = source.GetJobKey("JobKey");
            var description = source.Value<string>("Description");
            var calendarName = source.Value<string>("CalendarName");
            var jobDataMap = source.Value<JObject>("JobDataMap").GetJobDataMap() ?? new JobDataMap();
            var misfireInstruction = source.Value<int>("MisfireInstruction");
            var endTimeUtc = source.Value<DateTimeOffset?>("EndTimeUtc");
            var startTimeUtc = source.Value<DateTimeOffset>("StartTimeUtc");
            var priority = source.Value<int>("Priority");

            var builder = TriggerBuilder.Create()
                .WithSchedule(scheduleBuilder)
                .WithIdentity(key);

            if (jobKey != null)
            {
                builder = builder.ForJob(jobKey);
            }

            var trigger = builder
                .WithDescription(description)
                .ModifiedByCalendar(calendarName)
                .UsingJobData(jobDataMap)
                .EndAt(endTimeUtc)
                .StartAt(startTimeUtc)
                .WithPriority(priority)
                .Build();

            if (trigger is IMutableTrigger mutableTrigger)
            {
                mutableTrigger.MisfireInstruction = misfireInstruction;
            }

            if (trigger is IOperableTrigger operableTrigger)
            {
                // These properties might not exist in the JSON if trigger was serialized with older version
                var nextFireTimeUtc = source.Value<DateTimeOffset?>("NextFireTimeUtc");
                var previousFireTimeUtc = source.Value<DateTimeOffset?>("PreviousFireTimeUtc");

                operableTrigger.SetNextFireTimeUtc(nextFireTimeUtc);
                operableTrigger.SetPreviousFireTimeUtc(previousFireTimeUtc);
            }

            triggerSerializer.DeserializeFields(trigger, source);
            return trigger;
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to parse ITrigger from json", e);
        }
    }

    public override bool CanConvert(Type objectType) => typeof(ITrigger).IsAssignableFrom(objectType);

    private static ITriggerSerializer GetTriggerSerializer(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || !converters.TryGetValue(typeName!, out var converter))
        {
            throw new ArgumentException($"Don't know how to handle {typeName}", nameof(typeName));
        }

        return converter;
    }

    public static void AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        converters[serializer.TriggerTypeForJson] = serializer;

        // Support also type name
        converters[typeof(TTrigger).AssemblyQualifiedNameWithoutVersion()] = serializer;
    }
}