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

namespace Quartz;

/// <summary>
/// A ledger of the execution-group slots still free during one trigger acquisition, created from an
/// <see cref="ExecutionLimits" /> snapshot by <see cref="ExecutionLimits.CreateSlots" />.
/// </summary>
/// <remarks>
/// <para>
/// A job store acquiring triggers asks <see cref="TryTake" /> for each candidate and skips the ones it
/// refuses. The ledger holds the rule that decides which triggers a node may take — a group's own limit
/// wins, an unlisted named group falls back to the longest prefix it starts with and then to
/// <see cref="ExecutionLimits.OtherGroups" />, and triggers with no execution group never do — so that a store outside this assembly enforces the same limits as
/// the ones shipped with Quartz rather than reinventing them.
/// </para>
/// <para>
/// A <see cref="ExecutionLimitScope.Cluster" /> limit reaches this ledger already lowered by what the
/// cluster holds in flight, because <see cref="ExecutionLimits.CreateSlots" /> subtracts the counts the
/// store passed it. Nothing about <see cref="TryTake" /> changes for one: by the time it is asked, a
/// cluster-scoped group's remaining count is what the whole cluster has left rather than what this node
/// has left, and it counts down the same way.
/// </para>
/// <para>
/// It is mutable and not thread-safe: one acquisition pass owns one ledger, which is why
/// <see cref="ExecutionLimits" /> hands out a new one instead of being one. Create a ledger per attempt,
/// because a retried acquisition must start from the limits again rather than from what the failed
/// attempt had already counted down.
/// </para>
/// </remarks>
public sealed class ExecutionSlots
{
    private readonly Dictionary<string, ExecutionGroupAllowance> available;
    private readonly ExecutionGroupPrefixAllowance[] prefixes;
    private readonly bool usesTriggerGroupWhenUnset;

    internal ExecutionSlots(
        Dictionary<string, ExecutionGroupAllowance> available,
        ExecutionGroupPrefixAllowance[] prefixes,
        bool usesTriggerGroupWhenUnset)
    {
        this.available = available;
        this.prefixes = prefixes;
        this.usesTriggerGroupWhenUnset = usesTriggerGroupWhenUnset;
    }

    /// <summary>
    /// Takes one slot for a trigger in the given execution group, if the group has one left.
    /// </summary>
    /// <param name="executionGroup">The trigger's <see cref="ITrigger.ExecutionGroup" />, which may be
    /// <see langword="null" />.</param>
    /// <param name="triggerGroup">The trigger's <see cref="Key{T}.Group" />, which the limits fall
    /// back to when <see cref="ExecutionLimits.UsesTriggerGroupWhenUnset" /> is on and the trigger
    /// carries no execution group. Required rather than optional so that a store cannot silently opt out
    /// of the derivation by not passing it.</param>
    /// <returns><see langword="true" /> when the trigger may fire on this node, in which case a slot has
    /// been taken. <see langword="false" /> when its group is forbidden or has run out.</returns>
    public bool TryTake(string? executionGroup, string triggerGroup)
    {
        string key = ExecutionLimits.ResolveGroupKey(executionGroup, triggerGroup, usesTriggerGroupWhenUnset);

        ExecutionGroupAllowance allowance;
        if (available.TryGetValue(key, out ExecutionGroupAllowance groupAllowance))
        {
            allowance = groupAllowance;
        }
        else if (ExecutionLimits.TryInherit(available, prefixes, key, out ExecutionGroupAllowance inherited))
        {
            // The longest matching prefix, then OtherGroups ("*"): for named groups only, never for the
            // default (null/ungrouped) triggers.
            allowance = inherited;
        }
        else
        {
            return true; // no limit configured for this group
        }

        if (allowance.MaxConcurrent is not int limit)
        {
            return true; // unlimited
        }

        if (limit <= 0)
        {
            return false; // forbidden or exhausted
        }

        // Count down against the specific group key, even when the value came from a prefix or the
        // OtherGroups default, so that each such group gets its own allowance rather than sharing one.
        available[key] = allowance with { MaxConcurrent = limit - 1 };
        return true;
    }

    /// <summary>
    /// Reads how many slots are left for one execution group without taking any.
    /// </summary>
    /// <param name="executionGroup">The execution group, or <see langword="null" /> for triggers that have
    /// none.</param>
    /// <param name="remaining">The slots left, or <see langword="null" /> when the group is unlimited.</param>
    /// <returns><see langword="true" /> when the group is being tracked. <see langword="false" /> does not
    /// mean nothing is left — a prefix or <see cref="ExecutionLimits.OtherGroups" /> may still apply to a
    /// named group that has not been taken from yet.</returns>
    public bool TryGetRemaining(string? executionGroup, out int? remaining)
    {
        if (available.TryGetValue(ExecutionLimits.NormalizeGroupKey(executionGroup), out ExecutionGroupAllowance allowance))
        {
            remaining = allowance.MaxConcurrent;
            return true;
        }

        remaining = null;
        return false;
    }
}
