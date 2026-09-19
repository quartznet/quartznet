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
/// One page of the execution history of a scheduler, optionally narrowed by node, job and trigger.
/// </summary>
/// <remarks>
/// A <c>Contains</c> filter matches a key's group, its name, or the two joined as <c>group.name</c>,
/// case-insensitively — the same reading the dashboard's history search has always had. Results are
/// newest first, which is the one ordering a history page is read in.
/// </remarks>
public sealed record ExecutionHistoryQuery : PagedQuery
{
    /// <summary>
    /// The scheduler whose history to list. Required: a store keeps every scheduler's rows together.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The node whose executions to list, or <see langword="null" /> for every node's.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// Lists only the executions whose job key matches this, or every job's when null.
    /// </summary>
    public string? JobContains { get; init; }

    /// <summary>
    /// Lists only the executions whose trigger key matches this, or every trigger's when null.
    /// </summary>
    public string? TriggerContains { get; init; }

    /// <summary>
    /// Narrows the page to the executions that failed for the last time, or to everything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="true" /> lists the rows where the job failed and the trigger did <em>not</em>
    /// answer with another attempt — <c>!Succeeded &amp;&amp; !RetryScheduled</c>, the occurrence's last
    /// word. <see langword="false" /> lists every other row: the successes, and the failures that are
    /// going to be tried again. <see langword="null" />, the default, lists them all.
    /// </para>
    /// <para>
    /// "Failed for the last time" is the question an operator opens a history page with, and it cannot
    /// be asked with <c>Succeeded</c> alone: a job under a retry policy writes a failed row per
    /// attempt, so filtering on failure shows the same occurrence three times over.
    /// </para>
    /// </remarks>
    public bool? FailedFinally { get; init; }
}
