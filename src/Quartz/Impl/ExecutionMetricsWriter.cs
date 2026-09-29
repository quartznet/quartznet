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
using System.Globalization;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Quartz.Core;

namespace Quartz.Impl;

/// <summary>
/// Writes a job's reported metrics as the one JSON object its history row keeps.
/// </summary>
/// <remarks>
/// <para>
/// A type switch over <see cref="Utf8JsonWriter" /> rather than a serializer: a metric is a value of a
/// type the job chose, and serializing that by reflection is what the trim and AOT analyzers refuse.
/// Anything the switch does not know is written as its invariant-culture text.
/// </para>
/// <para>
/// The writer's default encoder escapes every character outside ASCII, so the output's length in
/// characters is its length in UTF-8 bytes: a column that counts bytes holds whatever fits the limit.
/// </para>
/// </remarks>
internal static class ExecutionMetricsWriter
{
    /// <summary>
    /// The metrics as a JSON object, or <see langword="null" /> when there are none or when they come to
    /// more than <see cref="JobRunReport.MaxMetricsLength" /> characters — which is logged once, as event
    /// <c>1059</c>.
    /// </summary>
    /// <param name="metrics">What the job reported.</param>
    /// <param name="logger">Where a dropped object is reported.</param>
    /// <param name="jobKey">The job that reported them, for the log event.</param>
    internal static string? Write(IReadOnlyDictionary<string, object?>? metrics, ILogger logger, JobKey jobKey)
    {
        if (metrics is null || metrics.Count == 0)
        {
            return null;
        }

        ArrayBufferWriter<byte> buffer = new(256);
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, object?> metric in metrics)
            {
                writer.WritePropertyName(metric.Key);
                WriteValue(writer, metric.Value);

                // Checked as it grows, so a job that reports a huge object costs no more than the limit.
                if (writer.BytesCommitted + writer.BytesPending > JobRunReport.MaxMetricsLength)
                {
                    logger.JobRunMetricsDropped(jobKey, JobRunReport.MaxMetricsLength);
                    return null;
                }
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        if (buffer.WrittenCount > JobRunReport.MaxMetricsLength)
        {
            logger.JobRunMetricsDropped(jobKey, JobRunReport.MaxMetricsLength);
            return null;
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case short number:
                writer.WriteNumberValue(number);
                break;
            case byte number:
                writer.WriteNumberValue(number);
                break;
            case sbyte number:
                writer.WriteNumberValue(number);
                break;
            case ushort number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case double number:
                // JSON has no NaN or infinity, and Utf8JsonWriter refuses to write one as a number.
                writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
                break;
            case float number when float.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case float number:
                writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case DateTimeOffset instant:
                writer.WriteStringValue(instant.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTime instant:
                writer.WriteStringValue(instant.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeSpan span:
                writer.WriteStringValue(span.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Guid id:
                writer.WriteStringValue(id);
                break;
            case Enum member:
                writer.WriteStringValue(member.ToString());
                break;
            case IFormattable formattable:
                writer.WriteStringValue(formattable.ToString(format: null, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
