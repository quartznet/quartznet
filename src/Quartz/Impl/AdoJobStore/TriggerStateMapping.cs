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

using System.Diagnostics;

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Translates between the state strings the ADO job store persists in TRIGGER_STATE and the public
/// <see cref="TriggerState" />. Both directions live here, and the filter direction is derived from the
/// reporting one, so a listing's state filter and the state it reports back cannot disagree.
/// </summary>
/// <remarks>
/// The precedence itself belongs to <see cref="TriggerStateResolver" />, which the in-memory store shares.
/// </remarks>
internal static class TriggerStateMapping
{
    // Every state string the column can hold, including the ones this store never writes there: EXECUTING
    // is only a FIRED_TRIGGERS state and DELETED is only what a read of a missing row reports, but a
    // third-party delegate, migrated data or a hand-repaired row can leave either in the column, and each
    // has to filter as whatever it reports as. Anything not listed here is covered by the negated filters.
    // Derived from the enum so that a state added there is automatically covered here.
    private static readonly string[] storedStates =
        [.. Enum.GetValues<StoredTriggerState>().Select(StoredTriggerStates.ToStoredValue)];

    private static readonly Dictionary<TriggerState, TriggerStateFilter> filters = BuildFilters();

    /// <summary>
    /// Maps a stored state string, plus whether the trigger has an execution in flight, to the state
    /// callers see.
    /// </summary>
    internal static TriggerState ToTriggerState(string? state, bool isExecuting)
    {
        return ToTriggerState(StoredTriggerStates.FromStoredValue(state), isExecuting);
    }

    /// <summary>
    /// Maps a stored state, plus whether the trigger has an execution in flight, to the state callers
    /// see.
    /// </summary>
    internal static TriggerState ToTriggerState(StoredTriggerState state, bool isExecuting)
    {
        return TriggerStateResolver.Resolve(state, isExecuting);
    }

    /// <summary>
    /// The predicate that selects exactly the rows a listing would report as the given state.
    /// </summary>
    internal static TriggerStateFilter ToFilter(TriggerState state)
    {
        if (!filters.TryGetValue(state, out TriggerStateFilter filter))
        {
            Throw.ArgumentOutOfRangeException(nameof(state), "Unknown trigger state: " + state);
        }

        return filter;
    }

    /// <summary>
    /// Derives every listing filter from <see cref="TriggerStateResolver" />, so that a filter cannot
    /// select rows the listing would then report as some other state. Changing the precedence changes
    /// both directions at once.
    /// </summary>
    private static Dictionary<TriggerState, TriggerStateFilter> BuildFilters()
    {
        // The values an unrecognised state string can take cannot be enumerated, so whatever it reports as
        // has to match by exclusion instead. It reports one thing while idle and another while executing,
        // so both sides need their own catch-all; everything else matches by inclusion.
        StoredTriggerState unrecognised = StoredTriggerStates.FromStoredValue("~unrecognised~");
        TriggerState catchAllIdle = TriggerStateResolver.Resolve(unrecognised, isExecuting: false);
        TriggerState catchAllExecuting = TriggerStateResolver.Resolve(unrecognised, isExecuting: true);

        var result = new Dictionary<TriggerState, TriggerStateFilter>();

        foreach (TriggerState reported in Enum.GetValues<TriggerState>())
        {
            string[] whenIdle = Matching(reported, isExecuting: false);
            string[] whenExecuting = Matching(reported, isExecuting: true);

            // A state executing outranks is only reported while nothing is running, and vice versa; a
            // state that outranks executing reports the same either way and needs no extra predicate. A
            // state no stored value reports as gets no entry at all, so asking to filter by it fails
            // loudly rather than quietly matching the wrong rows.
            if (whenIdle.Length == 0 && whenExecuting.Length == 0)
            {
                continue;
            }

            result[reported] = (whenIdle.Length, whenExecuting.Length) switch
            {
                (0, _) => Build(whenExecuting, reported == catchAllExecuting, executing: true),
                (_, 0) => Build(whenIdle, reported == catchAllIdle, executing: false),
                _ => BuildUnconditional(whenIdle, whenExecuting, reported == catchAllIdle)
            };
        }

        return result;

        static TriggerStateFilter Build(string[] states, bool isCatchAll, bool? executing)
        {
            // Excluding what reports as something else also covers the values that cannot be listed.
            return isCatchAll
                ? new TriggerStateFilter(Array.FindAll(storedStates, x => !states.Contains(x)), Negated: true, executing)
                : new TriggerStateFilter(states, Negated: false, executing);
        }

        static TriggerStateFilter BuildUnconditional(string[] whenIdle, string[] whenExecuting, bool isCatchAll)
        {
            // Reported either way, so no executing predicate — which is only expressible because the two
            // sides agree. A precedence change that broke that would need a different predicate shape.
            Debug.Assert(
                whenIdle.SequenceEqual(whenExecuting),
                "a state reported both while idle and while executing must cover the same stored states");

            return Build(whenIdle, isCatchAll, executing: null);
        }
    }

    /// <summary>
    /// The stored states that report as <paramref name="reported" /> under the given execution.
    /// </summary>
    private static string[] Matching(TriggerState reported, bool isExecuting)
    {
        return Array.FindAll(storedStates, stored => ToTriggerState(stored, isExecuting) == reported);
    }
}

/// <summary>
/// The predicate that selects the rows a listing reports as one particular <see cref="TriggerState" />.
/// </summary>
/// <param name="States">The stored state strings to match.</param>
/// <param name="Negated">
/// Whether <paramref name="States" /> is the set to exclude rather than the set to match. Needed for the
/// state an unrecognised stored value reports as, since those values cannot be listed.
/// </param>
/// <param name="Executing">
/// <see langword="true" /> when the rows must also have an execution in flight, <see langword="false" />
/// when they must not, and <see langword="null" /> when execution cannot change the reported state.
/// </param>
internal readonly record struct TriggerStateFilter(string[] States, bool Negated, bool? Executing);
