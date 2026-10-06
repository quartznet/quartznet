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

using System.Globalization;
using System.Text;

namespace Quartz.Dashboard.Services;

/// <summary>
/// What <em>Run again</em> passes back to <see cref="IQuartzApiClient.TriggerJob" />, and how the pages
/// say whether it carries the run's input.
/// </summary>
/// <remarks>
/// One place, so the History page and an execution's page fire a run again the same way and say so in the
/// same words.
/// </remarks>
internal static class RunAgainInput
{
    /// <summary>
    /// The run as recorded, its input included: the row itself when it carries one or cannot be asked
    /// for alone, and otherwise the row read by its key.
    /// </summary>
    /// <remarks>
    /// A listing may leave the input out, as the persistent store and the HTTP API do, so the row is read
    /// again when Run again is pressed. A row gone since the listing was read, or a target that serves no
    /// single executions, leaves the run with no input: it is fired as it was before 4.4.
    /// </remarks>
    public static async Task<DashboardHistoryEntry> Read(IQuartzApiClient api, string schedulerName, DashboardHistoryEntry listed)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(listed);

        if (listed.Input is not null || listed.InputTooLarge || listed.EntryId is not { Length: > 0 } entryId)
        {
            return listed;
        }

        try
        {
            return await api.GetExecution(schedulerName, entryId).ConfigureAwait(false) ?? listed;
        }
        catch (NotSupportedException)
        {
            return listed;
        }
    }

    /// <summary>
    /// The map that carries the run's input under <see cref="SchedulerConstants.JobInput" />, or
    /// <see langword="null" /> when the history recorded none.
    /// </summary>
    /// <remarks>
    /// The input is the string the scheduler stored, so it is passed on as it is: the scheduler leaves a
    /// string input alone, and the job reads it back as it read it the first time.
    /// </remarks>
    public static JobDataMap? DataFor(DashboardHistoryEntry run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Input is not { } input)
        {
            return null;
        }

        return new JobDataMap { [SchedulerConstants.JobInput] = input };
    }

    /// <summary>
    /// "with the original input" or "without input".
    /// </summary>
    public static string Label(DashboardHistoryEntry run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.Input is not null ? "with the original input" : "without input";
    }

    /// <summary>
    /// <see cref="Label" />, with the reason when the input was too large to keep.
    /// </summary>
    public static string Describe(DashboardHistoryEntry run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.Input is null && run.InputTooLarge
            ? "without input: the run's input was too large for the history to keep"
            : Label(run);
    }

    /// <summary>
    /// What an execution's page says of the run's input, in its <em>Input</em> row.
    /// </summary>
    public static string State(DashboardHistoryEntry run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Input is { } input)
        {
            return "Recorded, " + Encoding.UTF8.GetByteCount(input).ToString(CultureInfo.InvariantCulture) + " bytes";
        }

        return run.InputTooLarge
            ? "Not recorded: over the history's input cap, ExecutionHistoryOptions.MaxInputBytes"
            : "None recorded. The history keeps a run's input with ExecutionHistoryOptions.RecordInput.";
    }

    /// <summary>
    /// What Run again says of a run whose job the scheduler no longer stores.
    /// </summary>
    /// <param name="target">The job's key, as the pages write it.</param>
    public static string JobGone(string target)
    {
        return "The job " + target + " is no longer stored: it was not durable, and it was deleted with its last trigger. "
               + "Store it durably (StoreDurably()) to run it again.";
    }

    /// <summary>
    /// Whether the scheduler no longer stores <paramref name="job" />.
    /// </summary>
    /// <remarks>
    /// Asked through <see cref="IQuartzApiClient.GetJobDetail" />, which raises
    /// <see cref="KeyNotFoundException" /> for a job the scheduler does not hold, rather than read from the
    /// text of a failed trigger. A question that cannot be answered is not an answer, so any other failure
    /// says the job is there and leaves the page as it was.
    /// </remarks>
    public static async ValueTask<bool> IsJobGone(IQuartzApiClient api, string schedulerName, JobKeyDto job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);

        try
        {
            await api.GetJobDetail(schedulerName, job, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (KeyNotFoundException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a failed Run again failed because the scheduler no longer stores the job, so the pages say
    /// <see cref="JobGone" /> rather than the failure's own message.
    /// </summary>
    /// <remarks>
    /// The store refuses a trigger for a missing job in words about the trigger, which an operator who
    /// pressed a button on a run cannot act on. A failure that is itself a <see cref="KeyNotFoundException" />
    /// is the scheduler gone, not the job, so the job is not asked about.
    /// </remarks>
    public static async ValueTask<bool> FailedForAJobGone(
        IQuartzApiClient api,
        string schedulerName,
        JobKeyDto job,
        Exception failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure is not KeyNotFoundException
               && await IsJobGone(api, schedulerName, job, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What the History page's button says it will do, before the row's input has been read.
    /// </summary>
    public static string Hint(DashboardHistoryEntry listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        if (listed.InputTooLarge)
        {
            return "Fires the job again without input: the run's input was too large for the history to keep";
        }

        return listed.Input is not null
            ? "Fires the job again with the original input"
            : "Fires the job again, with the run's input when the history recorded one";
    }
}
