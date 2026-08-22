using System.Collections.Specialized;

using Microsoft.Extensions.Options;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

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
            JobDetailDto dto = await client.GetJob(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name));

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
            List<TriggerHeaderDto> headers = await client.GetJobTriggers(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name));

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
            headers = await client.GetJobTriggers(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name));

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

            PagedResult<JobKeyDto> firstPage = await client.GetJobs(scheduler.SchedulerName, new DashboardJobQuery { Take = 25 });
            firstPage.Items.Should().HaveCount(25, "the page size limits what the store returns");
            firstPage.TotalCount.Should().Be(30, "the total is counted regardless of paging");
            firstPage.HasMore.Should().BeTrue("30 jobs do not fit on one page of 25");
            firstPage.Items[0].Name.Should().Be("job01", "results are ordered by group and then name");

            PagedResult<JobKeyDto> secondPage = await client.GetJobs(scheduler.SchedulerName, new DashboardJobQuery { Skip = 25, Take = 25 });
            secondPage.Items.Should().HaveCount(5, "the second page holds the remainder");
            secondPage.Items.Select(x => x.Name).Should().Equal(["job26", "job27", "job28", "job29", "job30"],
                "page 2 continues where page 1 ended");
            secondPage.HasMore.Should().BeFalse("nothing matches beyond the second page");
            secondPage.TotalCount.Should().Be(30);

            PagedResult<JobKeyDto> countOnly = await client.GetJobs(scheduler.SchedulerName, new DashboardJobQuery { Take = 0 });
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

            PagedResult<JobKeyDto> filtered = await client.GetJobs(scheduler.SchedulerName, new DashboardJobQuery { GroupContains = "mpor", Take = 25 });

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

            PagedResult<TriggerHeaderDto> firstPage = await client.GetTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Take = 25 });
            firstPage.Items.Should().HaveCount(25);
            firstPage.TotalCount.Should().Be(30);
            firstPage.HasMore.Should().BeTrue("30 triggers do not fit on one page of 25");
            firstPage.Items[0].State.Should().Be(TriggerState.Paused, "the header carries the state the listing used to fetch per trigger");
            firstPage.Items[0].ExecutionGroup.Should().Be("imports", "the header carries the execution group without loading the trigger");

            PagedResult<TriggerHeaderDto> secondPage = await client.GetTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Skip = 25, Take = 25 });
            secondPage.Items.Select(x => x.Name).Should().Equal(["trigger26", "trigger27", "trigger28", "trigger29", "trigger30"],
                "page 2 continues where page 1 ended");
            secondPage.HasMore.Should().BeFalse();
            secondPage.Items[^1].State.Should().Be(TriggerState.Normal, "the last four triggers were never paused");

            PagedResult<TriggerHeaderDto> pausedCount = await client.GetTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Take = 0, State = TriggerState.Paused });
            pausedCount.TotalCount.Should().Be(26,
                "a state-filtered count is exact, where the dashboard tile used to count states over the first 25 items only");

            PagedResult<TriggerHeaderDto> errorCount = await client.GetTriggers(scheduler.SchedulerName, new DashboardTriggerQuery { Take = 0, State = TriggerState.Error });
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
            await client.TriggerJobWithData(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name), overrides);

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
            await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("paused"));

            InProcessQuartzApiClient client = CreateClient(scheduler);
            List<JobGroupDto> groups = await client.GetJobGroups(scheduler.SchedulerName);

            groups.Select(x => x.Name).Should().Contain(["paused", "running"], "every job group is listed");
            groups.Single(x => x.Name == "paused").Paused.Should().BeTrue("the group was paused");
            groups.Single(x => x.Name == "running").Paused.Should().BeFalse("the other group was not");
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

            ExecutionLimitsDto? limits = await client.GetExecutionLimits(scheduler.SchedulerName);

            limits.Should().NotBeNull();
            limits.Limits.Should().BeEquivalentTo(new Dictionary<string, DashboardExecutionLimit>
            {
                ["batch"] = new(2, ExecutionLimitScope.Node),
                ["tenant-acme"] = new(8, ExecutionLimitScope.Cluster),
                ["(default)"] = new(3, ExecutionLimitScope.Node),
            });
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetExecutionLimitsShouldBeNullWhenNothingIsLimited()
    {
        IScheduler scheduler = await CreateScheduler("GetExecutionLimitsEmptyTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler);

            (await client.GetExecutionLimits(scheduler.SchedulerName)).Should().BeNull(
                "a scheduler with no limits has nothing to show, which is not the same as showing zeros");
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

    private static InProcessQuartzApiClient CreateClient(IScheduler scheduler)
    {
        SchedulerRepository repository = new();
        repository.Bind(scheduler);
        return new InProcessQuartzApiClient(
            repository,
            Options.Create(new QuartzDashboardOptions()),
            new DashboardHistoryStore());
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
