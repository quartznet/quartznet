using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Impl.Triggers;
using Quartz.Serialization.Json.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Serialization.Json.Converters;

internal sealed class TriggerConverter : JsonConverter<ITrigger>
{
    private static readonly Dictionary<string, ITriggerSerializer> converters = new(StringComparer.OrdinalIgnoreCase);

    static TriggerConverter()
    {
        AddTriggerSerializer<CalendarIntervalTriggerImpl>(new CalendarIntervalTriggerSerializer());
        AddTriggerSerializer<CronTriggerImpl>(new CronTriggerSerializer());
        AddTriggerSerializer<DailyTimeIntervalTriggerImpl>(new DailyTimeIntervalTriggerSerializer());
        AddTriggerSerializer<SimpleTriggerImpl>(new SimpleTriggerSerializer());
    }

    public override bool CanConvert(Type typeToConvert) => typeof(ITrigger).IsAssignableFrom(typeToConvert);

    public override ITrigger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
            var type = rootElement.GetProperty(options.GetPropertyName("TriggerType")).GetString();

            var triggerSerializer = GetTriggerSerializer(type);
            var scheduleBuilder = triggerSerializer.CreateScheduleBuilder(rootElement, options);

            var key = rootElement.GetProperty(options.GetPropertyName("Key")).GetTriggerKey(options);
            var jobKey = rootElement.GetProperty(options.GetPropertyName("JobKey")).GetJobKey(options);
            var description = rootElement.GetProperty(options.GetPropertyName("Description")).GetString();
            var calendarName = rootElement.GetProperty(options.GetPropertyName("CalendarName")).GetString();
            var jobDataMap = rootElement.GetProperty(options.GetPropertyName("JobDataMap")).GetJobDataMap(options);
            var misfireInstruction = rootElement.GetProperty(options.GetPropertyName("MisfireInstruction")).GetInt32();
            var endTimeUtc = rootElement.GetProperty(options.GetPropertyName("EndTimeUtc")).GetDateTimeOffsetOrNull();
            var startTimeUtc = rootElement.GetProperty(options.GetPropertyName("StartTimeUtc")).GetDateTimeOffset();
            var priority = rootElement.GetProperty(options.GetPropertyName("Priority")).GetInt32();

            var builder = TriggerBuilder.Create()
                .WithSchedule(scheduleBuilder)
                .WithIdentity(key);

            if (jobKey is not null)
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
                var nextFireTimeUtc = rootElement.GetPropertyOrNull(options.GetPropertyName("NextFireTimeUtc"))?.GetDateTimeOffsetOrNull();
                var previousFireTimeUtc = rootElement.GetPropertyOrNull(options.GetPropertyName("PreviousFireTimeUtc"))?.GetDateTimeOffsetOrNull();

                operableTrigger.SetNextFireTimeUtc(nextFireTimeUtc);
                operableTrigger.SetPreviousFireTimeUtc(previousFireTimeUtc);
            }

            triggerSerializer.DeserializeFields(trigger, rootElement, options);
            return trigger;
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to parse ITrigger from json", e);
        }
    }

    public override void Write(Utf8JsonWriter writer, ITrigger value, JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStartObject();
            var type = value.GetType().AssemblyQualifiedNameWithoutVersion();
            var triggerSerializer = GetTriggerSerializer(type);

            writer.WriteString(options.GetPropertyName("TriggerType"), triggerSerializer.TriggerTypeForJson);

            writer.WriteKey(options.GetPropertyName("Key"), value.Key, options);
            writer.WriteKey(options.GetPropertyName("JobKey"), value.JobKey, options);
            writer.WriteString(options.GetPropertyName("Description"), value.Description);
            writer.WriteString(options.GetPropertyName("CalendarName"), value.CalendarName);
            writer.WritePropertyName(options.GetPropertyName("JobDataMap"));
            writer.WriteJobDataMapValue(value.JobDataMap, options);
            writer.WriteNumber(options.GetPropertyName("MisfireInstruction"), value.MisfireInstruction);
            writer.WriteString(options.GetPropertyName("StartTimeUtc"), value.StartTimeUtc);
            writer.WriteString(options.GetPropertyName("EndTimeUtc"), value.EndTimeUtc);
            writer.WriteNumber(options.GetPropertyName("Priority"), value.Priority);
            writer.WriteString(options.GetPropertyName("NextFireTimeUtc"), value.GetNextFireTimeUtc());
            writer.WriteString(options.GetPropertyName("PreviousFireTimeUtc"), value.GetPreviousFireTimeUtc());

            triggerSerializer.SerializeFields(writer, value, options);
            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to serialize ITrigger to json", e);
        }
    }

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