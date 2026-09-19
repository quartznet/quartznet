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
/// Selects triggers, as <see cref="TriggerHeader" />s. The filters combine with AND.
/// </summary>
public sealed record TriggerQuery : PagedQuery
{
    /// <summary>
    /// Limits the result to triggers whose group matches. Null matches every group.
    /// </summary>
    public GroupMatcher<TriggerKey>? Group { get; init; }

    /// <summary>
    /// Limits the result to triggers whose name matches. Null matches every name.
    /// </summary>
    public NameMatcher<TriggerKey>? Name { get; init; }

    /// <summary>
    /// Limits the result to the triggers of one job.
    /// </summary>
    public JobKey? Job { get; init; }

    /// <summary>
    /// Limits the result to triggers that reference the named calendar.
    /// </summary>
    public string? CalendarName { get; init; }

    /// <summary>
    /// Limits the result to triggers in the given state — for example
    /// <see cref="TriggerState.Error" /> to list or count failed triggers.
    /// </summary>
    public TriggerState? State { get; init; }

    /// <summary>
    /// Limits the result to triggers whose next fire time is strictly before this instant. Null,
    /// the default, matches every trigger whatever it is due to do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A trigger with no next fire time never matches, because "has no next firing" is not "is due
    /// before then" — that set is <see cref="TriggerState.Complete" />, and asking for it by state
    /// is how to list it.
    /// </para>
    /// <para>
    /// An instant in the past makes this the overdue set: combined with
    /// <see cref="State" /> of <see cref="TriggerState.Normal" /> it is every trigger that should
    /// already have fired and has not, which is what <c>QuartzHealthCheckOptions.StaleFiringTolerance</c>
    /// asks a scheduler in order to notice that it has stopped firing. Combined with
    /// <see cref="PagedQuery.IncludeTotalCount" /> and a <see cref="PagedQuery.Take" /> of zero it
    /// counts them instead.
    /// </para>
    /// <para>
    /// A filter rather than an ordering, which is also why <see cref="PagedQuery" />'s single order
    /// still holds: every store and the HTTP wire agree on group-then-name, no dialect agrees on
    /// where a null sorts, and the index the ADO store carries over the state and next fire time
    /// (<c>IDX_QRTZ_T_NFT_ST</c>) answers this predicate with a seek.
    /// </para>
    /// </remarks>
    public DateTimeOffset? NextFireTimeBefore { get; init; }
}
