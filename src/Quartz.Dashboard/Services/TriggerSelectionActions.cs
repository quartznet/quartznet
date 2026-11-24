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

using Quartz.Dashboard.Components.Shared;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Pauses, resumes or unschedules a selection of triggers in one key-set call, and reports it: a toast for the
/// count, the keys it did not reach and why, and an Action Log entry per key.
/// </summary>
/// <remarks>
/// The Triggers listing and the Agenda both act on a selection, and both report it the same way. Each entry
/// has the shape the single-row action records, so "who paused this trigger" finds a bulk pause under the
/// trigger's own key, with the reason it was given.
/// </remarks>
internal sealed class TriggerSelectionActions
{
    private readonly IQuartzApiClient api;
    private readonly DashboardActionLog actionLog;
    private readonly ToastService toasts;

    public TriggerSelectionActions(IQuartzApiClient api, DashboardActionLog actionLog, ToastService toasts)
    {
        this.api = api;
        this.actionLog = actionLog;
        this.toasts = toasts;
    }

    /// <summary>
    /// A count of triggers in words, with the noun that agrees with it: <c>1 trigger</c>, <c>2 selected triggers</c>.
    /// </summary>
    /// <param name="count">How many.</param>
    /// <param name="adjective">A word that comes between the count and the noun, or <see langword="null" />.</param>
    public static string Triggers(int count, string? adjective = null)
    {
        string number = count.ToString("N0", CultureInfo.InvariantCulture);
        string noun = count == 1 ? "trigger" : "triggers";
        return adjective is null ? number + " " + noun : number + " " + adjective + " " + noun;
    }

    /// <summary>
    /// The keys in the order a listing shows them: group, then name.
    /// </summary>
    public static List<TriggerKeyDto> Sorted(IEnumerable<TriggerKeyDto> keys)
    {
        List<TriggerKeyDto> sorted = [.. keys];
        sorted.Sort(static (left, right) =>
        {
            int byGroup = string.CompareOrdinal(left.Group, right.Group);
            return byGroup != 0 ? byGroup : string.CompareOrdinal(left.Name, right.Name);
        });
        return sorted;
    }

    /// <summary>
    /// Pauses the selection, with the one reason the prompt asked for once.
    /// </summary>
    /// <remarks>
    /// One call whatever the prompt was told: over HTTP one <c>…/triggers/keys/pause</c> request, carrying
    /// the reason when there is one. A pause that says nothing is the reasonless key-set pause.
    /// </remarks>
    public Task<TriggerSelectionResult> Pause(string schedulerName, IReadOnlyCollection<TriggerKeyDto> keys, string? reason)
    {
        PauseDetails details = actionLog.PauseDetailsFor(reason);
        return Execute(
            schedulerName,
            keys,
            "PauseTrigger",
            "Paused",
            "not paused - it no longer exists or was already paused",
            (name, selection) => api.PauseTriggersWith(name, selection, details),
            DashboardActionLog.PauseNote(details));
    }

    /// <summary>
    /// Resumes the selection in one call.
    /// </summary>
    public Task<TriggerSelectionResult> Resume(string schedulerName, IReadOnlyCollection<TriggerKeyDto> keys)
    {
        return Execute(
            schedulerName,
            keys,
            "ResumeTrigger",
            "Resumed",
            "not resumed - it no longer exists or was not paused",
            (name, selection) => api.ResumeTriggers(name, selection));
    }

    /// <summary>
    /// Unschedules the selection in one call.
    /// </summary>
    public Task<TriggerSelectionResult> Unschedule(string schedulerName, IReadOnlyCollection<TriggerKeyDto> keys)
    {
        return Execute(
            schedulerName,
            keys,
            "UnscheduleTrigger",
            "Unscheduled",
            "not unscheduled - it no longer exists",
            (name, selection) => api.UnscheduleJobs(name, selection));
    }

    private async Task<TriggerSelectionResult> Execute(
        string schedulerName,
        IReadOnlyCollection<TriggerKeyDto> selection,
        string actionName,
        string verb,
        string missedReason,
        Func<string, List<TriggerKeyDto>, ValueTask<List<TriggerKeyDto>>> operation,
        string? note = null)
    {
        List<TriggerKeyDto> keys = Sorted(selection);
        string batch = DashboardActionLog.MessageOf("one of " + keys.Count.ToString(CultureInfo.InvariantCulture) + " selected", note)!;

        HashSet<TriggerKeyDto> applied;
        Exception? failure = null;
        try
        {
            applied = [.. await operation(schedulerName, keys).ConfigureAwait(true)];
        }
        catch (Exception ex)
        {
            // The key-set call is one request and one store transaction, so a refusal is the whole
            // batch's: nothing it names was applied.
            applied = [];
            failure = ex;
        }

        List<(TriggerKeyDto Key, string Reason)> misses = [];
        foreach (TriggerKeyDto key in keys)
        {
            string target = DisplayValueHelper.FormatKey(key.Group, key.Name);
            if (applied.Contains(key))
            {
                actionLog.Record(schedulerName, actionName, target, succeeded: true, batch);
            }
            else if (failure is not null)
            {
                misses.Add((key, failure.Message));
                actionLog.Failed(schedulerName, actionName, target, failure, batch);
            }
            else
            {
                misses.Add((key, missedReason));
                actionLog.Record(schedulerName, actionName, target, succeeded: false, batch + "; " + missedReason);
            }
        }

        string summary = verb + " " + applied.Count.ToString(CultureInfo.InvariantCulture) + " of "
            + Triggers(keys.Count, "selected") + ".";

        if (misses.Count == 0)
        {
            toasts.Success(summary);
        }
        else if (failure is not null)
        {
            toasts.Error(summary + " " + keys.Count.ToString(CultureInfo.InvariantCulture) + " failed.");
        }
        else
        {
            toasts.Info(summary);
        }

        return new TriggerSelectionResult(summary, misses);
    }
}

/// <summary>
/// What a selection action did: the summary its toast said, and the keys it did not reach with the reason.
/// </summary>
/// <remarks>
/// A page keeps it on screen while <see cref="Misses" /> holds anything: a toast fades before a reader can copy
/// the keys out of it, and the keys a batch did not reach are the ones somebody has to go and look at.
/// </remarks>
internal sealed record TriggerSelectionResult(string Summary, List<(TriggerKeyDto Key, string Reason)> Misses);
