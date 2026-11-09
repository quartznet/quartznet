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

using System.Buffers;
using System.Text.Json;

namespace Quartz.Dashboard.Components.Shared;

/// <summary>
/// Builds the trigger payloads the dashboard posts back to the scheduler by editing the JSON the API
/// returned, rather than re-assembling a trigger field by field.
/// </summary>
/// <remarks>
/// Re-assembly silently dropped whatever it did not list. A null calendar name came back as an empty
/// string, which every job store reads as a calendar it then cannot find, so the trigger stopped
/// firing (#3294); the node pin was dropped altogether; and the trigger type was hardcoded, so a
/// custom cron trigger was rewritten as a plain one. Editing the serialized trigger in place keeps
/// every field the Quartz converters wrote, including ones added later.
/// </remarks>
internal static class TriggerPayloadBuilder
{
    private const string TriggerTypeProperty = "triggerType";
    private const string CronExpressionProperty = "cronExpressionString";
    private const string NextFireTimeProperty = "nextFireTimeUtc";
    private const string StartTimeProperty = "startTimeUtc";
    private const string EndTimeProperty = "endTimeUtc";

    /// <summary>
    /// Produces <paramref name="trigger"/> with its cron expression replaced by
    /// <paramref name="cronExpression"/> and a start that has passed moved to the whole second after
    /// <paramref name="now"/>, or <see langword="false"/> when the trigger cannot be sent back to the
    /// scheduler at all.
    /// </summary>
    /// <remarks>
    /// <c>RescheduleJob</c> computes the first fire as the first match after the trigger's start, less a
    /// second. The start is when the trigger was first scheduled, so kept as it is it gives a first fire in
    /// the past, which the scheduler treats as a misfire: the trigger skips its regular fires and then runs
    /// once off its schedule (#4187). The start moves to the whole second after now rather than to now, which
    /// can land on the current second, one the trigger may just have fired in. A start still ahead stays as
    /// it is, and so does the start of a trigger that ends before then, since a start after the end is refused.
    /// </remarks>
    public static bool TryWithCronExpression(JsonElement trigger, string cronExpression, DateTimeOffset now, out JsonElement payload)
    {
        if (trigger.ValueKind != JsonValueKind.Object || !HasProperty(trigger, TriggerTypeProperty))
        {
            // A trigger type the Quartz converters do not know is serialized by the API client's
            // reflection fallback, which omits the discriminator. Such a payload cannot be read
            // back, so refuse it rather than post something that will not deserialize - or, as the
            // hand-built payload did, quietly reschedule it as a different trigger type.
            payload = default;
            return false;
        }

        DateTimeOffset? movedStart = MovedStart(trigger, now);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            bool cronWritten = false;
            foreach (JsonProperty property in trigger.EnumerateObject())
            {
                if (Matches(property.Name, CronExpressionProperty))
                {
                    writer.WriteString(property.Name, cronExpression);
                    cronWritten = true;
                }
                else if (movedStart is { } start && Matches(property.Name, StartTimeProperty))
                {
                    writer.WriteString(property.Name, start);
                }
                else if (Matches(property.Name, NextFireTimeProperty))
                {
                    // The schedule just changed, so the stored next fire time belongs to the old
                    // expression, and RescheduleJob honours a non-null one verbatim. Clearing it
                    // lets the first fire be computed from the expression the user just entered.
                    writer.WriteNull(property.Name);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            if (!cronWritten)
            {
                writer.WriteString(CronExpressionProperty, cronExpression);
            }

            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        payload = document.RootElement.Clone();
        return true;
    }

    /// <summary>
    /// The start a reschedule at <paramref name="now"/> should give <paramref name="trigger"/>, or
    /// <see langword="null"/> to keep the one it has.
    /// </summary>
    private static DateTimeOffset? MovedStart(JsonElement trigger, DateTimeOffset now)
    {
        DateTimeOffset next = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero).AddSeconds(1);
        if (!TryGetTime(trigger, StartTimeProperty, out DateTimeOffset start) || start >= next)
        {
            return null;
        }

        if (TryGetTime(trigger, EndTimeProperty, out DateTimeOffset end) && end < next)
        {
            return null;
        }

        return next;
    }

    private static bool TryGetTime(JsonElement element, string name, out DateTimeOffset time)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Matches(property.Name, name) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.TryGetDateTimeOffset(out time);
            }
        }

        time = default;
        return false;
    }

    private static bool HasProperty(JsonElement element, string name)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Matches(property.Name, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The serialized trigger is camelCased today, but the page reads it through
    /// <see cref="DisplayValueHelper"/>, which tolerates either casing. This does the same so the
    /// two cannot disagree about which trigger they are looking at.
    /// </summary>
    private static bool Matches(string propertyName, string name)
        => string.Equals(propertyName, name, StringComparison.OrdinalIgnoreCase);
}
