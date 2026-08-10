using System.Collections.Specialized;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Serialization.Json;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class InProcessQuartzApiClientTest
{
    private static readonly JsonSerializerOptions requestSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public async Task RescheduleJobShouldAcceptCronTriggerPayload()
    {
        // regression test for #3094 - rescheduling from the dashboard failed because the
        // Quartz JSON converters were never registered and ITrigger could not be deserialized
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

            // payload mirrors TriggerDetail.razor BuildRescheduleTriggerPayload
            object payload = new
            {
                triggerType = "CronTrigger",
                key = new { name = triggerKey.Name, group = triggerKey.Group },
                jobKey = new { name = jobKey.Name, group = jobKey.Group },
                description = "updated by dashboard",
                calendarName = (string?) null,
                jobDataMap = new Dictionary<string, object>(),
                misfireInstruction = 0,
                startTimeUtc = DateTimeOffset.UtcNow,
                endTimeUtc = (DateTimeOffset?) null,
                priority = 5,
                timeZone = TimeZoneInfo.Utc.Id,
                cronExpressionString = "0 0 2 * * ?",
                executionGroup = "imports"
            };
            RescheduleRequest request = new(JsonSerializer.SerializeToElement(payload, requestSerializerOptions));

            await client.RescheduleJob(scheduler.SchedulerName, triggerKey.Group, triggerKey.Name, request);

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
    public async Task AddCalendarShouldAcceptCronCalendarPayload()
    {
        // the calendar deserialization path was broken the same way as reschedule (#3094)
        IScheduler scheduler = await CreateScheduler("AddCalendarTest");
        try
        {
            InProcessQuartzApiClient client = CreateClient(scheduler);

            // payload mirrors Calendars.razor calendarPayload
            object payload = new
            {
                type = "CronCalendar",
                description = "maintenance window",
                timeZoneId = TimeZoneInfo.Utc.Id,
                baseCalendar = (object?) null,
                cronExpressionString = "0 0 3 * * ?"
            };
            AddCalendarRequest request = new(
                "maintenance",
                JsonSerializer.SerializeToElement(payload, requestSerializerOptions),
                Replace: false,
                UpdateTriggers: false);

            await client.AddCalendar(scheduler.SchedulerName, request);

            ICalendar? calendar = await scheduler.GetCalendar("maintenance");
            CronCalendar cronCalendar = calendar.Should().BeOfType<CronCalendar>().Subject;
            cronCalendar.CronExpression.CronExpressionString.Should().Be("0 0 3 * * ?");
            cronCalendar.Description.Should().Be("maintenance window");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetJobExposesJobDataMapThatConvertsBackToJobDataMap()
    {
        // regression test for #3130 - JobDetail.razor cast the JsonElement directly to JobDataMap,
        // which always produced null. DisplayValueHelper.GetJobDataMap now performs the conversion.
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
            JobDetailDto dto = await client.GetJob(scheduler.SchedulerName, jobKey.Group, jobKey.Name);

            dto.JobDataMap.GetProperty("Name").GetString().Should().Be("abc");

            JobDataMap? map = DisplayValueHelper.GetJobDataMap(dto, "JobDataMap");
            map.Should().NotBeNull();
            map!["Name"].Should().Be("abc");
            map["Count"].Should().Be(5);
            map["Enabled"].Should().Be(true);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task GetTriggerForSimpleTriggerIncludesTypeScheduleAndJobDataMap()
    {
        // regression test for #3130 - simple triggers were serialized via plain reflection, so the
        // detail page was missing TriggerType / schedule / JobDataMap. GetTrigger now uses the
        // canonical Quartz converters.
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
            TriggerDetailDto detail = await client.GetTrigger(scheduler.SchedulerName, triggerKey.Group, triggerKey.Name);

            JsonElement value = detail.Value;
            value.GetProperty("triggerType").GetString().Should().Be("SimpleTrigger");
            value.GetProperty("jobDataMap").GetProperty("Color").GetString().Should().Be("red");
            value.TryGetProperty("repeatIntervalTimeSpan", out _).Should().BeTrue();

            DisplayValueHelper.GetJobDataMap(value, "JobDataMap", "jobDataMap")!["Color"].Should().Be("red");
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
            List<TriggerHeaderDto> headers = await client.GetJobTriggers(scheduler.SchedulerName, jobKey.Group, jobKey.Name);

            headers.Should().HaveCount(2);
            TriggerHeaderDto simple = headers.Single(h => h.Name == "simple");
            simple.TriggerType.Should().Be("Simple");
            simple.ScheduleSummary.Should().Contain("Every").And.Contain("time(s)");
            TriggerHeaderDto cron = headers.Single(h => h.Name == "cron");
            cron.TriggerType.Should().Be("Cron");
            cron.ScheduleSummary.Should().Be("0 0 1 * * ?");

            headers.Should().AllSatisfy(
                header => header.State.Should().Be("Normal"),
                "the states come from the single trigger query the associated triggers table used to make one call per trigger for");

            await scheduler.PauseTrigger(new TriggerKey("cron", "group1"));
            headers = await client.GetJobTriggers(scheduler.SchedulerName, jobKey.Group, jobKey.Name);

            headers.Single(h => h.Name == "cron").State.Should().Be("Paused", "each header carries its own trigger's state");
            headers.Single(h => h.Name == "simple").State.Should().Be("Normal");
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

            JobPageDto firstPage = await client.GetJobs(scheduler.SchedulerName, groupFilter: null, page: 1, pageSize: 25);
            firstPage.Items.Should().HaveCount(25, "the page size limits what the store returns");
            firstPage.TotalCount.Should().Be(30, "the total is counted regardless of paging");
            firstPage.HasMore.Should().BeTrue("30 jobs do not fit on one page of 25");
            firstPage.Items[0].Name.Should().Be("job01", "results are ordered by group and then name");

            JobPageDto secondPage = await client.GetJobs(scheduler.SchedulerName, groupFilter: null, page: 2, pageSize: 25);
            secondPage.Items.Should().HaveCount(5, "the second page holds the remainder");
            secondPage.Items.Select(x => x.Name).Should().Equal(["job26", "job27", "job28", "job29", "job30"],
                "page 2 continues where page 1 ended");
            secondPage.HasMore.Should().BeFalse("nothing matches beyond the second page");
            secondPage.TotalCount.Should().Be(30);

            JobPageDto countOnly = await client.GetJobs(scheduler.SchedulerName, groupFilter: null, page: 1, pageSize: 0);
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

            JobPageDto filtered = await client.GetJobs(scheduler.SchedulerName, groupFilter: "mpor", page: 1, pageSize: 25);

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

            TriggerPageDto firstPage = await client.GetTriggers(scheduler.SchedulerName, groupFilter: null, state: null, page: 1, pageSize: 25);
            firstPage.Items.Should().HaveCount(25);
            firstPage.TotalCount.Should().Be(30);
            firstPage.HasMore.Should().BeTrue("30 triggers do not fit on one page of 25");
            firstPage.Items[0].State.Should().Be("Paused", "the header carries the state the listing used to fetch per trigger");
            firstPage.Items[0].ExecutionGroup.Should().Be("imports", "the header carries the execution group without loading the trigger");

            TriggerPageDto secondPage = await client.GetTriggers(scheduler.SchedulerName, groupFilter: null, state: null, page: 2, pageSize: 25);
            secondPage.Items.Select(x => x.Name).Should().Equal(["trigger26", "trigger27", "trigger28", "trigger29", "trigger30"],
                "page 2 continues where page 1 ended");
            secondPage.HasMore.Should().BeFalse();
            secondPage.Items.Last().State.Should().Be("Normal", "the last four triggers were never paused");

            TriggerPageDto pausedCount = await client.GetTriggers(scheduler.SchedulerName, groupFilter: null, state: TriggerState.Paused, page: 1, pageSize: 0);
            pausedCount.TotalCount.Should().Be(26,
                "a state-filtered count is exact, where the dashboard tile used to count states over the first 25 items only");

            TriggerPageDto errorCount = await client.GetTriggers(scheduler.SchedulerName, groupFilter: null, state: TriggerState.Error, page: 1, pageSize: 0);
            errorCount.Items.Should().BeEmpty();
            errorCount.TotalCount.Should().Be(0, "no trigger has failed, and the error tile reports that exactly");
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
            new DashboardHistoryStore(),
            new DashboardSerializerOptions(new SystemTextJsonSerializerRegistry()).Deserializer);
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
