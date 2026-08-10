using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft;

internal sealed class TriggerConverter(NewtonsoftJsonSerializerRegistry registry) : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        try
        {
            var trigger = (ITrigger) value!;

            writer.WriteStartObject();
            var type = value!.GetType().AssemblyQualifiedNameWithoutVersion();
            var triggerSerializer = registry.GetTriggerSerializer(type);

            writer.WritePropertyName("TriggerType");
            writer.WriteValue(triggerSerializer.TriggerTypeName);

            writer.WriteKey("Key", trigger.Key);
            writer.WriteKey("JobKey", trigger.JobKey);

            writer.WritePropertyName("Description");
            writer.WriteValue(trigger.Description);

            writer.WritePropertyName("CalendarName");
            writer.WriteValue(trigger.CalendarName);

            writer.WritePropertyName("JobDataMap");
            writer.WriteJobDataMapValue(trigger.JobDataMap);

            writer.WritePropertyName("MisfireInstruction");
            writer.WriteValue(trigger.MisfireInstructionCode);

            writer.WritePropertyName("StartTimeUtc");
            writer.WriteValue(trigger.StartTimeUtc);

            writer.WritePropertyName("EndTimeUtc");
            writer.WriteValue(trigger.EndTimeUtc);

            writer.WritePropertyName("Priority");
            writer.WriteValue(trigger.Priority);

            writer.WritePropertyName("NextFireTimeUtc");
            writer.WriteValue(trigger.NextFireTimeUtc);

            writer.WritePropertyName("PreviousFireTimeUtc");
            writer.WriteValue(trigger.PreviousFireTimeUtc);

            if (trigger is Quartz.Impl.Triggers.AbstractTrigger abstractTrigger)
            {
                writer.WritePropertyName("ExecutionGroup");
                writer.WriteValue(abstractTrigger.ExecutionGroup);
            }

            // The pin travels as the pair the triggers table holds - the node name (or the auto-pin
            // sentinel) plus the auto-claim flag - so an automatic pin stays automatic across the
            // round trip instead of hardening into one the user named.
            PreferredNode preferredNode = trigger.PreferredNode;

            writer.WritePropertyName("PreferredNode");
            writer.WriteValue(preferredNode.StoredNode);

            writer.WritePropertyName("PreferredNodeAuto");
            writer.WriteValue(preferredNode.StoredAutomatic);

            triggerSerializer.SerializeFields(writer, trigger);
            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            // Quartz's exception, deliberately - the qualification is what keeps the choice visible,
            // because this namespace is nested inside Quartz and the unqualified name would bind here
            // whether or not anyone meant it to.
            throw new Quartz.JsonSerializationException("Failed to serialize ITrigger to json", e);
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        try
        {
            var source = JObject.Load(reader);
            var type = source["TriggerType"]!.Value<string>()!;

            var triggerSerializer = registry.GetTriggerSerializer(type);
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
                string? preferredNode = source.Value<string>("PreferredNode");
                bool preferredNodeAuto = source.Value<bool?>("PreferredNodeAuto") ?? false;
                mutableTrigger.PreferredNode = PreferredNode.FromStored(preferredNode, preferredNodeAuto);
            }

            if (trigger is IOperableTrigger operableTrigger)
            {
                // These properties might not exist in the JSON if trigger was serialized with older version
                var nextFireTimeUtc = source.Value<DateTimeOffset?>("NextFireTimeUtc");
                var previousFireTimeUtc = source.Value<DateTimeOffset?>("PreviousFireTimeUtc");

                operableTrigger.NextFireTimeUtc = nextFireTimeUtc;
                operableTrigger.PreviousFireTimeUtc = previousFireTimeUtc;
            }

            if (trigger is Quartz.Impl.Triggers.AbstractTrigger abstractTrigger)
            {
                abstractTrigger.ExecutionGroup = source.Value<string>("ExecutionGroup");
            }

            triggerSerializer.DeserializeFields(trigger, source);
            return trigger;
        }
        catch (Exception e)
        {
            // Quartz's exception, deliberately - see the note on the serialize side above.
            throw new Quartz.JsonSerializationException("Failed to parse ITrigger from json", e);
        }
    }

    public override bool CanConvert(Type objectType) => typeof(ITrigger).IsAssignableFrom(objectType);
}