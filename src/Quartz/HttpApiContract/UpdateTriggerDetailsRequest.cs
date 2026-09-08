#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.HttpApiContract;

/// <summary>
/// A <see cref="TriggerDetailsUpdate" /> on the wire: what an update changes, and nothing about what
/// it leaves alone.
/// </summary>
/// <remarks>
/// <para>
/// The body is read the way RFC 7386 reads a merge patch, which is the only reading that can say what
/// this API has to say: a member that is <b>absent</b> leaves the trigger's value alone, and a member
/// present as <c>null</c> <b>clears</b> it. Five of the eight are clearable — the description, the
/// calendar name, the execution group, the retry policy and the node pin — so a shape in which absent
/// and null were the same thing could set them but never unset them.
/// </para>
/// <para>
/// That distinction is what the nested converter exists for: a record's generated reader cannot tell an
/// absent member from a null one, because both arrive as <see langword="null" />. It also writes only
/// the members the update actually set, so a client asking to change one thing sends one member.
/// </para>
/// <para>
/// The members are spelled as the trigger body spells them, so a caller reading a trigger and then
/// editing it types the same names twice: the misfire instruction is <c>misfireInstruction</c>, the pin
/// is the <c>preferredNode</c>/<c>preferredNodeAuto</c> pair the triggers table holds, and the retry
/// policy is the stored string. <c>misfireInstructionFamily</c> is the one member with no counterpart
/// on a trigger — see <see cref="MisfireInstructionFamily" />.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
internal sealed record UpdateTriggerDetailsRequest : IValidatable
{
    /// <summary>Whether <see cref="Description" /> is part of the update.</summary>
    public bool HasDescription { get; init; }

    /// <summary>The description to set, or <see langword="null" /> to clear it.</summary>
    public string? Description { get; init; }

    /// <summary>Whether <see cref="Priority" /> is part of the update.</summary>
    public bool HasPriority { get; init; }

    /// <summary>The priority to set.</summary>
    public int Priority { get; init; }

    /// <summary>Whether <see cref="JobDataMap" /> is part of the update.</summary>
    public bool HasJobDataMap { get; init; }

    /// <summary>The job data map to set, or <see langword="null" /> to empty it.</summary>
    public Quartz.JobDataMap? JobDataMap { get; init; }

    /// <summary>Whether <see cref="CalendarName" /> is part of the update.</summary>
    public bool HasCalendarName { get; init; }

    /// <summary>The calendar to associate, or <see langword="null" /> to disassociate.</summary>
    public string? CalendarName { get; init; }

    /// <summary>Whether <see cref="MisfireInstruction" /> is part of the update.</summary>
    public bool HasMisfireInstruction { get; init; }

    /// <summary>The misfire instruction code to set.</summary>
    public int MisfireInstruction { get; init; }

    /// <summary>
    /// The schedule family <see cref="MisfireInstruction" /> is stated in — <c>Simple</c>, <c>Cron</c>,
    /// <c>CalendarInterval</c>, <c>DailyTimeInterval</c> or <c>Recurrence</c> — or <see langword="null" />
    /// when the caller has a bare code.
    /// </summary>
    /// <remarks>
    /// The same number means a different policy in each family, so naming one is what lets the store
    /// refuse an update aimed at a trigger of another rather than silently applying the wrong policy.
    /// Omitting it is how a code is set on a trigger outside the five built-in families, and is what
    /// <see cref="TriggerDetailsUpdate.WithMisfireInstructionCode" /> does.
    /// </remarks>
    public string? MisfireInstructionFamily { get; init; }

    /// <summary>Whether the node pin is part of the update.</summary>
    public bool HasPreferredNode { get; init; }

    /// <summary>
    /// The preferred-node column as the triggers table holds it: <see langword="null" /> to clear the
    /// pin, <c>*</c> to ask for an automatic one, or a scheduler instance id.
    /// </summary>
    public string? PreferredNode { get; init; }

    /// <summary>
    /// The auto-claim flag as the triggers table holds it, only ever set beside a node name.
    /// </summary>
    public bool PreferredNodeAuto { get; init; }

    /// <summary>Whether <see cref="ExecutionGroup" /> is part of the update.</summary>
    public bool HasExecutionGroup { get; init; }

    /// <summary>The execution group to join, or <see langword="null" /> to leave every group.</summary>
    public string? ExecutionGroup { get; init; }

    /// <summary>Whether <see cref="RetryPolicy" /> is part of the update.</summary>
    public bool HasRetryPolicy { get; init; }

    /// <summary>
    /// The retry policy in the stored form the <c>RETRY_POLICY</c> column carries — for example
    /// <c>fixed;3;00:00:30</c> — or <see langword="null" /> to stop retrying.
    /// </summary>
    public string? RetryPolicy { get; init; }

    /// <summary>
    /// The five families, spelled on the wire. Written out rather than derived from the internal enum,
    /// so that renaming a member of that enum cannot quietly rename a wire value.
    /// </summary>
    private static readonly (string Name, TriggerFamily Family)[] families =
    [
        ("Simple", TriggerFamily.Simple),
        ("Cron", TriggerFamily.Cron),
        ("CalendarInterval", TriggerFamily.CalendarInterval),
        ("DailyTimeInterval", TriggerFamily.DailyTimeInterval),
        ("Recurrence", TriggerFamily.Recurrence)
    ];

    public static UpdateTriggerDetailsRequest Create(TriggerDetailsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return new UpdateTriggerDetailsRequest
        {
            HasDescription = update.HasDescription,
            Description = update.Description,
            HasPriority = update.HasPriority,
            Priority = update.Priority,
            HasJobDataMap = update.HasJobDataMap,
            JobDataMap = update.JobDataMap,
            HasCalendarName = update.HasCalendarName,
            CalendarName = update.CalendarName,
            HasMisfireInstruction = update.HasMisfireInstruction,
            MisfireInstruction = update.MisfireInstructionCode,
            MisfireInstructionFamily = NameOf(update.MisfireInstructionFamily),
            HasPreferredNode = update.HasPreferredNode,
            PreferredNode = update.PreferredNode.StoredNode,
            PreferredNodeAuto = update.PreferredNode.StoredAutomatic,
            HasExecutionGroup = update.HasExecutionGroup,
            ExecutionGroup = update.ExecutionGroup,
            HasRetryPolicy = update.HasRetryPolicy,
            RetryPolicy = update.RetryPolicy?.ToStoredString()
        };
    }

    /// <summary>
    /// The update this body asks for. Call <c>Validate</c> first: this reads the misfire family and the
    /// retry policy as already legal.
    /// </summary>
    public TriggerDetailsUpdate AsUpdate()
    {
        TriggerDetailsUpdate update = new();

        if (HasDescription)
        {
            update.WithDescription(Description);
        }

        if (HasPriority)
        {
            update.WithPriority(Priority);
        }

        if (HasJobDataMap)
        {
            update.WithJobDataMap(JobDataMap ?? new Quartz.JobDataMap());
        }

        if (HasCalendarName)
        {
            update.WithCalendarName(CalendarName);
        }

        if (HasMisfireInstruction)
        {
            // The typed overloads are what carry the family through to the store's check; the bare code
            // is the only form that skips it, and it is the form a body naming no family asked for.
            switch (FamilyOf(MisfireInstructionFamily))
            {
                case TriggerFamily.Simple:
                    update.WithMisfireInstruction((SimpleTriggerMisfireInstruction) MisfireInstruction);
                    break;
                case TriggerFamily.Cron:
                    update.WithMisfireInstruction((CronTriggerMisfireInstruction) MisfireInstruction);
                    break;
                case TriggerFamily.CalendarInterval:
                    update.WithMisfireInstruction((CalendarIntervalTriggerMisfireInstruction) MisfireInstruction);
                    break;
                case TriggerFamily.DailyTimeInterval:
                    update.WithMisfireInstruction((DailyTimeIntervalTriggerMisfireInstruction) MisfireInstruction);
                    break;
                case TriggerFamily.Recurrence:
                    update.WithMisfireInstruction((RecurrenceTriggerMisfireInstruction) MisfireInstruction);
                    break;
                default:
                    update.WithMisfireInstructionCode(MisfireInstruction);
                    break;
            }
        }

        if (HasPreferredNode)
        {
            update.WithPreferredNode(Quartz.PreferredNode.FromStored(PreferredNode, PreferredNodeAuto));
        }

        if (HasExecutionGroup)
        {
            update.WithExecutionGroup(ExecutionGroup);
        }

        if (HasRetryPolicy)
        {
            update.WithRetryPolicy(Quartz.RetryPolicy.TryParse(RetryPolicy, out Quartz.RetryPolicy? policy) ? policy : null);
        }

        return update;
    }

    public IEnumerable<string> Validate()
    {
        if (MisfireInstructionFamily is not null && FamilyOf(MisfireInstructionFamily) is null)
        {
            yield return $"Misfire instruction family '{MisfireInstructionFamily}' is not one of {string.Join(", ", families.Select(x => x.Name))}";
        }

        if (MisfireInstructionFamily is not null && !HasMisfireInstruction)
        {
            yield return "Misfire instruction family was given without a misfire instruction";
        }

        if (HasRetryPolicy && RetryPolicy is not null && !Quartz.RetryPolicy.TryParse(RetryPolicy, out _))
        {
            yield return $"Retry policy '{RetryPolicy}' is not a valid retry policy";
        }
    }

    private static string? NameOf(TriggerFamily? family)
    {
        foreach ((string name, TriggerFamily candidate) in families)
        {
            if (family == candidate)
            {
                return name;
            }
        }

        return null;
    }

    private static TriggerFamily? FamilyOf(string? name)
    {
        foreach ((string candidate, TriggerFamily family) in families)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return family;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads and writes the merge-patch body <see cref="UpdateTriggerDetailsRequest" /> describes.
    /// </summary>
    /// <remarks>
    /// Nested so that it is not itself a wire-contract type — <c>WireFormatSourceGenerationTest</c>
    /// sweeps the namespace and would rightly ask for a <c>[JsonSerializable]</c> entry for a converter.
    /// The reading half is the same shape <c>TriggerConverter</c> uses, for the same reason: a
    /// <see cref="JsonDocument" /> is what can be asked whether a member was there at all.
    /// </remarks>
    internal sealed class Converter : JsonConverter<UpdateTriggerDetailsRequest>
    {
        public override UpdateTriggerDetailsRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("A trigger details update is a JSON object");
            }

            JsonElement? description = Member(root, options, "Description");
            JsonElement? priority = Member(root, options, "Priority");
            JsonElement? jobDataMap = Member(root, options, "JobDataMap");
            JsonElement? calendarName = Member(root, options, "CalendarName");
            JsonElement? misfireInstruction = Member(root, options, "MisfireInstruction");
            JsonElement? preferredNode = Member(root, options, "PreferredNode");
            JsonElement? executionGroup = Member(root, options, "ExecutionGroup");
            JsonElement? retryPolicy = Member(root, options, "RetryPolicy");

            return new UpdateTriggerDetailsRequest
            {
                HasDescription = description.HasValue,
                Description = Text(description, "description"),
                HasPriority = priority.HasValue,
                Priority = Number(priority, "priority"),
                HasJobDataMap = jobDataMap.HasValue,
                JobDataMap = Map(jobDataMap, options),
                HasCalendarName = calendarName.HasValue,
                CalendarName = Text(calendarName, "calendarName"),
                HasMisfireInstruction = misfireInstruction.HasValue,
                MisfireInstruction = Number(misfireInstruction, "misfireInstruction"),
                MisfireInstructionFamily = Text(Member(root, options, "MisfireInstructionFamily"), "misfireInstructionFamily"),
                HasPreferredNode = preferredNode.HasValue,
                PreferredNode = Text(preferredNode, "preferredNode"),
                PreferredNodeAuto = Member(root, options, "PreferredNodeAuto")?.ValueKind == JsonValueKind.True,
                HasExecutionGroup = executionGroup.HasValue,
                ExecutionGroup = Text(executionGroup, "executionGroup"),
                HasRetryPolicy = retryPolicy.HasValue,
                RetryPolicy = Text(retryPolicy, "retryPolicy")
            };
        }

        private static JsonElement? Member(JsonElement root, JsonSerializerOptions options, string name)
            => root.GetPropertyOrNull(options.GetPropertyName(name));

        /// <summary>
        /// A member that is absent, null or a string. A member of any other kind is refused here rather
        /// than by <see cref="JsonElement.GetString" />, whose <see cref="InvalidOperationException" />
        /// would leave the request as a <c>500</c> instead of the <c>400</c> a malformed body earns.
        /// </summary>
        private static string? Text(JsonElement? member, string name)
        {
            return member?.ValueKind switch
            {
                null or JsonValueKind.Null => null,
                JsonValueKind.String => member.Value.GetString(),
                _ => throw new JsonException($"'{name}' must be a string or null")
            };
        }

        private static int Number(JsonElement? member, string name)
        {
            return member?.ValueKind switch
            {
                null => 0,
                JsonValueKind.Number when member.Value.TryGetInt32(out int value) => value,
                _ => throw new JsonException($"'{name}' must be a whole number")
            };
        }

        private static Quartz.JobDataMap? Map(JsonElement? member, JsonSerializerOptions options)
        {
            return member?.ValueKind switch
            {
                null or JsonValueKind.Null => null,
                JsonValueKind.Object => member.Value.GetJobDataMap(options),
                _ => throw new JsonException("'jobDataMap' must be an object or null")
            };
        }

        public override void Write(Utf8JsonWriter writer, UpdateTriggerDetailsRequest value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (value.HasDescription)
            {
                writer.WriteString(options.GetPropertyName("Description"), value.Description);
            }

            if (value.HasPriority)
            {
                writer.WriteNumber(options.GetPropertyName("Priority"), value.Priority);
            }

            if (value.HasJobDataMap)
            {
                writer.WritePropertyName(options.GetPropertyName("JobDataMap"));
                if (value.JobDataMap is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    // Through the options' own JobDataMapConverter, which is what refuses a value the
                    // reading side could not accept - and through its metadata, so nothing here reflects.
                    JsonSerializer.Serialize(writer, value.JobDataMap, (JsonTypeInfo<Quartz.JobDataMap>) options.GetTypeInfo(typeof(Quartz.JobDataMap)));
                }
            }

            if (value.HasCalendarName)
            {
                writer.WriteString(options.GetPropertyName("CalendarName"), value.CalendarName);
            }

            if (value.HasMisfireInstruction)
            {
                writer.WriteNumber(options.GetPropertyName("MisfireInstruction"), value.MisfireInstruction);

                if (value.MisfireInstructionFamily is not null)
                {
                    writer.WriteString(options.GetPropertyName("MisfireInstructionFamily"), value.MisfireInstructionFamily);
                }
            }

            if (value.HasPreferredNode)
            {
                writer.WriteString(options.GetPropertyName("PreferredNode"), value.PreferredNode);
                writer.WriteBoolean(options.GetPropertyName("PreferredNodeAuto"), value.PreferredNodeAuto);
            }

            if (value.HasExecutionGroup)
            {
                writer.WriteString(options.GetPropertyName("ExecutionGroup"), value.ExecutionGroup);
            }

            if (value.HasRetryPolicy)
            {
                writer.WriteString(options.GetPropertyName("RetryPolicy"), value.RetryPolicy);
            }

            writer.WriteEndObject();
        }
    }
}
