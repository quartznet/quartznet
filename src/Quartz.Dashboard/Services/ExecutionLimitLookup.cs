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

namespace Quartz.Dashboard.Services;

/// <summary>
/// Which of a scheduler's execution limits governs a group, as the scheduler decides it: the Overview's
/// execution-groups panel and Trigger Detail's diagnosis ask the same question and must not answer it twice.
/// </summary>
internal static class ExecutionLimitLookup
{
    /// <summary>
    /// The key a firing or a trigger counts against, spelled as configuration spells it: <c>_</c> for the
    /// ungrouped bucket, else the group, derived from the trigger group when the scheduler says so.
    /// </summary>
    public static string KeyOf(string? executionGroup, string triggerGroup, bool usesTriggerGroupWhenUnset)
    {
        string storageKey = ExecutionLimits.ResolveGroupKey(executionGroup, triggerGroup, usesTriggerGroupWhenUnset);
        return storageKey == ExecutionLimits.DefaultGroupKey ? ExecutionLimits.DefaultGroupAlias : storageKey;
    }

    /// <summary>
    /// The limit that governs one group: its own when it has one, otherwise the longest prefix it starts
    /// with, otherwise the catch-all, neither of which applies to the ungrouped bucket. The same rule the
    /// scheduler applies.
    /// </summary>
    /// <param name="limits">The scheduler's limits, keyed by configuration spelling.</param>
    /// <param name="key">The group, as <see cref="KeyOf" /> spells it.</param>
    /// <param name="inheritedFrom">The rule the limit came from, when it is not the group's own.</param>
    public static DashboardExecutionLimit? Resolve(ExecutionLimitsDto limits, string key, out string? inheritedFrom)
    {
        ArgumentNullException.ThrowIfNull(limits);
        inheritedFrom = null;

        if (limits.Limits.TryGetValue(key, out DashboardExecutionLimit own))
        {
            return own;
        }

        if (key == ExecutionLimits.DefaultGroupAlias)
        {
            return null;
        }

        string? longest = null;
        foreach (string candidate in limits.Limits.Keys)
        {
            if (ExecutionLimits.TryReadPrefixKey(candidate, out string? prefix)
                && key.StartsWith(prefix, StringComparison.Ordinal)
                && (longest is null || candidate.Length > longest.Length))
            {
                longest = candidate;
            }
        }

        if (longest is not null)
        {
            inheritedFrom = longest;
            return limits.Limits[longest];
        }

        if (limits.Limits.TryGetValue(ExecutionLimits.OtherGroups, out DashboardExecutionLimit catchAll))
        {
            inheritedFrom = ExecutionLimits.OtherGroups;
            return catchAll;
        }

        return null;
    }
}
