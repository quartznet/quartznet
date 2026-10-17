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
/// first step has no answer, a read of its trigger headers.
/// </para>
/// </remarks>
internal static class ManualFiringExecutionGroup
{
    /// <summary>
    /// The group for a firing of <paramref name="jobKey" />.
    /// </summary>
    /// <param name="jobKey">The job the firing runs.</param>
    /// <param name="firingData">The firing's own job data, or <see langword="null" /> for none.</param>
    /// <param name="getJob">Reads the job.</param>
    /// <param name="queryTriggers">Reads trigger headers.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The group, or <see langword="null" /> for none.</returns>
    internal static async ValueTask<string?> Resolve(
        JobKey jobKey,
        JobDataMap? firingData,
        Func<JobKey, CancellationToken, ValueTask<IJobDetail?>> getJob,
        Func<TriggerQuery, CancellationToken, ValueTask<PagedResult<TriggerHeader>>> queryTriggers,
        CancellationToken cancellationToken = default)
    {
        IJobDetail? job = await getJob(jobKey, cancellationToken).ConfigureAwait(false);
        if (job is not null && DeclaredBy(job, firingData) is { } declared)
        {
            return declared;
        }

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
            if (trigger.ExecutionGroup is not { } group
                || (shared is not null && !string.Equals(shared, group, StringComparison.Ordinal)))
            {
                return null;
            }

            shared = group;
        }

        return shared;
    }
}
