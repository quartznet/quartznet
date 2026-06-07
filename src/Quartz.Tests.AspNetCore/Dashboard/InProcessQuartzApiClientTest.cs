using System.Collections.Specialized;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.Options;

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
        public ValueTask Execute(IJobExecutionContext context)
        {
            return ValueTask.CompletedTask;
        }
    }
}
