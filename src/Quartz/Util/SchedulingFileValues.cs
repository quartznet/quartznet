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

namespace Quartz.Util;

/// <summary>
/// The trigger settings a scheduling file states as a string, read the same way whoever read the file.
/// </summary>
/// <remarks>
/// Three readers declare triggers: the <c>Quartz:Schedule</c> section of <c>appsettings.json</c>, a
/// standalone <c>quartz_jobs.json</c>, and <c>quartz_jobs.xml</c>. A file in any of them declaring the
/// same trigger has to produce the same trigger, and a value whose meaning is decided in three parsers
/// has three meanings the first time one of them is edited. So they all come here, and each hands in
/// the phrase it names a trigger by so its own diagnostics read as they always did.
/// </remarks>
internal static class SchedulingFileValues
{
    /// <summary>
    /// Reads a trigger's retry policy from its stored form, for example <c>fixed;3;00:00:30</c>.
    /// </summary>
    /// <param name="value">The value the file stated, or <see langword="null" /> when it stated none.</param>
    /// <param name="trigger">How the reader names the trigger in a diagnostic, for example <c>Trigger 'nightly'</c>.</param>
    /// <exception cref="SchedulerConfigException">
    /// The file stated something that is not a retry policy. Refused as the file is read, rather than
    /// scheduling a trigger that silently never retries.
    /// </exception>
    internal static RetryPolicy? ReadRetryPolicy(string? value, string trigger)
    {
        if (value is null)
        {
            return null;
        }

        if (!RetryPolicy.TryParse(value, out RetryPolicy? policy))
        {
            throw new SchedulerConfigException($"{trigger}: '{value}' is not a retry policy.");
        }

        return policy;
    }

    /// <summary>
    /// Reads a trigger's preferred node: a scheduler instance id, <c>*</c> for an automatic pin, or
    /// nothing at all.
    /// </summary>
    /// <param name="value">The value the file stated, or <see langword="null" /> when it stated none.</param>
    /// <param name="trigger">How the reader names the trigger in a diagnostic, for example <c>Trigger 'nightly'</c>.</param>
    /// <remarks>
    /// A file that states no node leaves the trigger unpinned, which is also what a trigger built with
    /// no <c>WithPreferredNode</c> gets — so re-reading a file clears a pin that was set some other way,
    /// exactly as it clears an execution group or a retry policy.
    /// </remarks>
    /// <exception cref="SchedulerConfigException">
    /// The file stated a name the pinning protocol reserves. Refused as the file is read, rather than
    /// scheduling a trigger pinned to a node that cannot exist.
    /// </exception>
    internal static PreferredNode ReadPreferredNode(string? value, string trigger)
    {
        if (value is null)
        {
            return PreferredNode.None;
        }

        if (value.Trim() == PreferredNode.AutoSentinel)
        {
            return PreferredNode.Auto;
        }

        try
        {
            return PreferredNode.For(value);
        }
        catch (ArgumentException exception)
        {
            throw new SchedulerConfigException(
                $"{trigger}: '{value}' is not a preferred node - the pinning protocol reserves that name. "
                + "State a scheduler instance id, '*' to pin the trigger to whichever node fires it first, "
                + "or nothing at all to leave it unpinned.",
                exception);
        }
    }

    /// <summary>
    /// Reads the continuation a file declares: the trigger whose firing this one waits for, and the
    /// outcomes of it that release the wait.
    /// </summary>
    /// <param name="parentName">The parent trigger's name, or <see langword="null" /> when the file names none.</param>
    /// <param name="parentGroup">The parent trigger's group, or <see langword="null" /> for the default group.</param>
    /// <param name="condition">
    /// The outcomes that release the wait, named and joined with <c>|</c> — for example
    /// <c>OnFailure|OnCancellation</c> — or <see langword="null" /> for <c>OnSuccess</c>, which is what
    /// <see cref="Continuation.After" /> assumes.
    /// </param>
    /// <param name="trigger">How the reader names the trigger in a diagnostic, for example <c>Trigger 'nightly'</c>.</param>
    /// <remarks>
    /// The parent is named rather than resolved: a continuation carries a <see cref="TriggerKey" />, and
    /// nothing looks the parent up, so a file may declare the parent after the trigger that waits for it
    /// — or not at all, when the parent is already in the store.
    /// </remarks>
    /// <exception cref="SchedulerConfigException">
    /// The file stated a condition with no trigger to wait for, or named an outcome that is not one.
    /// Refused as the file is read, rather than scheduling a trigger that waits for something that can
    /// never happen.
    /// </exception>
    internal static Continuation ReadContinuation(string? parentName, string? parentGroup, string? condition, string trigger)
    {
        if (parentName is null)
        {
            if (parentGroup is not null || condition is not null)
            {
                throw new SchedulerConfigException(
                    $"{trigger}: a continuation names the trigger whose firing it waits for, and this one names none. "
                    + "State the parent trigger's name, or state neither a continuation nor a condition.");
            }

            return Continuation.None;
        }

        TriggerKey parent = new(parentName, parentGroup ?? Key<string>.DefaultGroup);
        return Continuation.After(parent, ReadContinuationCondition(condition, trigger));
    }

    /// <summary>
    /// Reads the outcomes a condition names, as the names themselves rather than as the integer the
    /// <c>CONTINUATION_CONDITION</c> column holds: a file is written by a person.
    /// </summary>
    private static ContinuationCondition ReadContinuationCondition(string? value, string trigger)
    {
        if (value is null)
        {
            return ContinuationCondition.OnSuccess;
        }

        ContinuationCondition condition = default;
        foreach (string part in value.Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            condition |= NamedOutcome(part, value, trigger);
        }

        if (condition == default)
        {
            throw new SchedulerConfigException($"{trigger}: '{value}' names no continuation condition. {ConditionNames}");
        }

        return condition;
    }

    /// <summary>
    /// One outcome by name. The names are listed rather than parsed as an enum so that a misspelling is
    /// a diagnostic naming the outcomes, and so that the integer the <c>CONTINUATION_CONDITION</c> column
    /// holds is not a second spelling a file can use.
    /// </summary>
    private static ContinuationCondition NamedOutcome(string part, string value, string trigger)
    {
        foreach ((string name, ContinuationCondition outcome) in outcomes)
        {
            if (string.Equals(part, name, StringComparison.OrdinalIgnoreCase))
            {
                return outcome;
            }
        }

        throw new SchedulerConfigException(
            $"{trigger}: '{value}' is not a continuation condition - '{part}' is not an outcome. {ConditionNames}");
    }

    private static readonly (string Name, ContinuationCondition Condition)[] outcomes =
    [
        (nameof(ContinuationCondition.OnSuccess), ContinuationCondition.OnSuccess),
        (nameof(ContinuationCondition.OnFailure), ContinuationCondition.OnFailure),
        (nameof(ContinuationCondition.OnCancellation), ContinuationCondition.OnCancellation),
        (nameof(ContinuationCondition.OnVeto), ContinuationCondition.OnVeto),
        (nameof(ContinuationCondition.OnAnyOutcome), ContinuationCondition.OnAnyOutcome)
    ];

    private const string ConditionNames =
        "Name one or more of OnSuccess, OnFailure, OnCancellation, OnVeto and OnAnyOutcome, joined with '|'.";
}
