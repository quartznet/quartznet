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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Core;

/// <summary>
/// The execution group a firing nobody named one for counts against: a manual firing, and a
/// <c>RunAtStartup</c> run.
/// </summary>
/// <remarks>
/// <para>
/// In order: the job type's <see cref="ExecutionGroupAttribute" />, its placeholders read from the job's
/// <see cref="JobDataMap" /> overlaid by the firing's; otherwise the one group every stored trigger of the
/// job carries; otherwise none. Once the trigger carries it, limits and routing apply through ordinary
/// acquisition, as for any other trigger.
/// </para>
/// <para>
/// A firing must not fail over its group, so the first step is skipped rather than thrown from: a
/// placeholder with nothing behind it, a job type this node cannot load — a dashboard host without the
/// job's assembly — and an attribute that cannot be read. The cost is a read of the job and, when the
/// first step has no answer, a read of the group its triggers share: the group column of the job's
/// triggers where an <see cref="ISharedExecutionGroupSource" /> answers (#4174), and their headers
/// elsewhere.
/// </para>
/// </remarks>
internal static class ManualFiringExecutionGroup
{
    /// <summary>
    /// The group for a firing of <paramref name="jobKey" />, read from the scheduler's own store.
    /// </summary>
    /// <param name="store">The store the job is in.</param>
    /// <param name="jobKey">The job the firing runs.</param>
    /// <param name="firingData">The firing's own job data, or <see langword="null" /> for none.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The group, or <see langword="null" /> for none.</returns>
    internal static async ValueTask<string?> Resolve(
        IJobStore store,
        JobKey jobKey,
        JobDataMap? firingData,
        CancellationToken cancellationToken = default)
    {
        IJobDetail? job = await store.GetJob(jobKey, cancellationToken).ConfigureAwait(false);
        return DeclaredByJob(job, firingData)
               ?? await SharedGroupOf(store, jobKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The group for a firing of <paramref name="jobKey" />, read through a scheduler, for the extensions
    /// and plugins that hold no store.
    /// </summary>
    /// <param name="scheduler">The scheduler the job is in.</param>
    /// <param name="jobKey">The job the firing runs.</param>
    /// <param name="firingData">The firing's own job data, or <see langword="null" /> for none.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The group, or <see langword="null" /> for none.</returns>
    internal static async ValueTask<string?> Resolve(
        IScheduler scheduler,
        JobKey jobKey,
        JobDataMap? firingData,
        CancellationToken cancellationToken = default)
    {
        IJobDetail? job = await scheduler.GetJobDetail(jobKey, cancellationToken).ConfigureAwait(false);
        return DeclaredByJob(job, firingData)
               ?? await SharedGroupOf(scheduler, jobKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one group every stored trigger of <paramref name="jobKey" /> carries: asked of the store when it
    /// can answer, and otherwise read off the job's trigger headers.
    /// </summary>
    internal static ValueTask<string?> SharedGroupOf(IJobStore store, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return store switch
        {
            ISharedExecutionGroupSource source => source.GetSharedExecutionGroup(jobKey, cancellationToken),
            RAMJobStore memory => memory.GetSharedExecutionGroup(jobKey, cancellationToken),
            _ => SharedByListing(store.QueryTriggers, jobKey, cancellationToken)
        };
    }

    /// <inheritdoc cref="SharedGroupOf(IJobStore, JobKey, CancellationToken)" />
    internal static ValueTask<string?> SharedGroupOf(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler is ISharedExecutionGroupSource source
            ? source.GetSharedExecutionGroup(jobKey, cancellationToken)
            : SharedByListing(scheduler.QueryTriggers, jobKey, cancellationToken);
    }

    private static string? DeclaredByJob(IJobDetail? job, JobDataMap? firingData)
    {
        return job is null ? null : DeclaredBy(job, firingData);
    }

    private static async ValueTask<string?> SharedByListing(
        Func<TriggerQuery, CancellationToken, ValueTask<PagedResult<TriggerHeader>>> queryTriggers,
        JobKey jobKey,
        CancellationToken cancellationToken)
    {
        PagedResult<TriggerHeader> triggers = await queryTriggers(
            new TriggerQuery
            {
                Job = jobKey,
                Take = PagedQuery.All
            },
            cancellationToken).ConfigureAwait(false);

        return SharedBy(triggers.Items);
    }

    /// <summary>
    /// What the job type's <see cref="ExecutionGroupAttribute" /> resolves to, or <see langword="null" />
    /// when it has no answer.
    /// </summary>
    internal static string? DeclaredBy(IJobDetail job, JobDataMap? firingData)
    {
        if (!job.JobType.TryResolve(out Type? jobType))
        {
            return null;
        }

        string? template;
        try
        {
            template = DeclaredExecutionGroup.TemplateOf(jobType);
        }
        catch (Exception e) when (JobTypeInformation.IsUnreadableAttribute(e))
        {
            return null;
        }

        if (template is null || !ExecutionGroupTemplate.IsTemplate(template))
        {
            return template;
        }

        JobDataMap jobData = job.JobDataMap;
        return ExecutionGroupTemplate.TryResolve(template, (string key, out object? value) =>
            (firingData is not null && firingData.TryGetValue(key, out value)) || jobData.TryGetValue(key, out value));
    }

    /// <summary>
    /// The one group every trigger carries, or <see langword="null" /> when there are none, or one of
    /// them carries no group or another group.
    /// </summary>
    internal static string? SharedBy(IReadOnlyList<TriggerHeader> triggers)
    {
        string? shared = null;
        foreach (TriggerHeader trigger in triggers)
        {
            if (!Shares(ref shared, trigger.ExecutionGroup))
            {
                return null;
            }
        }

        return shared;
    }

    /// <summary>
    /// Takes one more trigger's group into the group the triggers before it share.
    /// </summary>
    /// <remarks>
    /// <see cref="SharedBy" />'s rule, one trigger at a time, so that a store reading the groups alone
    /// stops at the first that settles the answer.
    /// </remarks>
    /// <param name="shared">
    /// The group the triggers before it share, <see langword="null" /> before the first; this trigger's group
    /// once the answer is <see langword="true" />.
    /// </param>
    /// <param name="group">The trigger's group, <see langword="null" /> for none.</param>
    /// <returns>
    /// <see langword="false" /> once the triggers share no group: this one carries none, or another than the
    /// ones before it.
    /// </returns>
    internal static bool Shares(ref string? shared, string? group)
    {
        if (group is null || (shared is not null && !string.Equals(shared, group, StringComparison.Ordinal)))
        {
            return false;
        }

        shared = group;
        return true;
    }
}
