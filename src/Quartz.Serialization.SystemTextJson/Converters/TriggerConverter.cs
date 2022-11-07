using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Triggers;
using Quartz.Util;

namespace Quartz.Converters;

internal class TriggerConverter : JsonConverter<ITrigger>
{
    private static readonly Dictionary<string, ITriggerSerializer> converters = new()
    {
        { CalendarIntervalTriggerSerializer.TriggerTypeKey, CalendarIntervalTriggerSerializer.Instance },
        { CronTriggerSerializer.TriggerTypeKey, CronTriggerSerializer.Instance },
        { DailyTimeIntervalTriggerSerializer.TriggerTypeKey, DailyTimeIntervalTriggerSerializer.Instance },
        { SimpleTriggerSerializer.TriggerTypeKey, SimpleTriggerSerializer.Instance },

        // Support also type name
        { typeof(CalendarIntervalTriggerImpl).AssemblyQualifiedNameWithoutVersion(), CalendarIntervalTriggerSerializer.Instance },
        { typeof(CronTriggerImpl).AssemblyQualifiedNameWithoutVersion(), CronTriggerSerializer.Instance },
        { typeof(DailyTimeIntervalTriggerImpl).AssemblyQualifiedNameWithoutVersion(), DailyTimeIntervalTriggerSerializer.Instance },
        { typeof(SimpleTriggerImpl).AssemblyQualifiedNameWithoutVersion(), SimpleTriggerSerializer.Instance }
    };

    public override bool CanConvert(Type objectType) => typeof(ITrigger).IsAssignableFrom(objectType);

    public override ITrigger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
            var type = rootElement.GetProperty("TriggerType").GetString();

            var triggerSerializer = GetTriggerSerializer(type);
            var scheduleBuilder = triggerSerializer.CreateScheduleBuilder(rootElement);

            var key = rootElement.GetProperty("Key").GetTriggerKey();
            var jobKey = rootElement.GetProperty("JobKey").GetJobKey();
            var description = rootElement.GetProperty("Description").GetString();
            var calendarName = rootElement.GetProperty("CalendarName").GetString();
            var jobDataMap = rootElement.GetProperty("JobDataMap").GetJobDataMap();
            var misfireInstruction = rootElement.GetProperty("MisfireInstruction").GetInt32();
            var endTimeUtc = rootElement.GetProperty("EndTimeUtc").GetDateTimeOffsetOrNull();
            var startTimeUtc = rootElement.GetProperty("StartTimeUtc").GetDateTimeOffset();
            var priority = rootElement.GetProperty("Priority").GetInt32();

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

            ((IMutableTrigger)trigger).MisfireInstruction = misfireInstruction;
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

            writer.WriteString("TriggerType", triggerSerializer.TriggerTypeForJson);

            writer.WriteKey("Key", value.Key);
            writer.WriteKey("JobKey", value.JobKey);
            writer.WriteString("Description", value.Description);
            writer.WriteString("CalendarName", value.CalendarName);
            writer.WriteJobDataMap("JobDataMap", value.JobDataMap);
            writer.WriteNumber("MisfireInstruction", value.MisfireInstruction);
            writer.WriteString("StartTimeUtc", value.StartTimeUtc);
            writer.WriteString("EndTimeUtc", value.EndTimeUtc);
            writer.WriteNumber("Priority", value.Priority);

            triggerSerializer.SerializeFields(writer, value);
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
            throw new ArgumentException("Don't know how to handle " + typeName);
        }

        return converter;
    }
}