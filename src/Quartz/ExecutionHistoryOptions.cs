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

using Microsoft.Extensions.Options;

namespace Quartz;

/// <summary>
/// The bounds the shipped execution history is kept under.
/// </summary>
/// <remarks>
/// Sized by you: an age per result (<see cref="Retention" />, <see cref="RetentionByResult" />,
/// <see cref="MisfireRetention" />), a cap per job (<see cref="MaxEntriesPerJob" />), and
/// <see cref="MaxEntriesPerScheduler" /> as the backstop on the whole feed. Keep failures for a month and
/// successes for a day, say, and cap a job that runs every second so it cannot crowd out the rest.
/// </remarks>
public sealed class ExecutionHistoryOptions
{
    /// <summary>
    /// How far back the history reaches: 24 hours by default. The age of every result
    /// <see cref="RetentionByResult" /> does not name.
    /// </summary>
    /// <remarks>
    /// Applied when the history is read as well as when it is written, because a scheduler that has
    /// stopped running jobs never writes again — and it is exactly that scheduler whose page would
    /// otherwise keep showing executions from days ago.
    /// </remarks>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How far back the history keeps executions with a given result, overriding
    /// <see cref="Retention" /> for that result. Empty by default.
    /// </summary>
    /// <remarks>
    /// Matched against <see cref="ExecutionHistoryEntry.EffectiveResult" />. Every age must be positive.
    /// </remarks>
    /// <example>
    /// <code>
    /// options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(30);
    /// options.RetentionByResult[JobRunResult.Skipped] = TimeSpan.FromHours(1);
    /// </code>
    /// </example>
    public Dictionary<JobRunResult, TimeSpan> RetentionByResult { get; } = [];

    /// <summary>
    /// How far back the misfire feed reaches, or <see langword="null" />, the default, for
    /// <see cref="Retention" />.
    /// </summary>
    public TimeSpan? MisfireRetention { get; set; }

    /// <summary>
    /// The most executions kept per job, failures not counted: <c>0</c>, the default, keeps no per-job cap.
    /// </summary>
    /// <remarks>
    /// The oldest go first. An execution whose <see cref="ExecutionHistoryEntry.EffectiveResult" /> is
    /// <see cref="JobRunResult.Failed" /> is exempt: it neither counts towards the cap nor is removed by it,
    /// so a job that runs often cannot push its own failures out.
    /// </remarks>
    public int MaxEntriesPerJob { get; set; }

    /// <summary>
    /// The most rows kept per scheduler, in each feed: 2000 by default. <c>0</c> records nothing.
    /// </summary>
    /// <remarks>
    /// The backstop: the oldest rows go first, whatever their result. The in-memory history also keeps at
    /// most this many <see cref="JobRunStatus" /> per scheduler, dropping the one run longest ago.
    /// <para>
    /// Zero is the opt-out, and it is an opt-out rather than a limit that keeps nothing: the recorder
    /// reads it and stops recording, so a process that does not want the history does not pay for the
    /// rows it would immediately discard. It is how a worker that maps the HTTP API — which turns
    /// recording on by default — turns it back off.
    /// </para>
    /// </remarks>
    public int MaxEntriesPerScheduler { get; set; } = 2000;
}

/// <summary>
/// Validates <see cref="ExecutionHistoryOptions" />.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}" /> rather than an <c>AddOptions().Validate(lambda)</c>, so
/// every Quartz configuration mistake produces one exception type from one place.
/// </remarks>
internal sealed class ExecutionHistoryOptionsValidator : IValidateOptions<ExecutionHistoryOptions>
{
    public ValidateOptionsResult Validate(string? name, ExecutionHistoryOptions options)
    {
        if (options.Retention <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ExecutionHistoryOptions.Retention)} must be positive, was {options.Retention}: a zero or "
                + "negative window would forget every execution the moment it was recorded. To keep no history at all, "
                + $"set {nameof(ExecutionHistoryOptions.MaxEntriesPerScheduler)} to 0, which stops the recorder.");
        }

        if (options.MaxEntriesPerScheduler < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ExecutionHistoryOptions.MaxEntriesPerScheduler)} must not be negative, was "
                + $"{options.MaxEntriesPerScheduler}. Use 0 to record nothing.");
        }

        foreach (KeyValuePair<JobRunResult, TimeSpan> tier in options.RetentionByResult)
        {
            if (tier.Value <= TimeSpan.Zero)
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(ExecutionHistoryOptions.RetentionByResult)}[{tier.Key}] must be positive, was {tier.Value}: "
                    + $"a zero or negative age would forget every {tier.Key} execution the moment it was recorded.");
            }
        }

        if (options.MisfireRetention is { } misfireRetention && misfireRetention <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ExecutionHistoryOptions.MisfireRetention)} must be positive when set, was {misfireRetention}. "
                + $"Leave it null to keep misfires for {nameof(ExecutionHistoryOptions.Retention)}.");
        }

        if (options.MaxEntriesPerJob < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ExecutionHistoryOptions.MaxEntriesPerJob)} must not be negative, was "
                + $"{options.MaxEntriesPerJob}. Use 0 for no per-job cap.");
        }

        return ValidateOptionsResult.Success;
    }
}
