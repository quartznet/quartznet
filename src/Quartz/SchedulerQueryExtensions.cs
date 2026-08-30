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
/// Convenience calls built on the <see cref="IScheduler" /> query members.
/// </summary>
/// <remarks>
/// <para>
/// Two families live here, and they page differently. The <c>Get*</c> listings are the members the
/// query members replaced: each enumerates the whole result — there is no paging, and a scheduler
/// with many jobs, triggers or groups will pay for all of them on each call. Use the underlying
/// query member with <see cref="PagedQuery.Skip" /> and <see cref="PagedQuery.Take" /> when the
/// result may be large, and note that the query members return richer items than the bare keys and
/// names returned here.
/// </para>
/// <para>
/// The <c>Query*</c> shorthands are the opposite: each one is the query member called with a query
/// record the caller did not have to name, so it pages exactly as the member does —
/// <see cref="PagedQuery.DefaultTake" /> items, with <see cref="PagedResult{T}.HasMore" /> saying
/// whether anything was left out. Name the query record when you want a different page, a filter or
/// a total count.
/// </para>
/// </remarks>
public static class SchedulerQueryExtensions
{
    /// <summary>
    /// Lists the first page of jobs.
    /// </summary>
    /// <remarks>
    /// <see cref="IScheduler.QueryJobs" /> with a <see cref="JobQuery" /> that filters nothing, which is
    /// the whole of what the query object says for a caller who only wants "the jobs". Pages like every
    /// other query: <see cref="PagedQuery.DefaultTake" /> items, and <see cref="PagedResult{T}.HasMore" />
    /// reports the rest.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static ValueTask<PagedResult<JobHeader>> QueryJobs(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        return scheduler.QueryJobs(new JobQuery(), cancellationToken);
    }

    /// <summary>
    /// Lists the first page of triggers.
    /// </summary>
    /// <inheritdoc cref="QueryJobs(IScheduler, CancellationToken)" path="/remarks" />
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static ValueTask<PagedResult<TriggerHeader>> QueryTriggers(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        return scheduler.QueryTriggers(new TriggerQuery(), cancellationToken);
    }

    /// <summary>
    /// Lists the first page of firings.
    /// </summary>
    /// <remarks>
    /// <see cref="IScheduler.QueryFireInstances" /> with a <see cref="FireInstanceQuery" /> that filters
    /// nothing further. That record's own default is <see cref="FireInstanceState.Executing" />, so this
    /// is "what is running", cluster-wide on a persistent store.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static ValueTask<PagedResult<FireInstance>> QueryFireInstances(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        return scheduler.QueryFireInstances(new FireInstanceQuery(), cancellationToken);
    }

    /// <summary>
    /// Lists the first page of firings of one job.
    /// </summary>
    /// <remarks>
    /// <see cref="IScheduler.QueryFireInstances" /> with <see cref="FireInstanceQuery.Job" /> set. A
    /// firing that is only <see cref="FireInstanceState.Acquired" /> has no job recorded yet and so is
    /// never selected by a job filter, which is why this lists what is executing rather than everything
    /// in flight.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="jobKey">The job whose firings to list.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static ValueTask<PagedResult<FireInstance>> QueryFireInstancesOfJob(
        this IScheduler scheduler,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(jobKey);

        return scheduler.QueryFireInstances(new FireInstanceQuery { Job = jobKey }, cancellationToken);
    }

    /// <summary>
    /// Lists the first page of triggers that are in <see cref="TriggerState.Error" />.
    /// </summary>
    /// <remarks>
    /// <see cref="IScheduler.QueryTriggers" /> with <see cref="TriggerQuery.State" /> set. Name the
    /// query record instead to count them without reading them —
    /// <c>new TriggerQuery { State = TriggerState.Error, Take = 0, IncludeTotalCount = true }</c> — or to
    /// narrow the listing to one group.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static ValueTask<PagedResult<TriggerHeader>> QueryTriggersInError(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        return scheduler.QueryTriggers(new TriggerQuery { State = TriggerState.Error }, cancellationToken);
    }

    /// <summary>
    /// Resets every trigger in the matching groups that is in <see cref="TriggerState.Error" />, and
    /// returns the keys it moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion of <see cref="QueryTriggersInError" />: it names the set the same way, and hands it
    /// to <see cref="IScheduler.ResetTriggersFromErrorState(IReadOnlyCollection{TriggerKey}, CancellationToken)" />,
    /// which is what decides what resetting a trigger does. Nothing about a trigger's reset changes here
    /// — only how the caller names the set.
    /// </para>
    /// <para>
    /// Two round trips, and the listing between them is deliberately unbounded: the set is "every
    /// trigger in error", and a page of it would silently reset some and leave the rest. They are not
    /// one atomic operation, so a trigger that enters the error state between the two calls is left for
    /// the next one — which is also true of a caller who writes the two calls by hand.
    /// </para>
    /// </remarks>
    /// <param name="scheduler">The scheduler to act on.</param>
    /// <param name="matcher">Limits the reset to triggers whose group matches.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(
        this IScheduler scheduler,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(matcher);

        TriggerQuery query = new() { Group = matcher, State = TriggerState.Error, Take = int.MaxValue };
        PagedResult<TriggerHeader> failed = await scheduler.QueryTriggers(query, cancellationToken).ConfigureAwait(false);
        if (failed.Items.Count == 0)
        {
            return [];
        }

        return await scheduler.ResetTriggersFromErrorState(
            Project(failed.Items, static header => header.Key),
            cancellationToken).ConfigureAwait(false);
    }

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

        // deliberately unbounded: this is the 3.x-compatible listing, and it returns everything
        PagedResult<JobHeader> result = await scheduler.QueryJobs(new JobQuery { Group = matcher, Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        return Project(result.Items, static header => header.Key);
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

        // deliberately unbounded: this is the 3.x-compatible listing, and it returns everything
        PagedResult<TriggerHeader> result = await scheduler.QueryTriggers(new TriggerQuery { Group = matcher, Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        return Project(result.Items, static header => header.Key);
    }

    /// <summary>
    /// Get all <see cref="ITrigger" />s that are associated with the identified
    /// <see cref="IJobDetail" />.
    /// </summary>
    /// <remarks>
    /// Enumerates every trigger of the job, in two steps: the listing that names them, then the
    /// bulk fetch that materializes them. The returned triggers are snapshots of the stored ones;
    /// to modify one you must re-store it (e.g. see <see cref="IScheduler.RescheduleJob" />).
    /// When the fire times and state a listing needs are enough, use
    /// <see cref="IScheduler.QueryTriggers" /> with <see cref="TriggerQuery.Job" /> and skip the
    /// second round trip.
    /// </remarks>
    /// <param name="scheduler">The scheduler to query.</param>
    /// <param name="jobKey">The job whose triggers to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<List<ITrigger>> GetTriggersOfJob(
        this IScheduler scheduler,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(jobKey);

        // deliberately unbounded: this is the 3.x-compatible listing, and it returns everything
        PagedResult<TriggerHeader> result = await scheduler.QueryTriggers(new TriggerQuery { Job = jobKey, Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        if (result.Items.Count == 0)
        {
            return [];
        }

        return await scheduler.GetTriggers(Project(result.Items, static header => header.Key), cancellationToken).ConfigureAwait(false);
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

        // deliberately unbounded: this is the 3.x-compatible listing, and it returns everything
        PagedResult<JobGroup> result = await scheduler.QueryJobGroups(new JobGroupQuery { Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        return Project(result.Items, static group => group.Name);
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

        // deliberately unbounded: this is the 3.x-compatible listing, and it returns everything
        PagedResult<TriggerGroup> result = await scheduler.QueryTriggerGroups(new TriggerGroupQuery { Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        return Project(result.Items, static group => group.Name);
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

        // deliberately unbounded: this is the 3.x-compatible listing, and it returns everything
        PagedResult<TriggerGroup> result = await scheduler.QueryTriggerGroups(new TriggerGroupQuery { Paused = true, Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        return Project(result.Items, static group => group.Name);
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

        // deliberately unbounded: this is the 3.x-compatible listing, and it returns everything
        PagedResult<string> result = await scheduler.QueryCalendarNames(new CalendarQuery { Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
        return Project(result.Items, static name => name);
    }

    /// <summary>
    /// Returns true if the given job group is paused.
    /// </summary>
    /// <remarks>
    /// Asks for the one named group rather than listing every paused one, so the cost does not
    /// grow with the number of groups.
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

        JobGroupQuery query = new() { Name = groupName, Paused = true, Take = 1 };
        PagedResult<JobGroup> result = await scheduler.QueryJobGroups(query, cancellationToken).ConfigureAwait(false);
        return result.Items.Count > 0;
    }

    /// <summary>
    /// Returns true if the given trigger group is paused.
    /// </summary>
    /// <remarks>
    /// Asks for the one named group rather than listing every paused one, so the cost does not
    /// grow with the number of groups.
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

        TriggerGroupQuery query = new() { Name = groupName, Paused = true, Take = 1 };
        PagedResult<TriggerGroup> result = await scheduler.QueryTriggerGroups(query, cancellationToken).ConfigureAwait(false);
        return result.Items.Count > 0;
    }

    /// <summary>
    /// Maps one page of results into a list, without assuming what list type the store handed back.
    /// </summary>
    private static List<TResult> Project<TItem, TResult>(IReadOnlyList<TItem> items, Func<TItem, TResult> selector)
    {
        List<TResult> projected = new(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            projected.Add(selector(items[i]));
        }

        return projected;
    }
}
