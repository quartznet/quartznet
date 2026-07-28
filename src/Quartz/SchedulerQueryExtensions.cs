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

using Quartz.Impl.Matchers;

namespace Quartz;

/// <summary>
/// Convenience listings built on the <see cref="IScheduler" /> query members.
/// </summary>
/// <remarks>
/// Every method here enumerates the whole result — there is no paging, and a scheduler with
/// many jobs, triggers or groups will pay for all of them on each call. Use the underlying
/// query member with <see cref="PagedQuery.Skip" /> and <see cref="PagedQuery.Take" /> when
/// the result may be large, and note that the query members return richer items than the
/// bare keys and names returned here.
/// </remarks>
public static class SchedulerQueryExtensions
{
    /// <summary>
    /// Get the keys of all the <see cref="IJobDetail" />s in the matching groups.
    /// </summary>
    /// <remarks>
    /// Enumerates every matching job. For paged access, and for the job metadata a listing
    /// usually needs, use <see cref="IScheduler.QueryJobs" />.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="matcher">Limits the result to jobs whose group matches.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<JobKey>> GetJobKeys(
        this IScheduler scheduler,
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(matcher);

        PagedResult<JobHeader> result = await scheduler.QueryJobs(new JobQuery { Group = matcher }, cancellationToken).ConfigureAwait(false);
        return result.Items.ConvertAll(static header => header.Key);
    }

    /// <summary>
    /// Get the keys of all the <see cref="ITrigger" />s in the matching groups.
    /// </summary>
    /// <remarks>
    /// Enumerates every matching trigger. For paged access, and for the state and fire times
    /// a listing usually needs, use <see cref="IScheduler.QueryTriggers" />.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="matcher">Limits the result to triggers whose group matches.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<TriggerKey>> GetTriggerKeys(
        this IScheduler scheduler,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(matcher);

        PagedResult<TriggerHeader> result = await scheduler.QueryTriggers(new TriggerQuery { Group = matcher }, cancellationToken).ConfigureAwait(false);
        return result.Items.ConvertAll(static header => header.Key);
    }

    /// <summary>
    /// Get the names of all known <see cref="IJobDetail" /> groups.
    /// </summary>
    /// <remarks>
    /// Enumerates every group. For paged access, and for each group's paused state, use
    /// <see cref="IScheduler.QueryJobGroups" />.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<string>> GetJobGroupNames(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        PagedResult<JobGroup> result = await scheduler.QueryJobGroups(new JobGroupQuery(), cancellationToken).ConfigureAwait(false);
        return result.Items.ConvertAll(static group => group.Name);
    }

    /// <summary>
    /// Get the names of all known <see cref="ITrigger" /> groups.
    /// </summary>
    /// <remarks>
    /// Enumerates every group. For paged access, and for each group's paused state, use
    /// <see cref="IScheduler.QueryTriggerGroups" />.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<string>> GetTriggerGroupNames(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        PagedResult<TriggerGroup> result = await scheduler.QueryTriggerGroups(new TriggerGroupQuery(), cancellationToken).ConfigureAwait(false);
        return result.Items.ConvertAll(static group => group.Name);
    }

    /// <summary>
    /// Get the names of all <see cref="ITrigger" /> groups that are paused.
    /// </summary>
    /// <remarks>
    /// Enumerates every paused group. For paged access use
    /// <see cref="IScheduler.QueryTriggerGroups" /> with
    /// <see cref="TriggerGroupQuery.Paused" /> set.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<string>> GetPausedTriggerGroups(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        PagedResult<TriggerGroup> result = await scheduler.QueryTriggerGroups(new TriggerGroupQuery { Paused = true }, cancellationToken).ConfigureAwait(false);
        return result.Items.ConvertAll(static group => group.Name);
    }

    /// <summary>
    /// Get the names of all registered <see cref="ICalendar" />s.
    /// </summary>
    /// <remarks>
    /// Enumerates every calendar name. For paged access use
    /// <see cref="IScheduler.QueryCalendarNames" />.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<string>> GetCalendarNames(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        PagedResult<string> result = await scheduler.QueryCalendarNames(new CalendarQuery(), cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>
    /// Returns true if the given job group is paused.
    /// </summary>
    /// <remarks>
    /// Enumerates every paused job group. For paged access use
    /// <see cref="IScheduler.QueryJobGroups" /> with <see cref="JobGroupQuery.Paused" /> set.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="groupName">The group to check.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<bool> IsJobGroupPaused(
        this IScheduler scheduler,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        PagedResult<JobGroup> result = await scheduler.QueryJobGroups(new JobGroupQuery { Paused = true }, cancellationToken).ConfigureAwait(false);
        return result.Items.Exists(group => string.Equals(group.Name, groupName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns true if the given trigger group is paused.
    /// </summary>
    /// <remarks>
    /// Enumerates every paused trigger group. For paged access use
    /// <see cref="IScheduler.QueryTriggerGroups" /> with
    /// <see cref="TriggerGroupQuery.Paused" /> set.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="groupName">The group to check.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<bool> IsTriggerGroupPaused(
        this IScheduler scheduler,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        PagedResult<TriggerGroup> result = await scheduler.QueryTriggerGroups(new TriggerGroupQuery { Paused = true }, cancellationToken).ConfigureAwait(false);
        return result.Items.Exists(group => string.Equals(group.Name, groupName, StringComparison.Ordinal));
    }
}
