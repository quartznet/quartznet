using System.Collections.Specialized;
using System.Text.Json;


using Microsoft.Extensions.Options;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

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
            await scheduler.AddJob(job, replace: true);

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
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(30).WithRepeatCount(3))
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
                    .WithSimpleSchedule(x => x.WithIntervalInSeconds(30).WithRepeatCount(2)).Build());
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
        return await new StdSchedulerFactory(properties).GetScheduler();
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
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }
}
