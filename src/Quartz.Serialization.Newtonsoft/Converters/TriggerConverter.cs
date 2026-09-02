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

            // Before anything is written, so a trigger carrying one unreadable job data value puts
            // nothing at all in the column.
            JobDataValues.Refuse(trigger.JobDataMap, registry);

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
            JobDataValues.WriteMap(writer, trigger.JobDataMap, serializer);

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

            if (trigger is Quartz.Impl.Triggers.TriggerBase abstractTrigger)
            {
                writer.WritePropertyName("ExecutionGroup");
                writer.WriteValue(abstractTrigger.ExecutionGroup);

                // The policy travels as the string the RETRY_POLICY column holds, so a trigger read
                // out of a blob and one read out of the row carry the same value in the same shape.
                writer.WritePropertyName("RetryPolicy");
                writer.WriteValue(abstractTrigger.RetryPolicy?.ToStoredString());

                writer.WritePropertyName("RetryAttempt");
                writer.WriteValue(abstractTrigger.RetryAttempt);
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
        // A refused job data value already says which entry it is about and what to do; wrapping it in
        // a second exception of the same type would only bury that.
        catch (Quartz.JsonSerializationException)
        {
            throw;
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
            (JObject source, JobDataMap jobDataMap) = ReadTrigger(reader, serializer);
            var type = source["TriggerType"]!.Value<string>()!;

            var triggerSerializer = registry.GetTriggerSerializer(type);
            var scheduleBuilder = triggerSerializer.CreateScheduleBuilder(source);

            var key = source.GetTriggerKey("Key");
            var jobKey = source.GetJobKey("JobKey");
            var description = source.Value<string>("Description");
            var calendarName = source.Value<string>("CalendarName");
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

            if (trigger is Quartz.Impl.Triggers.TriggerBase abstractTrigger)
            {
                abstractTrigger.ExecutionGroup = source.Value<string>("ExecutionGroup");

                // Both absent from payloads written before triggers could retry: no policy string
                // reads back as no policy, and no attempt as an occurrence that has not been retried.
                abstractTrigger.RetryPolicy = RetryPolicy.TryParse(source.Value<string>("RetryPolicy"), out RetryPolicy? retryPolicy) ? retryPolicy : null;
                abstractTrigger.RetryAttempt = source.Value<int?>("RetryAttempt") ?? 0;
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

    /// <summary>
    /// Reads the stored trigger, taking its job data map off the reader as the map is reached rather
    /// than out of a token tree the whole trigger was parsed into first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="JobDataValues.ReadMap" /> is the half the default path reads a map with, and it can
    /// only do its work while the reader is still on the map: whether text that looks like a timestamp
    /// becomes one is a reader setting, so a map that arrives inside a
    /// <see cref="JObject.Load(JsonReader)" /> over the whole trigger has already had every such string
    /// turned into a date — nested ones included, which is what made a stored string map come back with
    /// values the application never put in it. Turning date parsing off for the whole trigger instead
    /// would change what <c>StartTimeUtc</c> and its siblings read back as, and those are in databases
    /// already.
    /// </para>
    /// <para>
    /// The map is therefore the one property not among the returned object's. Nothing reads it from
    /// there — the trigger a serializer's <c>DeserializeFields</c> is handed already carries the map —
    /// and putting it back would mean parsing it a second time.
    /// </para>
    /// </remarks>
    private static (JObject Source, JobDataMap JobDataMap) ReadTrigger(JsonReader reader, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.StartObject)
        {
            throw new Quartz.JsonSerializationException(
                $"A trigger is stored as a JSON object, and this payload holds {reader.TokenType} where the trigger should be.");
        }

        JObject source = new();
        JobDataMap jobDataMap = new();

        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            string name = (string) reader.Value!;
            reader.Read();

            // A payload written before triggers carried a map has no such property at all, which is an
            // empty map; one that has it explicitly null is the same thing said out loud.
            if (name == "JobDataMap" && reader.TokenType != JsonToken.Null)
            {
                jobDataMap = new JobDataMap(JobDataValues.ReadMap(reader, serializer));
                jobDataMap.ClearDirtyFlag();
                continue;
            }

            source[name] = JToken.Load(reader);
        }

        return (source, jobDataMap);
    }

    public override bool CanConvert(Type objectType) => typeof(ITrigger).IsAssignableFrom(objectType);
}