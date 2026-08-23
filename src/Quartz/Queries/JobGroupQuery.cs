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
/// Selects job groups, as <see cref="JobGroup" />s.
/// </summary>
public sealed record JobGroupQuery : PagedQuery
{
    /// <summary>
    /// Limits the result to the one group with this exact name. Null matches every group.
    /// </summary>
    /// <remarks>
    /// Combined with <c>Take = 1</c> this answers "is this group paused?" without listing
    /// every group.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// Limits the result by paused state: true for paused groups only, false for
    /// unpaused only, null for all groups.
    /// </summary>
    /// <remarks>
    /// A group can be paused while it holds no jobs, so <c>Paused = true</c> can report a
    /// group the unfiltered listing does not — the unfiltered listing enumerates the groups
    /// jobs are in, and an empty group is not one of them.
    /// </remarks>
    public bool? Paused { get; init; }
}
