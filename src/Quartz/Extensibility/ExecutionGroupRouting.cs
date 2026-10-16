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

using System.Collections.Frozen;

namespace Quartz.Extensibility;

/// <summary>
/// Which execution groups a node serves: the ones <see cref="QuartzSchedulerOptions.ServedExecutionGroups" />
/// lists, or every one but those <see cref="QuartzSchedulerOptions.RefusedExecutionGroups" /> lists.
/// </summary>
/// <remarks>
/// <para>
/// One model for the three places the question is asked. The scheduler thread puts the two lists on the
/// <see cref="TriggerAcquisitionRequest" /> as scopes; the stores read them back from the request through
/// <see cref="FromScopes" />; the health check and the dashboard rebuild the same predicate from the
/// configuration spellings <see cref="SchedulerMetadata" /> reports, through <see cref="FromSpellings" />.
/// A trigger's group is compared ordinally, as a job type name is: the ADO.NET store's SQL follows the
/// column's collation instead, and the two agree because 4.x writes a group the way it compares it.
/// </para>
/// <para>
/// The ungrouped bucket is spelled <c>_</c> (or <c>null</c>) in configuration, as an execution limit's
/// key spells it, and is <see cref="ExecutionGroupScope.Default" /> on the request. A serve-list that
/// omits it does not serve ungrouped triggers; a refuse-list that omits it does.
/// </para>
/// </remarks>
internal sealed class ExecutionGroupRouting
{
    /// <summary>
    /// The most groups a list may name. Oracle refuses an <c>IN</c> list of more than a thousand
    /// expressions, and the acquisition statement spells the list as one.
    /// </summary>
    internal const int MaxGroups = 1000;

    private ExecutionGroupRouting(bool isServeList, FrozenSet<string> groups, bool includesUngrouped, string[] spellings)
    {
        IsServeList = isServeList;
        Groups = groups;
        IncludesUngrouped = includesUngrouped;

        List<ExecutionGroupScope> scopes = new(groups.Count + 1);
        if (includesUngrouped)
        {
            scopes.Add(ExecutionGroupScope.Default);
        }

        foreach (string group in groups)
        {
            scopes.Add(ExecutionGroupScope.Named(group));
        }

        // One array, built once and handed out by reference every round: the in-memory store and the
        // delegate's statement cache both key on the instance.
        ExecutionGroupScope[] asScopes = [.. scopes];
        Served = isServeList ? asScopes : null;
        Refused = isServeList ? null : asScopes;
        Spellings = spellings;
    }

    /// <summary>
    /// Whether <see cref="Groups" /> is what this node serves, rather than what it refuses.
    /// </summary>
    public bool IsServeList { get; }

    /// <summary>
    /// The named groups the list spells, compared ordinally. The ungrouped bucket is
    /// <see cref="IncludesUngrouped" />, not a member.
    /// </summary>
    public FrozenSet<string> Groups { get; }

    /// <summary>
    /// Whether the list names the ungrouped bucket.
    /// </summary>
    public bool IncludesUngrouped { get; }

    /// <summary>
    /// The request's view of a serve-list, or <see langword="null" /> for a refuse-list.
    /// </summary>
    public IReadOnlyCollection<ExecutionGroupScope>? Served { get; }

    /// <summary>
    /// The request's view of a refuse-list, or <see langword="null" /> for a serve-list.
    /// </summary>
    public IReadOnlyCollection<ExecutionGroupScope>? Refused { get; }

    /// <summary>
    /// The list as configuration spells it, normalized: trimmed, with <c>_</c> for the ungrouped bucket.
    /// What <see cref="SchedulerMetadata" /> reports.
    /// </summary>
    public string[] Spellings { get; }

    /// <summary>
    /// Whether this node acquires and misfire-handles a trigger of <paramref name="executionGroup" />,
    /// <see langword="null" /> being a trigger that carries none.
    /// </summary>
    public bool Serves(string? executionGroup)
    {
        bool listed = executionGroup is null ? IncludesUngrouped : Groups.Contains(executionGroup);
        return IsServeList ? listed : !listed;
    }

    /// <summary>
    /// The routing <paramref name="options" /> configure, or <see langword="null" /> when both lists are
    /// empty — the node that serves everything.
    /// </summary>
    /// <exception cref="ArgumentException">The lists fail the rules <see cref="Validate" /> states.</exception>
    public static ExecutionGroupRouting? From(QuartzSchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return FromSpellings(options.ServedExecutionGroups, options.RefusedExecutionGroups);
    }

    /// <summary>
    /// The routing two configuration spellings describe, or <see langword="null" /> when both are empty.
    /// </summary>
    /// <exception cref="ArgumentException">The lists fail the rules <see cref="Validate" /> states.</exception>
    public static ExecutionGroupRouting? FromSpellings(IReadOnlyList<string> served, IReadOnlyList<string> refused)
    {
        ArgumentNullException.ThrowIfNull(served);
        ArgumentNullException.ThrowIfNull(refused);

        List<string>? failures = null;
        Validate(served, refused, ref failures);
        if (failures is not null)
        {
            Throw.ArgumentException(string.Join(" ", failures), served.Count > 0 ? nameof(served) : nameof(refused));
        }

        if (served.Count == 0 && refused.Count == 0)
        {
            return null;
        }

        bool isServeList = served.Count > 0;
        return Build(isServeList, Normalize(isServeList ? served : refused));
    }

    /// <summary>
    /// The routing a request or criteria carries, or <see langword="null" /> when it carries none. Either
    /// collection is one <see cref="Validated" /> has already passed.
    /// </summary>
    public static ExecutionGroupRouting? FromScopes(
        IReadOnlyCollection<ExecutionGroupScope>? served,
        IReadOnlyCollection<ExecutionGroupScope>? refused)
    {
        if (served is null && refused is null)
        {
            return null;
        }

        bool isServeList = served is not null;
        IReadOnlyCollection<ExecutionGroupScope> scopes = served ?? refused!;

        bool includesUngrouped = false;
        List<string> names = new(scopes.Count);
        foreach (ExecutionGroupScope scope in scopes)
        {
            if (scope.IsDefault)
            {
                includesUngrouped = true;
            }
            else
            {
                names.Add(scope.Name!);
            }
        }

        return Build(isServeList, (names, includesUngrouped));
    }

    /// <summary>
    /// <see cref="Validated" />, refusing a list set beside the other one: what a node serves and what it
    /// refuses are two ways of saying one thing, so a request or criteria carry one of them.
    /// </summary>
    /// <param name="scopes">The list being set.</param>
    /// <param name="other">The opposite list, as already set.</param>
    /// <param name="parameterName">The name to report the list under.</param>
    /// <returns>The list, validated.</returns>
    public static IReadOnlyCollection<ExecutionGroupScope>? ValidatedBeside(
        IReadOnlyCollection<ExecutionGroupScope>? scopes,
        IReadOnlyCollection<ExecutionGroupScope>? other,
        string parameterName)
    {
        if (scopes is not null && other is not null)
        {
            Throw.ArgumentException("ServedExecutionGroups and RefusedExecutionGroups cannot both be set; a node lists what it serves or what it refuses.", parameterName);
        }

        return Validated(scopes, parameterName);
    }

    /// <summary>
    /// Returns the scopes unchanged, having established that a statement built from them means what the
    /// caller intended.
    /// </summary>
    /// <remarks>
    /// Only a named group or the ungrouped bucket can be routed: the catch-all and a prefix are rules
    /// about the limits, and spelling either in SQL would need a <c>LIKE</c> with escaping in three
    /// statements. A duplicate is refused rather than folded, because a list that names a group twice is
    /// a list somebody edited by hand and got wrong.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An entry is the catch-all or a prefix, an entry repeats, or there are more than
    /// <see cref="MaxGroups" /> of them.
    /// </exception>
    public static IReadOnlyCollection<ExecutionGroupScope>? Validated(IReadOnlyCollection<ExecutionGroupScope>? scopes, string parameterName)
    {
        if (scopes is null)
        {
            return null;
        }

        if (scopes.Count > MaxGroups)
        {
            Throw.ArgumentException($"An execution group list must not exceed {MaxGroups} entries.", parameterName);
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        bool seenDefault = false;
        foreach (ExecutionGroupScope scope in scopes)
        {
            if (scope.IsDefault)
            {
                if (seenDefault)
                {
                    Throw.ArgumentException("An execution group list names the ungrouped bucket more than once.", parameterName);
                }

                seenDefault = true;
                continue;
            }

            if (scope.Name is not { } name)
            {
                Throw.ArgumentException(
                    "An execution group list routes named groups and the ungrouped bucket only; the catch-all and a prefix are "
                    + "execution-limit rules and cannot be routed.",
                    parameterName);
                return null;
            }

            if (!seen.Add(name))
            {
                Throw.ArgumentException($"An execution group list names '{name}' more than once.", parameterName);
            }
        }

        return scopes;
    }

    /// <summary>
    /// The rules a configured pair of lists has to keep, each broken one added to
    /// <paramref name="failures" /> in the words the options validator reports.
    /// </summary>
    public static void Validate(IReadOnlyList<string> served, IReadOnlyList<string> refused, ref List<string>? failures)
    {
        if (served.Count > 0 && refused.Count > 0)
        {
            (failures ??= []).Add(
                $"{nameof(QuartzSchedulerOptions.ServedExecutionGroups)} and {nameof(QuartzSchedulerOptions.RefusedExecutionGroups)} "
                + "are both set. A node either lists the groups it serves or the groups it refuses, never both; clear one of them.");
        }

        ValidateList(nameof(QuartzSchedulerOptions.ServedExecutionGroups), served, ref failures);
        ValidateList(nameof(QuartzSchedulerOptions.RefusedExecutionGroups), refused, ref failures);
    }

    private static void ValidateList(string optionName, IReadOnlyList<string> entries, ref List<string>? failures)
    {
        if (entries.Count > MaxGroups)
        {
            (failures ??= []).Add($"{optionName} names {entries.Count} groups; at most {MaxGroups} can be listed, which is Oracle's ceiling on an IN list.");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        bool seenDefault = false;
        foreach (string entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                (failures ??= []).Add($"{optionName} holds a blank entry. Name a group, or '_' for triggers that carry none; remove the entry otherwise.");
                continue;
            }

            string trimmed = entry.Trim();
            if (trimmed == ExecutionLimits.OtherGroups)
            {
                (failures ??= []).Add(
                    $"{optionName} names '*', which is the execution limits' catch-all. A node serves every group by leaving both lists "
                    + "empty, and refuses a set of groups by listing them; remove the entry.");
                continue;
            }

            if (ExecutionLimits.TryReadPrefixKey(trimmed, out _))
            {
                (failures ??= []).Add(
                    $"{optionName} names '{trimmed}', a prefix. Routing has no prefix form: list the groups by name, or route by the "
                    + "ungrouped bucket '_'.");
                continue;
            }

            if (ExecutionLimits.IsDefaultGroupAlias(trimmed))
            {
                if (seenDefault)
                {
                    (failures ??= []).Add($"{optionName} names the ungrouped bucket more than once ('_' and 'null' are the same bucket); remove the repeat.");
                }

                seenDefault = true;
                continue;
            }

            if (!seen.Add(trimmed))
            {
                (failures ??= []).Add($"{optionName} names '{trimmed}' more than once; remove the repeat.");
            }
        }
    }

    /// <summary>
    /// Trims the spellings and splits the ungrouped bucket out, assuming <see cref="Validate" /> passed.
    /// </summary>
    private static (List<string> Names, bool IncludesUngrouped) Normalize(IReadOnlyList<string> entries)
    {
        List<string> names = new(entries.Count);
        bool includesUngrouped = false;
        foreach (string entry in entries)
        {
            string trimmed = entry.Trim();
            if (ExecutionLimits.IsDefaultGroupAlias(trimmed))
            {
                includesUngrouped = true;
            }
            else
            {
                names.Add(trimmed);
            }
        }

        return (names, includesUngrouped);
    }

    private static ExecutionGroupRouting Build(bool isServeList, (List<string> Names, bool IncludesUngrouped) list)
    {
        // The spellings are reported in a fixed order - the bucket first, then the names as listed -
        // so two nodes configured alike report alike whatever order their files listed the groups in.
        List<string> spellings = new(list.Names.Count + 1);
        if (list.IncludesUngrouped)
        {
            spellings.Add(ExecutionLimits.DefaultGroupAlias);
        }

        spellings.AddRange(list.Names);

        return new ExecutionGroupRouting(
            isServeList,
            list.Names.ToFrozenSet(StringComparer.Ordinal),
            list.IncludesUngrouped,
            [.. spellings]);
    }
}
