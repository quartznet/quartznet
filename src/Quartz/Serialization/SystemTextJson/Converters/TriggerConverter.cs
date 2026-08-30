using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Serialization.SystemTextJson.Converters;

internal sealed class TriggerConverter(SystemTextJsonSerializerRegistry registry) : JsonConverter<ITrigger>
{
    public override bool CanConvert(Type typeToConvert) => typeof(ITrigger).IsAssignableFrom(typeToConvert);

    public override ITrigger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
            var type = rootElement.GetProperty(options.GetPropertyName("TriggerType")).GetString();

            var triggerSerializer = registry.GetTriggerSerializer(type);
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
                .WithCalendarName(calendarName)
                .UsingJobData(jobDataMap)
                .EndAt(endTimeUtc)
                .StartAt(startTimeUtc)
                .WithPriority(priority)
                .Build();

            if (trigger is IMutableTrigger mutableTrigger)
            {
                mutableTrigger.MisfireInstructionCode = misfireInstruction;

                // Written as the pair the triggers table stores, and absent altogether from payloads
                // written before triggers could be pinned - a missing pair reads back as
                // PreferredNode.None, which is exactly an unpinned trigger.
                string? preferredNode = rootElement.GetPropertyOrNull(options.GetPropertyName("PreferredNode"))?.GetString();
                bool preferredNodeAuto = rootElement.GetPropertyOrNull(options.GetPropertyName("PreferredNodeAuto"))?.ValueKind == JsonValueKind.True;
                mutableTrigger.PreferredNode = PreferredNode.FromStored(preferredNode, preferredNodeAuto);
            }

            if (trigger is IOperableTrigger operableTrigger)
            {
                // These properties might not exist in the JSON if trigger was serialized with older version
                var nextFireTimeUtc = rootElement.GetPropertyOrNull(options.GetPropertyName("NextFireTimeUtc"))?.GetDateTimeOffsetOrNull();
                var previousFireTimeUtc = rootElement.GetPropertyOrNull(options.GetPropertyName("PreviousFireTimeUtc"))?.GetDateTimeOffsetOrNull();

                operableTrigger.NextFireTimeUtc = nextFireTimeUtc;
                operableTrigger.PreviousFireTimeUtc = previousFireTimeUtc;
            }

            if (trigger is Quartz.Impl.Triggers.TriggerBase abstractTrigger)
            {
                abstractTrigger.ExecutionGroup = rootElement.GetPropertyOrNull(options.GetPropertyName("ExecutionGroup"))?.GetString();

                // Both absent from payloads written before triggers could retry: no policy string
                // reads back as no policy, and no attempt as an occurrence that has not been retried.
                string? retryPolicy = rootElement.GetPropertyOrNull(options.GetPropertyName("RetryPolicy"))?.GetString();
                abstractTrigger.RetryPolicy = RetryPolicy.TryParse(retryPolicy, out RetryPolicy? parsed) ? parsed : null;
                abstractTrigger.RetryAttempt = rootElement.GetPropertyOrNull(options.GetPropertyName("RetryAttempt"))?.GetInt32() ?? 0;
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
            var triggerSerializer = registry.GetTriggerSerializer(type);

            writer.WriteString(options.GetPropertyName("TriggerType"), triggerSerializer.TriggerTypeName);

            writer.WriteKey(options.GetPropertyName("Key"), value.Key, options);
            writer.WriteKey(options.GetPropertyName("JobKey"), value.JobKey, options);
            writer.WriteString(options.GetPropertyName("Description"), value.Description);
            writer.WriteString(options.GetPropertyName("CalendarName"), value.CalendarName);
            writer.WritePropertyName(options.GetPropertyName("JobDataMap"));
            writer.WriteJobDataMapValue(value.JobDataMap, options, registry);
            writer.WriteNumber(options.GetPropertyName("MisfireInstruction"), value.MisfireInstructionCode);
            writer.WriteString(options.GetPropertyName("StartTimeUtc"), value.StartTimeUtc);
            writer.WriteString(options.GetPropertyName("EndTimeUtc"), value.EndTimeUtc);
            writer.WriteNumber(options.GetPropertyName("Priority"), value.Priority);
            writer.WriteString(options.GetPropertyName("NextFireTimeUtc"), value.NextFireTimeUtc);
            writer.WriteString(options.GetPropertyName("PreviousFireTimeUtc"), value.PreviousFireTimeUtc);

            if (value is Quartz.Impl.Triggers.TriggerBase abstractTrigger)
            {
                writer.WriteString(options.GetPropertyName("ExecutionGroup"), abstractTrigger.ExecutionGroup);

                // The policy travels as the string the RETRY_POLICY column holds, so a trigger read
                // out of a blob and one read out of the row carry the same value in the same shape.
                writer.WriteString(options.GetPropertyName("RetryPolicy"), abstractTrigger.RetryPolicy?.ToStoredString());
                writer.WriteNumber(options.GetPropertyName("RetryAttempt"), abstractTrigger.RetryAttempt);
            }

            // The pin travels as the pair the triggers table holds - the node name (or the auto-pin
            // sentinel) plus the auto-claim flag - so an automatic pin stays automatic across the
            // round trip instead of hardening into one the user named.
            PreferredNode preferredNode = value.PreferredNode;
            writer.WriteString(options.GetPropertyName("PreferredNode"), preferredNode.StoredNode);
            writer.WriteBoolean(options.GetPropertyName("PreferredNodeAuto"), preferredNode.StoredAutomatic);

            triggerSerializer.SerializeFields(writer, value, options);
            writer.WriteEndObject();
        }
        // A job data value the reader could not accept is refused by name; wrapping that in a second
        // exception of the same type would only bury which entry it was about.
        catch (JsonSerializationException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to serialize ITrigger to json", e);
        }
    }
}