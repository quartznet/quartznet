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

namespace Quartz.Impl;

/// <summary>
/// Folds one recorded execution into its trigger's <see cref="TriggerRunStatus" />, by <see cref="RunTally" />'s
/// rule, the one a job's status is folded by.
/// </summary>
/// <remarks>
/// <para>
/// The status is the firing trigger's: <see cref="ExecutionHistoryEntry.TriggerGroup" /> and
/// <see cref="ExecutionHistoryEntry.TriggerName" />. Which firings a store folds in is the store's to decide; the
/// shipped ones leave out a manual run and a firing after which the trigger is finalized.
/// </para>
/// <inheritdoc cref="RunTally" path="/remarks" />
/// </remarks>
internal static class TriggerRunStatusFold
{
    internal static TriggerRunStatus Apply(TriggerRunStatus? current, ExecutionHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RunTally tally = current is null ? default : new RunTally
        {
            LastFiredAtUtc = current.LastFiredAtUtc,
            LastResult = current.LastResult,
            LastDuration = current.LastDuration,
            LastSchedulerInstanceId = current.LastSchedulerInstanceId,
            LastEntryId = current.LastEntryId,
            LastSummary = current.LastSummary,
            LastSucceededAtUtc = current.LastSucceededAtUtc,
            LastFailedAtUtc = current.LastFailedAtUtc,
            LastFailureMessage = current.LastFailureMessage,
            ConsecutiveFailures = current.ConsecutiveFailures,
            RunCount = current.RunCount,
            FailureCount = current.FailureCount,
            FirstFiredAtUtc = current.FirstFiredAtUtc
        };

        tally.Fold(entry, first: current is null);

        return new TriggerRunStatus(
            current?.SchedulerName ?? entry.SchedulerName,
            current?.Trigger ?? new TriggerKey(entry.TriggerName, entry.TriggerGroup),
            tally.LastFiredAtUtc,
            tally.LastResult)
        {
            LastDuration = tally.LastDuration,
            LastSchedulerInstanceId = tally.LastSchedulerInstanceId,
            LastEntryId = tally.LastEntryId,
            LastSummary = tally.LastSummary,
            LastSucceededAtUtc = tally.LastSucceededAtUtc,
            LastFailedAtUtc = tally.LastFailedAtUtc,
            LastFailureMessage = tally.LastFailureMessage,
            ConsecutiveFailures = tally.ConsecutiveFailures,
            RunCount = tally.RunCount,
            FailureCount = tally.FailureCount,
            FirstFiredAtUtc = tally.FirstFiredAtUtc
        };
    }
}
