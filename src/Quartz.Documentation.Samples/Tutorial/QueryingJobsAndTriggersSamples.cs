namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/querying-jobs-and-triggers.md.
/// </summary>
public static class QueryingJobsAndTriggersSamples
{
    public static async ValueTask QueryingTriggers(IScheduler scheduler)
    {
        #region sample_querying_trigger_query

        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupStartsWith("reporting-"),
            State = TriggerState.Error,
            Take = 50,
        });

        #endregion
    }

    public static void CombiningMatchers(TriggerKey triggerKey)
    {
        #region sample_querying_combining_matchers

        IMatcher<JobKey> notArchived = Matchers.Group<JobKey>(StringOperator.StartsWith, "archive-").Not();
        IMatcher<TriggerKey> either = Matchers.Key(triggerKey).Or(Matchers.AllTriggers());

        #endregion
    }

    public static async ValueTask Paging(IScheduler scheduler, int pageNumber, int pageSize)
    {
        #region sample_querying_paging

        PagedResult<JobHeader> page = await scheduler.QueryJobs(new JobQuery
        {
            Skip = (pageNumber - 1) * pageSize,
            Take = pageSize,
        });

        #endregion
    }

    public static void EverythingInOnePage()
    {
        #region sample_querying_everything

        JobQuery everything = new() { Take = PagedQuery.All };

        #endregion
    }

    public static async ValueTask TotalCount(IScheduler scheduler, int pageSize)
    {
        #region sample_querying_total_count

        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
        {
            Take = pageSize,
            IncludeTotalCount = true,
        });

        int total = page.TotalCount!.Value;   // non-null because IncludeTotalCount was set

        #endregion
    }

    public static async ValueTask CountOnly(IScheduler scheduler)
    {
        #region sample_querying_count_only

        PagedResult<JobHeader> count = await scheduler.QueryJobs(new JobQuery
        {
            Take = 0,
            IncludeTotalCount = true,
        });

        int jobCount = count.TotalCount!.Value;

        #endregion
    }

    public static async ValueTask FromHeadersToDetails(
        IScheduler scheduler,
        PagedResult<JobHeader> page,
        IReadOnlyCollection<TriggerKey> triggerKeys)
    {
        #region sample_querying_headers_to_details

        List<JobKey> keys = page.Items.Select(h => h.Key).ToList();
        List<IJobDetail> details = await scheduler.GetJobDetails(keys);

        List<ITrigger> triggers = await scheduler.GetTriggers(triggerKeys);

        #endregion
    }

    public static async ValueTask QueryingFireInstances(IScheduler scheduler)
    {
        #region sample_querying_fire_instances

        PagedResult<FireInstance> running = await scheduler.QueryFireInstances(new FireInstanceQuery
        {
            TriggerGroup = GroupMatcher<TriggerKey>.GroupEquals("reporting"),
        });

        foreach (FireInstance fire in running.Items)
        {
            Console.WriteLine($"{fire.TriggerKey} on {fire.SchedulerInstanceId} since {fire.FireTimeUtc:O}");
        }

        #endregion
    }

    public static async ValueTask Shorthands(IScheduler scheduler, JobKey jobKey)
    {
        #region sample_querying_shorthands

        PagedResult<JobHeader> jobs = await scheduler.QueryJobs(new JobQuery());
        PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggers(new TriggerQuery());
        PagedResult<FireInstance> running = await scheduler.QueryFireInstances(new FireInstanceQuery());
        PagedResult<FireInstance> runningOneJob = await scheduler.QueryFireInstances(new FireInstanceQuery { Job = jobKey });

        // the one shorthand that is a preset rather than a synonym: it knows the filter
        PagedResult<TriggerHeader> failed = await scheduler.QueryTriggersInError();

        #endregion
    }

    public static async ValueTask ResettingAGroupFromError(IScheduler scheduler)
    {
        #region sample_querying_reset_group_from_error

        List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(
            GroupMatcher<TriggerKey>.GroupEquals("imports"));

        #endregion
    }

    public static async ValueTask CalendarExists(IScheduler scheduler)
    {
        #region sample_querying_calendar_exists

        bool haveHolidays = await scheduler.Exists("holidays");

        #endregion
    }

    public static void ReservedVersusRunning()
    {
        #region sample_querying_fire_instance_state

        FireInstanceQuery reservedAndRunning = new() { State = null };
        FireInstanceQuery reservedOnly = new() { State = FireInstanceState.Acquired };

        #endregion
    }

    public static async ValueTask PausingAGroup(IScheduler scheduler)
    {
        #region sample_querying_pause_triggers

        List<string> pausedGroups = await scheduler.PauseTriggerGroups(
            GroupMatcher<TriggerKey>.GroupStartsWith("nightly-"));

        #endregion
    }
}

#region sample_querying_trigger_list_model

public sealed class TriggerListModel(IScheduler scheduler)
{
    public async Task<(IReadOnlyList<TriggerHeader> Rows, int Total)> GetPage(
        int pageNumber,
        int pageSize,
        TriggerState? state,
        string? groupPrefix,
        CancellationToken cancellationToken)
    {
        TriggerQuery query = new()
        {
            Skip = (pageNumber - 1) * pageSize,
            Take = pageSize,
            IncludeTotalCount = true,
            State = state,
            Group = groupPrefix is null
                ? null
                : GroupMatcher<TriggerKey>.GroupStartsWith(groupPrefix),
        };

        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(query, cancellationToken);
        return (page.Items, page.TotalCount ?? page.Items.Count);
    }

    public ValueTask<List<ITrigger>> Expand(
        IReadOnlyCollection<TriggerKey> keys,
        CancellationToken cancellationToken) =>
        scheduler.GetTriggers(keys, cancellationToken);
}

#endregion
