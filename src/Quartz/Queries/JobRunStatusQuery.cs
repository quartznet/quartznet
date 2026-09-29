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
/// One page of the per-job run statuses of a scheduler, optionally narrowed to some jobs or to the
/// failing ones.
/// </summary>
/// <remarks>
/// Results are ordered by job group and then name (ordinal), as every <see cref="PagedQuery" /> is.
/// </remarks>
public sealed record JobRunStatusQuery : PagedQuery
{
    /// <summary>
    /// The scheduler whose statuses to list. Required: a store keeps every scheduler's together.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// Lists only these jobs' statuses, or every job's when null. An empty set lists nothing.
    /// </summary>
    public IReadOnlyCollection<JobKey>? Jobs { get; init; }

    /// <summary>
    /// <see langword="true" /> lists the jobs whose <see cref="JobRunStatus.ConsecutiveFailures" /> is above
    /// zero, <see langword="false" /> the rest, and <see langword="null" />, the default, all of them.
    /// </summary>
    public bool? Failing { get; init; }
}
