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
/// One page of the misfires of a scheduler, optionally narrowed by node and trigger.
/// </summary>
/// <remarks>
/// <inheritdoc cref="ExecutionHistoryQuery" path="/remarks" />
/// </remarks>
public sealed record MisfireHistoryQuery : PagedQuery
{
    /// <summary>
    /// The scheduler whose misfires to list. Required: a store keeps every scheduler's rows together.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The node whose misfires to list, or <see langword="null" /> for every node's.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// Lists only the misfires whose trigger key matches this, or every trigger's when null.
    /// </summary>
    public string? TriggerContains { get; init; }
}
