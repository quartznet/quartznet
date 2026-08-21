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

    /// <summary>
    /// Produces <paramref name="trigger"/> with its cron expression replaced by
    /// <paramref name="cronExpression"/>, or <see langword="false"/> when the trigger cannot be sent
    /// back to the scheduler at all.
    /// </summary>
    public static bool TryWithCronExpression(JsonElement trigger, string cronExpression, out JsonElement payload)
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
