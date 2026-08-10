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
}
