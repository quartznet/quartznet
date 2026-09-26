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
/// How much of what a job logs while it runs <c>UseExecutionLogCapture()</c> keeps with the execution's
/// history row.
/// </summary>
/// <remarks>
/// <para>
/// Two bounds, applied together: whichever is reached first drops the oldest line, and the kept text
/// then opens with a line saying how many were dropped. The newest lines are the ones kept, because the
/// end of a run is where a failure says what went wrong.
/// </para>
/// <para>
/// What is kept lives as long as its history row. The in-memory history keeps up to
/// <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" /> rows, so its memory is bounded by that
/// times <see cref="MaxBytes" />; the persistent store keeps the text in
/// <c>QRTZ_EXECUTION_HISTORY.EXECUTION_LOG</c>.
/// </para>
/// </remarks>
public sealed class ExecutionLogCaptureOptions
{
    /// <summary>
    /// The most log entries kept per execution: 200 by default. One entry is one log call, with the
    /// exception it carried.
    /// </summary>
    public int MaxLines { get; set; } = 200;

    /// <summary>
    /// The most UTF-8 bytes kept per execution: 16 KB by default. An entry longer than this on its own is
    /// cut to fit.
    /// </summary>
    public int MaxBytes { get; set; } = 16 * 1024;
}

/// <summary>
/// Validates <see cref="ExecutionLogCaptureOptions" />, as <c>ExecutionHistoryOptionsValidator</c> does its
/// own: one exception type from one place, when the scheduler is built.
/// </summary>
internal sealed class ExecutionLogCaptureOptionsValidator : IValidateOptions<ExecutionLogCaptureOptions>
{
    /// <summary>
    /// The least <see cref="ExecutionLogCaptureOptions.MaxBytes" /> accepted: room for a line and its
    /// timestamp, so that a bound meant as "a little" does not keep nothing at all.
    /// </summary>
    internal const int MinimumMaxBytes = 256;

    public ValidateOptionsResult Validate(string? name, ExecutionLogCaptureOptions options)
    {
        if (options.MaxLines < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ExecutionLogCaptureOptions.MaxLines)} must be at least 1, was {options.MaxLines}. "
                + "To capture nothing, do not call UseExecutionLogCapture().");
        }

        if (options.MaxBytes < MinimumMaxBytes)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ExecutionLogCaptureOptions.MaxBytes)} must be at least {MinimumMaxBytes}, was {options.MaxBytes}: "
                + "less leaves no room for a single line and its timestamp.");
        }

        return ValidateOptionsResult.Success;
    }
}
