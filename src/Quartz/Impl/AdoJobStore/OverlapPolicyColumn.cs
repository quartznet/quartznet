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

using System.Data.Common;
using System.Globalization;

using Quartz.Impl.Triggers;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// How a trigger's overlap policy is spelled in the <c>OVERLAP_POLICY</c> column of the triggers
/// table.
/// </summary>
/// <remarks>
/// <para>
/// The integer of the policy, with <see cref="OverlapPolicy.Default" /> written as <c>NULL</c>, so a
/// trigger nobody gave a policy writes the row a 4.2 node would have written.
/// </para>
/// <para>
/// <see cref="UnsettledFlag" /> is added to <see cref="OverlapPolicy.CancelPrevious" /> while
/// <see cref="TriggerBase.OverlapPolicyUnsettled" /> is set. It is the store's bookkeeping and never
/// leaves it: the trigger reports the policy alone.
/// </para>
/// </remarks>
internal static class OverlapPolicyColumn
{
    /// <summary>
    /// Added to <see cref="OverlapPolicy.CancelPrevious" /> while a firing that started under another
    /// policy may still be running.
    /// </summary>
    internal const int UnsettledFlag = 16;

    /// <summary>
    /// What the column holds for <paramref name="trigger" />.
    /// </summary>
    public static object ToDbValue(ITrigger trigger)
    {
        OverlapPolicy policy = trigger.OverlapPolicy;
        if (policy == OverlapPolicy.Default)
        {
            return DBNull.Value;
        }

        bool unsettled = policy == OverlapPolicy.CancelPrevious
                         && trigger is TriggerBase { OverlapPolicyUnsettled: true };

        return (int) policy | (unsettled ? UnsettledFlag : 0);
    }

    /// <summary>
    /// The column's raw value at <paramref name="ordinal" />, or <see langword="null" />.
    /// </summary>
    /// <remarks>
    /// Not <c>GetInt32</c>: Oracle hands back a decimal for a <c>NUMBER</c> column.
    /// </remarks>
    public static int? Read(DbDataReader rs, int ordinal)
    {
        return rs.IsDBNull(ordinal) ? null : Convert.ToInt32(rs.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The policy and the unsettled mark a stored value says.
    /// </summary>
    /// <remarks>
    /// A value this node does not know — one a newer node wrote — reads as
    /// <see cref="OverlapPolicy.Default" />, as an unreadable retry policy reads as none: the row is
    /// still a schedule, and refusing to read it would take a job out of service.
    /// </remarks>
    public static (OverlapPolicy Policy, bool Unsettled) Decode(int? value)
    {
        if (value is not { } stored)
        {
            return (OverlapPolicy.Default, false);
        }

        OverlapPolicy policy = (OverlapPolicy) (stored & ~UnsettledFlag);
        if (!Enum.IsDefined(policy))
        {
            return (OverlapPolicy.Default, false);
        }

        return (policy, policy == OverlapPolicy.CancelPrevious && (stored & UnsettledFlag) != 0);
    }

    /// <summary>
    /// Puts what the column says onto a trigger read from its row.
    /// </summary>
    public static void Apply(TriggerBase trigger, int? value)
    {
        (OverlapPolicy policy, bool unsettled) = Decode(value);
        trigger.OverlapPolicy = policy;
        trigger.OverlapPolicyUnsettled = unsettled;
    }
}
