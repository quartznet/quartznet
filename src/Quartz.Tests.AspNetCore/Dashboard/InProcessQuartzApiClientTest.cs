using System.Collections.Specialized;

using Microsoft.Extensions.Options;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class InProcessQuartzApiClientTest
{
    [Test]
    public async Task RescheduleJobStoresTheTriggerItWasGiven()
    {
        // #3094 was this client failing to parse a trigger out of JSON. In-process there is no JSON:
        // the request carries the trigger, and the client hands it to the scheduler as it stands.
        IScheduler scheduler = await CreateScheduler("RescheduleJobTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity(jobKey)
                .StoreDurably()
                .Build();
            TriggerKey triggerKey = new("trigger1", "group1");
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .WithCronSchedule("0 0 1 * * ?")
                .Build();
            await scheduler.ScheduleJob(job, trigger);

            InProcessQuartzApiClient client = CreateClient(scheduler);

            ITrigger replacement = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .WithDescription("updated by dashboard")
                .WithExecutionGroup("imports")
                .WithCronSchedule("0 0 2 * * ?")
                .Build();

            await client.RescheduleJob(scheduler.SchedulerName, new TriggerKeyDto(triggerKey.Group, triggerKey.Name), new RescheduleRequest(replacement));

            ITrigger? updated = await scheduler.GetTrigger(triggerKey);
            CronTriggerImpl cronTrigger = updated.Should().BeOfType<CronTriggerImpl>().Subject;
            cronTrigger.CronExpressionString.Should().Be("0 0 2 * * ?");
            cronTrigger.JobKey.Should().Be(jobKey);
            cronTrigger.Description.Should().Be("updated by dashboard");
            cronTrigger.ExecutionGroup.Should().Be("imports");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task RescheduleFromTriggerDetailPayloadChangesNothingButTheSchedule()
    {
        // regression test for #3294 - the detail page rebuilt the trigger from its display strings,
        // so a trigger with no calendar came back with CALENDAR_NAME='', which every job store reads
        // as a calendar it then cannot find. The trigger silently never fired again. The node pin
        // was dropped outright, because the hand-written payload never listed it.
        IScheduler scheduler = await CreateScheduler("RescheduleRoundTripTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity(jobKey)
                .StoreDurably()
                .Build();
            TriggerKey triggerKey = new("trigger1", "group1");
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .WithCronSchedule("0 0 1 * * ?")
                .WithPreferredNode(PreferredNode.For("node-a"))
                .Build();
            await scheduler.ScheduleJob(job, trigger);

            InProcessQuartzApiClient client = CreateClient(scheduler);

            // exactly what TriggerDetail.razor does: read the trigger, then edit its cron expression
            ITrigger detail = await client.GetTrigger(scheduler.SchedulerName, new TriggerKeyDto(triggerKey.Group, triggerKey.Name));
            TriggerPayloadBuilder.TryWithCronExpression(detail, "0 0 2 * * ?", out ITrigger? newTrigger)
                .Should().BeTrue();

            await client.RescheduleJob(scheduler.SchedulerName, new TriggerKeyDto(triggerKey.Group, triggerKey.Name), new RescheduleRequest(newTrigger!));

            ITrigger? updated = await scheduler.GetTrigger(triggerKey);
            CronTriggerImpl cronTrigger = updated.Should().BeOfType<CronTriggerImpl>().Subject;
            cronTrigger.CronExpressionString.Should().Be("0 0 2 * * ?");
            cronTrigger.JobKey.Should().Be(jobKey);
            cronTrigger.CalendarName.Should().BeNull(
                "the trigger never had a calendar, and an empty name would name one that cannot be found");
            cronTrigger.Description.Should().BeNull();
            cronTrigger.PreferredNode.Node.Should().Be("node-a", "editing a schedule must not unpin the trigger");
            cronTrigger.NextFireTimeUtc.Should().NotBeNull("a rescheduled trigger has to keep firing");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task AddCalendarStoresTheCalendarItWasGiven()
    {
        IScheduler scheduler = await CreateScheduler("AddCalendarTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler);

            // the calendar Calendars.razor builds
            CronCalendar calendar = new(baseCalendar: null, "0 0 3 * * ?", TimeZoneInfo.Utc)
            {
                Description = "maintenance window"
            };
            AddCalendarRequest request = new("maintenance", calendar, Replace: false, UpdateTriggers: false);

            await client.AddCalendar(scheduler.SchedulerName, request);

            ICalendar? stored = await scheduler.GetCalendar("maintenance");
            CronCalendar cronCalendar = stored.Should().BeOfType<CronCalendar>().Subject;
            cronCalendar.CronExpression.CronExpressionString.Should().Be("0 0 3 * * ?");
            cronCalendar.Description.Should().Be("maintenance window");

            // and the detail page reads the calendar itself back, not a rendering of one
            ICalendar readBack = await client.GetCalendar(scheduler.SchedulerName, "maintenance");
            readBack.Should().BeOfType<CronCalendar>()
                .Which.CronExpression.CronExpressionString.Should().Be("0 0 3 * * ?");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetJobReturnsTheJobDataMapWithItsValuesAsTheyWereStored()
    {
        // #3130 was the detail page casting a JsonElement to JobDataMap and always getting null. The
        // map is a JobDataMap on the way out now, so an int is an int and a bool is a bool - reading
        // it back out of JSON turned every value into whatever the reader guessed.
        IScheduler scheduler = await CreateScheduler("GetJobDataMapTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity(jobKey)
                .UsingJobData("Name", "abc")
                .UsingJobData("Count", 5)
                .UsingJobData("Enabled", true)
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, new AddJobOptions { Replace = true });

            InProcessQuartzApiClient client = CreateClient(scheduler);
            JobDetailDto dto = await client.GetJobDetail(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name));

            dto.JobDataMap["Name"].Should().Be("abc");
            dto.JobDataMap["Count"].Should().Be(5);
            dto.JobDataMap["Enabled"].Should().Be(true);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetTriggerReturnsTheTriggerItself()
    {
        // #3130 was the detail page missing type, schedule and job data because the trigger had been
        // reflected into JSON. There is no JSON in the way now: the page gets the trigger, and reads
        // the schedule off the interface its kind implements.
        IScheduler scheduler = await CreateScheduler("GetSimpleTriggerTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build();
            TriggerKey triggerKey = new("trigger1", "group1");
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .UsingJobData("Color", "red")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).WithRepeatCount(3))
                .Build();
            await scheduler.ScheduleJob(job, trigger);

            InProcessQuartzApiClient client = CreateClient(scheduler);
            ITrigger detail = await client.GetTrigger(scheduler.SchedulerName, new TriggerKeyDto(triggerKey.Group, triggerKey.Name));

            ISimpleTrigger simple = detail.Should().BeAssignableTo<ISimpleTrigger>().Subject;
            simple.RepeatInterval.Should().Be(TimeSpan.FromSeconds(30));
            simple.RepeatCount.Should().Be(3);
            detail.JobDataMap["Color"].Should().Be("red");
            TriggerDisplay.TypeName(detail).Should().Be("Simple");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetJobTriggersPopulatesTypeAndScheduleSummary()
    {
        // regression test for #3130 - the associated triggers table now shows each trigger's type
        // and a schedule summary, so SimpleSchedule triggers are no longer indistinguishable.
        IScheduler scheduler = await CreateScheduler("GetJobTriggersTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build();
            await scheduler.ScheduleJob(
                job,
                TriggerBuilder.Create().WithIdentity("simple", "group1").ForJob(jobKey)
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).WithRepeatCount(2)).Build());
            await scheduler.ScheduleJob(
                TriggerBuilder.Create().WithIdentity("cron", "group1").ForJob(jobKey)
                    .WithCronSchedule("0 0 1 * * ?").Build());

            InProcessQuartzApiClient client = CreateClient(scheduler);
            List<TriggerHeaderDto> headers = await client.GetTriggersOfJob(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name));

            headers.Should().HaveCount(2);
            TriggerHeaderDto simple = headers.Single(h => h.Name == "simple");
            simple.TriggerType.Should().Be("Simple");
            simple.ScheduleSummary.Should().Contain("Every").And.Contain("time(s)");
            TriggerHeaderDto cron = headers.Single(h => h.Name == "cron");
            cron.TriggerType.Should().Be("Cron");
            cron.ScheduleSummary.Should().Be("0 0 1 * * ?");

            headers.Should().AllSatisfy(
                header => header.State.Should().Be(TriggerState.Normal),
                "the states come from the single trigger query the associated triggers table used to make one call per trigger for");

            await scheduler.PauseTrigger(new TriggerKey("cron", "group1"));
            headers = await client.GetTriggersOfJob(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name));

            headers.Single(h => h.Name == "cron").State.Should().Be(TriggerState.Paused, "each header carries its own trigger's state");
            headers.Single(h => h.Name == "simple").State.Should().Be(TriggerState.Normal);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetJobsPagesServerSideAndCountsExactly()
    {
        // #3008 - the jobs page fetched every key and paged in the browser
        IScheduler scheduler = await CreateScheduler("GetJobsPagingTest");
        try
        {
            for (int i = 1; i <= 30; i++)
            {
                await scheduler.AddJob(
                    JobBuilder.Create<NoOpJob>()
                        .WithIdentity("job" + i.ToString("00"), "group1")
                        .StoreDurably()
                        .Build(), new AddJobOptions { Replace = true });
            }

            InProcessQuartzApiClient client = CreateClient(scheduler);

            PagedResult<JobKeyDto> firstPage = await client.QueryJobs(scheduler.SchedulerName, new DashboardJobQuery { Take = 25 });
            firstPage.Items.Should().HaveCount(25, "the page size limits what the store returns");
            firstPage.TotalCount.Should().Be(30, "the total is counted regardless of paging");
            firstPage.HasMore.Should().BeTrue("30 jobs do not fit on one page of 25");
            firstPage.Items[0].Name.Should().Be("job01", "results are ordered by group and then name");

            PagedResult<JobKeyDto> secondPage = await client.QueryJobs(scheduler.SchedulerName, new DashboardJobQuery { Skip = 25, Take = 25 });
            secondPage.Items.Should().HaveCount(5, "the second page holds the remainder");
            secondPage.Items.Select(x => x.Name).Should().Equal(["job26", "job27", "job28", "job29", "job30"],
                "page 2 continues where page 1 ended");
            secondPage.HasMore.Should().BeFalse("nothing matches beyond the second page");
            secondPage.TotalCount.Should().Be(30);

            PagedResult<JobKeyDto> countOnly = await client.QueryJobs(scheduler.SchedulerName, new DashboardJobQuery { Take = 0 });
            countOnly.Items.Should().BeEmpty("a page size of zero fetches no items");
            countOnly.TotalCount.Should().Be(30, "the dashboard total jobs tile is a count query, not a materialized list");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetJobsFiltersByGroup()
    {
        IScheduler scheduler = await CreateScheduler("GetJobsGroupFilterTest");
        try
        {
            await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("job1", "imports").StoreDurably().Build(), new AddJobOptions { Replace = true });
            await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("job2", "reports").StoreDurably().Build(), new AddJobOptions { Replace = true });

            InProcessQuartzApiClient client = CreateClient(scheduler);

            PagedResult<JobKeyDto> filtered = await client.QueryJobs(scheduler.SchedulerName, new DashboardJobQuery { GroupContains = "mpor", Take = 25 });

            filtered.Items.Should().ContainSingle("the group filter matches groups that contain it")
                .Which.Group.Should().Be("imports");
            filtered.TotalCount.Should().Be(1, "the total counts the filtered set, not everything");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetTriggersPagesServerSideAndCarriesStateAndExecutionGroup()
    {
        // #3008 - the triggers page fetched every key and then one state per trigger
        IScheduler scheduler = await CreateScheduler("GetTriggersPagingTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build(), new AddJobOptions { Replace = true });

            for (int i = 1; i <= 30; i++)
            {
                await scheduler.ScheduleJob(
                    TriggerBuilder.Create()
                        .WithIdentity("trigger" + i.ToString("00"), "group1")
                        .ForJob(jobKey)
                        .WithExecutionGroup("imports")
                        .WithCronSchedule("0 0 1 * * ?")
                        .Build());
            }

            for (int i = 1; i <= 26; i++)
            {
                await scheduler.PauseTrigger(new TriggerKey("trigger" + i.ToString("00"), "group1"));
            }

            InProcessQuartzApiClient client = CreateClient(scheduler);

            PagedResult<TriggerHeaderDto> firstPage = await client.QueryTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Take = 25 });
            firstPage.Items.Should().HaveCount(25);
            firstPage.TotalCount.Should().Be(30);
            firstPage.HasMore.Should().BeTrue("30 triggers do not fit on one page of 25");
            firstPage.Items[0].State.Should().Be(TriggerState.Paused, "the header carries the state the listing used to fetch per trigger");
            firstPage.Items[0].ExecutionGroup.Should().Be("imports", "the header carries the execution group without loading the trigger");

            PagedResult<TriggerHeaderDto> secondPage = await client.QueryTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Skip = 25, Take = 25 });
            secondPage.Items.Select(x => x.Name).Should().Equal(["trigger26", "trigger27", "trigger28", "trigger29", "trigger30"],
                "page 2 continues where page 1 ended");
            secondPage.HasMore.Should().BeFalse();
            secondPage.Items[^1].State.Should().Be(TriggerState.Normal, "the last four triggers were never paused");

            PagedResult<TriggerHeaderDto> pausedCount = await client.QueryTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Take = 0, State = TriggerState.Paused });
            pausedCount.TotalCount.Should().Be(26,
                "a state-filtered count is exact, where the dashboard tile used to count states over the first 25 items only");

            PagedResult<TriggerHeaderDto> errorCount = await client.QueryTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Take = 0, State = TriggerState.Error });
            errorCount.Items.Should().BeEmpty();
            errorCount.TotalCount.Should().Be(0, "no trigger has failed, and the error tile reports that exactly");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task ScheduleJobStoresTheJobAndTheTriggerItWasGiven()
    {
        IScheduler scheduler = await CreateScheduler("ScheduleJobTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler);

            JobDetailDto job = new(
                Name: "job1",
                Group: "group1",
                JobType: typeof(NoOpJob).FullName!,
                Description: "scheduled from the dashboard",
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: false,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap { ["colour"] = "green" });

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .ForJob("job1", "group1")
                .WithCronSchedule("0 0 1 * * ?")
                .Build();

            await client.ScheduleJob(scheduler.SchedulerName, new ScheduleJobRequest(trigger, job));

            IJobDetail? stored = await scheduler.GetJobDetail(new JobKey("job1", "group1"));
            stored.Should().NotBeNull();
            stored!.Description.Should().Be("scheduled from the dashboard");
            stored.JobDataMap["colour"].Should().Be("green", "the map travels as a JobDataMap, not as re-parsed JSON");
            (await scheduler.GetTrigger(new TriggerKey("trigger1", "group1"))).Should().NotBeNull();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task ScheduleJobWithNoJobSchedulesTheTriggerAgainstAStoredJob()
    {
        IScheduler scheduler = await CreateScheduler("ScheduleTriggerOnlyTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            await scheduler.AddJob(
                JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build(),
                new AddJobOptions { Replace = true });

            InProcessQuartzApiClient client = CreateClient(scheduler);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .ForJob(jobKey)
                .WithCronSchedule("0 0 1 * * ?")
                .Build();

            await client.ScheduleJob(scheduler.SchedulerName, new ScheduleJobRequest(trigger, Job: null));

            (await scheduler.GetTrigger(new TriggerKey("trigger1", "group1"))).Should().NotBeNull(
                "a request with no job detail schedules against the job already stored");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task TriggerJobWithDataPassesTheOverridesStraightThrough()
    {
        IScheduler scheduler = await CreateScheduler("TriggerWithDataTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            await scheduler.AddJob(
                JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build(),
                new AddJobOptions { Replace = true });

            InProcessQuartzApiClient client = CreateClient(scheduler);

            // in process there is no serializer between the page and the scheduler, so a value keeps
            // the type it was given rather than the one a JSON reader would have guessed
            JobDataMap overrides = new() { ["Count"] = 5 };
            await client.TriggerJob(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name), overrides);

            PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggers(new TriggerQuery { Job = jobKey });
            triggers.Items.Should().ContainSingle("triggering a job now schedules the one-off trigger that fires it");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetCalendarThrowsForACalendarThatIsNotThere()
    {
        IScheduler scheduler = await CreateScheduler("GetMissingCalendarTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler);

            Func<Task> act = async () => await client.GetCalendar(scheduler.SchedulerName, "no-such-calendar");

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("*no-such-calendar*",
                    "the detail page shows the message, so it has to name what was missing");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetJobGroupsReportsPausedStateInOneCall()
    {
        // #3008 - the jobs page asked IsJobGroupPaused once per group
        IScheduler scheduler = await CreateScheduler("GetJobGroupsTest");
        try
        {
            await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("job1", "paused").StoreDurably().Build(), new AddJobOptions { Replace = true });
            await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("job2", "running").StoreDurably().Build(), new AddJobOptions { Replace = true });
            await scheduler.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("paused"));

            InProcessQuartzApiClient client = CreateClient(scheduler);
            PagedResult<JobGroupDto> groups = await client.QueryJobGroups(scheduler.SchedulerName, new DashboardGroupQuery());

            groups.Items.Select(x => x.Name).Should().Contain(["paused", "running"], "every job group is listed");
            groups.Items.Single(x => x.Name == "paused").Paused.Should().BeTrue("the group was paused");
            groups.Items.Single(x => x.Name == "running").Paused.Should().BeFalse("the other group was not");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The paused-groups tile counts with the paused filter rather than by listing every group, and
    /// <c>Take = 0</c> is what turns that filter into a count.
    /// </summary>
    /// <remarks>
    /// The distinction is not academic: a group can be paused while it holds nothing, and the unfiltered
    /// listing enumerates the groups that hold something, so tallying it would miss exactly the group an
    /// operator most needs to be told about — one that was paused and then emptied.
    /// </remarks>
    [Test]
    public async Task GroupListingsCountThePausedOnesWithoutListingThem()
    {
        IScheduler scheduler = await CreateScheduler("PausedGroupCountTest");
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "reports").StoreDurably().Build();
            await scheduler.AddJob(job, new AddJobOptions { Replace = true });
            await scheduler.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity("trigger1", "nightly")
                .ForJob(job)
                .WithCronSchedule("0 0 1 * * ?")
                .Build());

            await scheduler.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("reports"));
            await scheduler.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("nightly"));
            await scheduler.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("empty-and-paused"));

            InProcessQuartzApiClient client = CreateClient(scheduler);

            PagedResult<TriggerGroupDto> pausedTriggerGroups = await client.QueryTriggerGroups(
                scheduler.SchedulerName,
                new DashboardGroupQuery { Take = 0, Paused = true });
            PagedResult<JobGroupDto> pausedJobGroups = await client.QueryJobGroups(
                scheduler.SchedulerName,
                new DashboardGroupQuery { Take = 0, Paused = true });

            pausedTriggerGroups.Items.Should().BeEmpty("Take = 0 is a count, not a page");
            pausedTriggerGroups.TotalCount.Should().Be(2,
                "a trigger group that was paused while empty is still paused, and only the filtered "
                + "query reports it");
            pausedJobGroups.TotalCount.Should().Be(1);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetTriggerGroupsReportsPausedStateInOneCall()
    {
        IScheduler scheduler = await CreateScheduler("GetTriggerGroupsTest");
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job1", "jobs").StoreDurably().Build();
            await scheduler.AddJob(job, new AddJobOptions { Replace = true });
            await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("t1", "paused").ForJob(job).WithCronSchedule("0 0 1 * * ?").Build());
            await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("t2", "running").ForJob(job).WithCronSchedule("0 0 2 * * ?").Build());
            await scheduler.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("paused"));

            InProcessQuartzApiClient client = CreateClient(scheduler);
            PagedResult<TriggerGroupDto> groups = await client.QueryTriggerGroups(scheduler.SchedulerName, new DashboardGroupQuery());

            groups.Items.Select(x => x.Name).Should().Contain(["paused", "running"], "every trigger group is listed");
            groups.Items.Single(x => x.Name == "paused").Paused.Should().BeTrue("the group was paused");
            groups.Items.Single(x => x.Name == "running").Paused.Should().BeFalse("the other group was not");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The dashboard reads a limit's scope as well as its number, so a cluster-wide quota is not shown
    /// as a per-node one.
    /// </summary>
    [Test]
    public async Task GetExecutionLimitsShouldReportEachLimitsScope()
    {
        IScheduler scheduler = await CreateScheduler("GetExecutionLimitsTest");
        try
        {
            await scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create()
                .ForGroup("batch", 2)
                .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster)
                .ForDefaultGroup(3)
                .Build());

            InProcessQuartzApiClient client = CreateClient(scheduler);

            ExecutionLimitsDto limits = await client.GetExecutionLimits(scheduler.SchedulerName);

            limits.CanReport.Should().BeTrue();
            limits.Limits.Should().BeEquivalentTo(new Dictionary<string, DashboardExecutionLimit>
            {
                ["batch"] = new(2, ExecutionLimitScope.Node),
                ["tenant-acme"] = new(8, ExecutionLimitScope.Cluster),
                ["_"] = new(3, ExecutionLimitScope.Node),
            }, "the keys are the spellings configuration and the HTTP API use, which is what lets the "
               + "overview join a firing's execution group to the limit governing it");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetExecutionLimitsCarriesTheTriggerGroupDerivation()
    {
        IScheduler scheduler = await CreateScheduler("GetExecutionLimitsDerivationTest");
        try
        {
            await scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create()
                .ForOtherGroups(4)
                .UseTriggerGroupWhenUnset()
                .Build());

            InProcessQuartzApiClient client = CreateClient(scheduler);

            ExecutionLimitsDto limits = await client.GetExecutionLimits(scheduler.SchedulerName);

            limits.UsesTriggerGroupWhenUnset.Should().BeTrue(
                "the overview has to apply the same derivation when it counts what is in flight, or its "
                + "counts and the acquisition filter would key the same firing differently");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetExecutionLimitsIsEmptyRatherThanUnknownWhenNothingIsLimited()
    {
        IScheduler scheduler = await CreateScheduler("GetExecutionLimitsEmptyTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler);

            ExecutionLimitsDto limits = await client.GetExecutionLimits(scheduler.SchedulerName);

            limits.Limits.Should().BeEmpty("nothing is limited");
            limits.CanReport.Should().BeTrue(
                "a scheduler that limits nothing has answered the question, and the overview says every "
                + "group is unlimited rather than that it cannot tell");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A scheduler that refuses the question is a different answer from one that limits nothing, and the
    /// client no longer flattens the two into a bare null.
    /// </summary>
    [Test]
    public async Task GetExecutionLimitsSaysSoWhenTheSchedulerCannotReportThem()
    {
        IScheduler scheduler = await CreateScheduler("GetExecutionLimitsUnsupportedTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(new LimitlessScheduler(scheduler));

            ExecutionLimitsDto limits = await client.GetExecutionLimits(scheduler.SchedulerName);

            limits.CanReport.Should().BeFalse(
                "reporting a refusal as 'nothing is limited' would have the panel state a fact nobody "
                + "established");
            limits.Limits.Should().BeEmpty();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// An <see cref="IScheduler" /> of an application's own that does not implement execution limits.
    /// </summary>
    private sealed class LimitlessScheduler : DelegatingScheduler
    {
        public LimitlessScheduler(IScheduler scheduler) : base(scheduler)
        {
        }

        public override ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This scheduler does not implement execution limits.");
        }
    }

    /// <summary>
    /// The node listing reaches the scheduler rather than being answered by the dashboard.
    /// </summary>
    /// <remarks>
    /// In-process the scheduler here is backed by the in-memory store, so the honest answer is one node
    /// with no check-in history.
    /// </remarks>
    [Test]
    public async Task ClusterNodesComeFromTheScheduler()
    {
        IScheduler scheduler = await CreateScheduler("ClusterNodesTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler);

            List<ClusterNodeDto> nodes = await client.QueryClusterNodes(scheduler.SchedulerName);

            ClusterNodeDto node = nodes.Should().ContainSingle().Subject;
            node.InstanceId.Should().Be(scheduler.SchedulerInstanceId);
            node.IsCurrentNode.Should().BeTrue();
            node.State.Should().Be(ClusterNodeState.Alive);
            node.LastCheckInUtc.Should().BeNull();
            node.CheckInInterval.Should().BeNull();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The scheduler detail carries what the scheduler is made of, not only its name and its state.
    /// </summary>
    /// <remarks>
    /// Everything <see cref="SchedulerMetadata" /> knows used to be dropped on the way to the UI, so the
    /// dashboard could not say whether a scheduler was clustered, what store it used, or how many
    /// threads it had. The values are read from the scheduler rather than assembled here, which is why
    /// this asserts against its own metadata rather than against literals.
    /// </remarks>
    [Test]
    public async Task TheSchedulerDetailCarriesTheMetadataTheSchedulerReports()
    {
        IScheduler scheduler = await CreateScheduler("SchedulerDetailTest");
        try
        {
            await scheduler.Start();
            InProcessQuartzApiClient client = CreateClient(scheduler);

            SchedulerMetadata metadata = await scheduler.GetMetadata();
            SchedulerDetailDto detail = await client.GetScheduler(scheduler.SchedulerName);

            detail.SchedulerName.Should().Be(scheduler.SchedulerName);
            detail.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId);
            detail.Status.Should().Be(SchedulerStatus.Running);
            detail.Clustered.Should().BeFalse("an in-memory store has no cluster to be part of");
            detail.Persistent.Should().BeFalse();
            detail.JobStoreTypeName.Should().Be(metadata.JobStoreTypeName);
            detail.ThreadPoolTypeName.Should().Be(metadata.ThreadPoolTypeName);
            detail.ThreadPoolSize.Should().Be(1, "the scheduler was built with one thread");
            detail.RunningSince.Should().NotBeNull("the scheduler has been started");
            detail.JobsExecuted.Should().Be(metadata.JobsExecuted);
            detail.Version.Should().Be(metadata.Version);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The listing is the registrations, so a tenant nothing has built is in it — with no status and no
    /// instance id, because there is no scheduler to have either.
    /// </summary>
    [Test]
    public async Task TheListingCarriesRegistrationsNothingHasBuilt()
    {
        IScheduler scheduler = await CreateScheduler("SchedulerListingTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler, "acme");

            List<SchedulerHeaderDto> schedulers = await client.GetSchedulers();

            SchedulerHeaderDto built = schedulers.Should().ContainSingle(x => x.SchedulerName == scheduler.SchedulerName).Subject;
            built.IsCreated.Should().BeTrue();
            built.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId,
                "the registration does not carry an instance id, so the repository is asked for the one "
                + "scheduler that has one");

            SchedulerHeaderDto registered = schedulers.Should().ContainSingle(x => x.SchedulerName == "acme").Subject;
            registered.IsCreated.Should().BeFalse();
            registered.Status.Should().BeNull("null is what says the registration is there and nothing has built it");
            registered.SchedulerInstanceId.Should().BeNull("there is no scheduler to have one");
            registered.Origin.Should().Be(SchedulerOrigin.Container);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The history feeds answer for themselves. A store holding nothing has an empty page and a count of
    /// zero, which is the only "no history" answer there is: the nullable returns these replace meant
    /// "this data source keeps no history", which was the deleted HTTP-backed client's 404 and never a
    /// thing an in-process store could say.
    /// </summary>
    [Test]
    public async Task TheHistoryFeedsAnswerWithAPageEvenWhenNothingWasRecorded()
    {
        IScheduler scheduler = await CreateScheduler(nameof(TheHistoryFeedsAnswerWithAPageEvenWhenNothingWasRecorded));
        try
        {
            DashboardHistoryStore store = TestData.Dashboard.HistoryStore();
            InProcessQuartzApiClient client = CreateClient(scheduler, store);
            DashboardHistoryQuery historyQuery = new() { SchedulerName = scheduler.SchedulerName };
            DashboardMisfireQuery misfireQuery = new() { SchedulerName = scheduler.SchedulerName };

            PagedResult<DashboardHistoryEntry> executions = await client.QueryExecutions(historyQuery);
            PagedResult<DashboardMisfireEntry> misfires = await client.QueryMisfires(misfireQuery);
            int misfireCount = await client.CountMisfires(scheduler.SchedulerName, DateTimeOffset.MinValue);

            executions.Items.Should().BeEmpty("nothing has run, which is an empty page rather than no page");
            misfires.Items.Should().BeEmpty("nothing has misfired, which is an empty page rather than no page");
            misfireCount.Should().Be(0);

            await store.AddExecution(new DashboardHistoryEntry(
                SchedulerName: scheduler.SchedulerName,
                SchedulerInstanceId: scheduler.SchedulerInstanceId,
                JobGroup: "reports",
                JobName: "rollup",
                TriggerGroup: "nightly",
                TriggerName: "midnight",
                FiredAtUtc: DateTimeOffset.UtcNow,
                Duration: TimeSpan.FromSeconds(1),
                Succeeded: true,
                ExceptionMessage: null));

            executions = await client.QueryExecutions(historyQuery);

            executions.Items.Should().ContainSingle("the client reads the store it was given, unchanged")
                .Which.SchedulerName.Should().Be(scheduler.SchedulerName);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Every mutation that can find nothing to act on hands back the scheduler's own answer.
    /// </summary>
    /// <remarks>
    /// Five of these ten used to discard it with a literal <c>_ =</c>, so a dashboard could not tell a
    /// deletion from a job a cluster peer had already removed. The pairs are asserted together because
    /// the flag is only worth anything if both answers reach the caller.
    /// </remarks>
    [Test]
    public async Task EveryMutationSaysWhetherItFoundAnythingToActOn()
    {
        IScheduler scheduler = await CreateScheduler("AppliedFlagTest");
        try
        {
            JobKey jobKey = new("job1", "group1");
            TriggerKey triggerKey = new("trigger1", "group1");
            await scheduler.ScheduleJob(
                JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).WithCronSchedule("0 0 1 * * ?").Build());
            await scheduler.AddCalendar("holidays", new HolidayCalendar(), new AddCalendarOptions());

            InProcessQuartzApiClient client = CreateClient(scheduler);
            string name = scheduler.SchedulerName;
            JobKeyDto job = new(jobKey.Group, jobKey.Name);
            TriggerKeyDto trigger = new(triggerKey.Group, triggerKey.Name);

            (await client.Interrupt(name, job)).Should().BeFalse(
                "the job is scheduled but not running, so there is no execution to interrupt");
            (await client.InterruptFireInstance(name, "no-such-fire-instance")).Should().BeFalse();

            (await client.UnscheduleJob(name, trigger)).Should().BeTrue("the trigger was there");
            (await client.UnscheduleJob(name, trigger)).Should().BeFalse(
                "the second call finds nothing, which is what a second operator clicking the same button does");

            (await client.DeleteCalendar(name, "holidays")).Should().BeTrue();
            (await client.DeleteCalendar(name, "holidays")).Should().BeFalse();

            (await client.DeleteJob(name, job)).Should().BeFalse(
                "unscheduling the only trigger of a non-durable job took the job with it");
            (await client.DeleteJob(name, new JobKeyDto("group1", "never-existed"))).Should().BeFalse();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static async Task<IScheduler> CreateScheduler(string testName)
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = testName + "_" + Guid.NewGuid().ToString("N"),
            ["quartz.threadPool.threadCount"] = "1",
            ["quartz.serializer.type"] = "stj"
        };
        return await QuartzSchedulerBuilder.Create().UseProperties(properties).BuildScheduler();
    }

    private static InProcessQuartzApiClient CreateClient(IScheduler scheduler, params string[] registeredButNotCreated)
    {
        return CreateClient(scheduler, TestData.Dashboard.HistoryStore(), registeredButNotCreated);
    }

    private static InProcessQuartzApiClient CreateClient(
        IScheduler scheduler,
        IDashboardHistoryStore historyStore,
        params string[] registeredButNotCreated)
    {
        SchedulerRepository repository = new();
        repository.Bind(scheduler);
        return new InProcessQuartzApiClient(
            repository,
            new StubSchedulerRegistry(repository, registeredButNotCreated),
            Options.Create(new QuartzDashboardOptions()),
            historyStore);
    }

    /// <summary>
    /// A registry over a repository plus the names nothing has built, which is the shape
    /// <c>ContainerSchedulerRegistry</c> answers with. The real one needs a container to be told what was
    /// registered; what this client does with the answer is what these tests are about.
    /// </summary>
    private sealed class StubSchedulerRegistry : ISchedulerRegistry
    {
        private readonly ISchedulerRepository repository;
        private readonly IReadOnlyList<string> registeredButNotCreated;

        public StubSchedulerRegistry(ISchedulerRepository repository, IReadOnlyList<string> registeredButNotCreated)
        {
            this.repository = repository;
            this.registeredButNotCreated = registeredButNotCreated;
        }

        public ValueTask<List<SchedulerRegistration>> QuerySchedulers(CancellationToken cancellationToken = default)
        {
            List<SchedulerRegistration> registrations = [];
            foreach (IScheduler scheduler in repository.LookupAll())
            {
                registrations.Add(new SchedulerRegistration(scheduler.SchedulerName, SchedulerOrigin.Container, scheduler.Status));
            }

            foreach (string name in registeredButNotCreated)
            {
                registrations.Add(new SchedulerRegistration(name, SchedulerOrigin.Container, Status: null));
            }

            registrations.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
            return new ValueTask<List<SchedulerRegistration>>(registrations);
        }
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
