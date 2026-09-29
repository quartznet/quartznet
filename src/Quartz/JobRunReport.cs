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

namespace Quartz;

/// <summary>
/// A job's report of what its run achieved: set it as <see cref="IJobExecutionContext.Result" />.
/// </summary>
/// <remarks>
/// <code>
/// context.Result = JobRunReport.Skipped("no stale reservations").With("released", 0);
/// </code>
/// <para>
/// <inheritdoc cref="IJobRunReport" path="/remarks/para[2]" />
/// </para>
/// </remarks>
public sealed record JobRunReport : IJobRunReport
{
    /// <summary>
    /// The longest <see cref="Summary" /> the history keeps: 1,000 characters. A longer one is cut,
    /// never between the two halves of a surrogate pair.
    /// </summary>
    public const int MaxSummaryLength = 1000;

    /// <summary>
    /// The longest the <see cref="Metrics" /> may be as JSON: 4,000 characters. A larger object is
    /// dropped whole, and log event <c>1059</c> says so.
    /// </summary>
    /// <remarks>
    /// The JSON escapes every character outside ASCII, so the limit is also 4,000 bytes.
    /// </remarks>
    public const int MaxMetricsLength = 4000;

    /// <inheritdoc />
    public JobRunResult Result { get; init; }

    /// <inheritdoc />
    public string? Summary { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// A value is written as a JSON string, number, boolean or null: numbers stay numbers (a non-finite
    /// <see cref="double" /> or <see cref="float" /> is a string), dates are round-trip (<c>"O"</c>)
    /// strings, a <see cref="TimeSpan" /> is its constant (<c>"c"</c>) form, an enum is its name, and
    /// anything else is its invariant-culture text.
    /// </remarks>
    public IReadOnlyDictionary<string, object?>? Metrics { get; init; }

    /// <summary>
    /// A report that the run did its work.
    /// </summary>
    /// <param name="summary">One line for a reader of the history, or <see langword="null" />.</param>
    public static JobRunReport Succeeded(string? summary = null) => new() { Result = JobRunResult.Succeeded, Summary = summary };

    /// <summary>
    /// A report that the run found nothing to do. It counts as a success.
    /// </summary>
    /// <param name="summary">One line for a reader of the history, or <see langword="null" />.</param>
    public static JobRunReport Skipped(string? summary = null) => new() { Result = JobRunResult.Skipped, Summary = summary };

    /// <summary>
    /// A report that the run failed, recorded in the history only.
    /// </summary>
    /// <remarks>
    /// The scheduler does not retry it, run <c>OnFailure</c> continuations for it, or count it against a
    /// retry policy. To have the scheduler act on a failure, throw.
    /// </remarks>
    /// <param name="summary">What went wrong, for a reader of the history, or <see langword="null" />.</param>
    public static JobRunReport Failed(string? summary = null) => new() { Result = JobRunResult.Failed, Summary = summary };

    /// <summary>
    /// A copy of this report with one more metric. A name already present takes the new value.
    /// </summary>
    /// <param name="name">The metric's name, the key in the recorded JSON object.</param>
    /// <param name="value">What was measured.</param>
    /// <returns>A new report; this one is unchanged.</returns>
    public JobRunReport With(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        Dictionary<string, object?> metrics = Metrics is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(Metrics, StringComparer.Ordinal);

        metrics[name] = value;
        return this with { Metrics = metrics };
    }
}
