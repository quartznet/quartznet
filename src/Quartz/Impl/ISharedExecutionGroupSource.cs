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

using Quartz.Core;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// A job store, or a scheduler in this process, that answers which execution group every stored trigger of
/// a job carries without listing the job's triggers.
/// </summary>
/// <remarks>
/// <para>
/// A manual firing of a job whose type declares no group counts against that one group, and
/// <see cref="ManualFiringExecutionGroup" /> asks on every <c>TriggerJob</c>. Answered from trigger headers,
/// the cost grows with the job's triggers, and a manual firing that has not run yet is one of them (#4174).
/// </para>
/// <para>
/// The answer is <see cref="ManualFiringExecutionGroup.SharedBy" />'s over the job's headers, with each
/// group as a header reports it. The ADO store answers it; behind a delegate Quartz does not ship it lists
/// the headers, so an override of that listing still runs. The tracing decorator every store is wrapped in,
/// <see cref="StdScheduler" /> and the container's scheduler hand the question on.
/// <see cref="RAMJobStore" /> answers through an internal member of the same name instead, because an
/// explicit implementation on a public type is listed in its public API baseline.
/// </para>
/// <para>
/// Internal, like <see cref="IBackfillingScheduler" />: it promises nothing a caller could act on. A store
/// or scheduler that does not implement it is asked for <see cref="IJobStore.QueryTriggers" /> or
/// <see cref="IScheduler.QueryTriggers" />, as before. So is a decorator of an application's own, whose
/// override of that listing would otherwise be passed by.
/// </para>
/// </remarks>
internal interface ISharedExecutionGroupSource
{
    /// <summary>
    /// The one execution group every stored trigger of <paramref name="jobKey" /> carries.
    /// </summary>
    /// <param name="jobKey">The job whose triggers to read.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The group, or <see langword="null" /> when the job has no triggers, or one of them carries no group
    /// or another group. Groups compare ordinally.
    /// </returns>
    ValueTask<string?> GetSharedExecutionGroup(JobKey jobKey, CancellationToken cancellationToken = default);
}
