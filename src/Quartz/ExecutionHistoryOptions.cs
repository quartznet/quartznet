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
/// Two bounds, because either alone leaves a case unanswered: the count bound lets a quiet scheduler go
/// on showing executions from an arbitrary distance in the past, and the age bound lets a busy one grow
/// the history without limit inside the window. What this describes is an operator's recent view rather
/// than an audit log — for an audit log, register an
/// <see cref="Extensibility.IExecutionHistoryStore" /> of your own that writes somewhere that survives a
/// restart.
/// </remarks>
public sealed class ExecutionHistoryOptions
{
    /// <summary>
    /// How far back the history reaches: 24 hours by default.
    /// </summary>
    /// <remarks>
    /// Applied when the history is read as well as when it is written, because a scheduler that has
    /// stopped running jobs never writes again — and it is exactly that scheduler whose page would
    /// otherwise keep showing executions from days ago.
    /// </remarks>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The most rows kept per scheduler, in each feed: 2000 by default. <c>0</c> records nothing.
    /// </summary>
    /// <remarks>
    /// Zero is the opt-out, and it is an opt-out rather than a limit that keeps nothing: the recorder
    /// reads it and stops recording, so a process that does not want the history does not pay for the
    /// rows it would immediately discard. It is how a worker that maps the HTTP API — which turns
    /// recording on by default — turns it back off.
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

        return ValidateOptionsResult.Success;
    }
}
