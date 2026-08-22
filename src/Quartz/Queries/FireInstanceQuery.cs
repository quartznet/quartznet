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
/// Selects firings, as <see cref="FireInstance" />s. The filters combine with AND.
/// </summary>
/// <remarks>
/// Firings are ordered by trigger group, then trigger name, then fire instance id (all ordinal). The
/// fire instance id is the tiebreaker the rest of the query family does not need: a trigger can have
/// several firings in flight at once, so group and name alone do not order a page deterministically.
/// </remarks>
public sealed record FireInstanceQuery : PagedQuery
{
    /// <summary>
    /// Limits the result to firings of triggers whose group matches. Null matches every group.
    /// </summary>
    public GroupMatcher<TriggerKey>? TriggerGroup { get; init; }

    /// <summary>
    /// Limits the result to firings of triggers whose name matches. Null matches every name.
    /// </summary>
    /// <remarks>
    /// Combines with <see cref="TriggerGroup" /> by AND, so the two together select a name pattern
    /// within a group pattern.
    /// </remarks>
    public NameMatcher<TriggerKey>? TriggerName { get; init; }

    /// <summary>
    /// Limits the result to firings of one job.
    /// </summary>
    /// <remarks>
    /// A firing that is only <see cref="FireInstanceState.Acquired" /> has no job recorded yet, so it
    /// never matches this filter — combining it with <c>State = null</c> still lists executing firings
    /// only.
    /// </remarks>
    public JobKey? Job { get; init; }

    /// <summary>
    /// Limits the result to firings owned by one scheduler node, identified by its
    /// <see cref="IScheduler.SchedulerInstanceId" />. Null matches every node.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// Limits the result to firings in the given state. Defaults to
    /// <see cref="FireInstanceState.Executing" />, so a query that says nothing lists what is running;
    /// set it to <see langword="null" /> to include reserved firings as well.
    /// </summary>
    public FireInstanceState? State { get; init; } = FireInstanceState.Executing;
}
