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

namespace Quartz.Dashboard.Services;

/// <summary>
/// How the pages say what a run achieved, why a firing did not happen, and what a run measured.
/// </summary>
/// <remarks>
/// One place, so the History page, an execution's page, a job's page and the Jobs listing say the same
/// thing in the same words.
/// </remarks>
internal static class RunResultDisplay
{
    /// <summary>
    /// The results the History page offers as a filter, in the order it offers them.
    /// </summary>
    public static readonly JobRunResult[] Offered = [JobRunResult.Succeeded, JobRunResult.Skipped, JobRunResult.Failed, JobRunResult.Cancelled];

    /// <summary>
    /// A row's result, with a failure the trigger is going to retry told apart from one it gave up on.
    /// </summary>
    /// <remarks>
    /// A page that called both "Failed" made a job under a retry policy look several times as broken as it
    /// was. A row written before 4.4 answers through its success.
    /// </remarks>
    public static string Label(DashboardHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.EffectiveResult == JobRunResult.Failed && entry.RetryScheduled
            ? "Failed (retrying)"
            : Label(entry.EffectiveResult);
    }

    public static string Label(JobRunResult result) => result switch
    {
        JobRunResult.Succeeded => "Succeeded",
        JobRunResult.Failed => "Failed",
        JobRunResult.Cancelled => "Cancelled",
        JobRunResult.Skipped => "Skipped",

        // A result a newer host appended, shown as what it is rather than as one it is not.
        _ => result.ToString()
    };

    public static string Label(MisfireReason reason) => reason switch
    {
        MisfireReason.Overlap => "Overlap",
        MisfireReason.Vetoed => "Vetoed",
        _ => "Misfire"
    };

    /// <summary>
    /// A run's metrics as name and text pairs, in the order the job reported them; empty when there are
    /// none or the text is not a JSON object.
    /// </summary>
    /// <remarks>
    /// A string shows without its quotes; any other value shows as its JSON.
    /// </remarks>
    public static List<KeyValuePair<string, string>> Metrics(string? metricsJson)
    {
        List<KeyValuePair<string, string>> metrics = [];
        if (string.IsNullOrWhiteSpace(metricsJson))
        {
            return metrics;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(metricsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return metrics;
            }

            foreach (JsonProperty metric in document.RootElement.EnumerateObject())
            {
                string text = metric.Value.ValueKind == JsonValueKind.String
                    ? metric.Value.GetString() ?? string.Empty
                    : metric.Value.GetRawText();

                metrics.Add(new KeyValuePair<string, string>(metric.Name, text));
            }
        }
        catch (JsonException)
        {
            // A store of an application's own may keep anything in the column; its row still shows.
            metrics.Clear();
        }

        return metrics;
    }
}
