using System.Collections.Specialized;

using AwesomeAssertions.Execution;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Drives the query endpoints against a real scheduler holding real data, so that paging,
/// filtering and ordering are exercised end to end rather than against a fake's canned answers.
/// </summary>
public class QueryEndpointsTest : WebApiTest
{
    private static readonly JobKey alphaJobOne = new("job1", "alpha");
    private static readonly JobKey alphaJobTwo = new("job2", "alpha");
    private static readonly JobKey alphaJobThree = new("job3", "alpha");
    private static readonly JobKey betaJobOne = new("job1", "beta");
    private static readonly JobKey betaJobTwo = new("job2", "beta");

    private static readonly TriggerKey alphaTriggerOne = new("t1", "alpha");
    private static readonly TriggerKey alphaTriggerTwo = new("t2", "alpha");
    private static readonly TriggerKey alphaTriggerThree = new("t3", "alpha");
    private static readonly TriggerKey alphaTriggerFour = new("t4", "alpha");
    private static readonly TriggerKey betaTriggerOne = new("t5", "beta");
    private static readonly TriggerKey betaTriggerTwo = new("t6", "beta");

    private IScheduler scheduler = null!;
    private HttpScheduler client = null!;

    [SetUp]
    public async Task SeedScheduler()
    {
        string schedulerName = "QueryEndpointsTest_" + Guid.NewGuid().ToString("N");
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.threadPool.threadCount"] = "1",
            ["quartz.serializer.type"] = "stj"
        };

        scheduler = await QuartzSchedulerBuilder.Create().UseProperties(properties).BuildScheduler();
        await Seed();

        WebApplicationFactory.Services.GetRequiredService<ISchedulerRepository>().Bind(scheduler);
        client = new HttpScheduler(schedulerName, WebApplicationFactory.CreateClient());
    }

    [TearDown]
    public async Task ShutDownScheduler()
    {
        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task QueryJobsShouldPageInGroupThenNameOrder()
    {
        PagedResult<JobHeader> all = await client.QueryJobs(new JobQuery());
        using (new AssertionScope())
        {
            all.Items.Select(x => x.Key).Should().Equal(alphaJobOne, alphaJobTwo, alphaJobThree, betaJobOne, betaJobTwo);
            all.HasMore.Should().BeFalse();
            all.TotalCount.Should().BeNull("total count costs an extra query and is opt-in");
        }

        PagedResult<JobHeader> firstPage = await client.QueryJobs(new JobQuery { Take = 2 });
        using (new AssertionScope())
        {
            firstPage.Items.Select(x => x.Key).Should().Equal(alphaJobOne, alphaJobTwo);
            firstPage.HasMore.Should().BeTrue();
        }

        PagedResult<JobHeader> middlePage = await client.QueryJobs(new JobQuery { Skip = 2, Take = 2, IncludeTotalCount = true });
        using (new AssertionScope())
        {
            middlePage.Items.Select(x => x.Key).Should().Equal(alphaJobThree, betaJobOne);
            middlePage.HasMore.Should().BeTrue();
            middlePage.TotalCount.Should().Be(5);
        }

        PagedResult<JobHeader> lastPage = await client.QueryJobs(new JobQuery { Skip = 4, Take = 2, IncludeTotalCount = true });
        using (new AssertionScope())
        {
            lastPage.Items.Select(x => x.Key).Should().Equal(betaJobTwo);
            lastPage.HasMore.Should().BeFalse();
            lastPage.TotalCount.Should().Be(5);
        }

        PagedResult<JobHeader> countOnly = await client.QueryJobs(new JobQuery { Take = 0, IncludeTotalCount = true });
        using (new AssertionScope())
        {
            countOnly.Items.Should().BeEmpty();
            countOnly.TotalCount.Should().Be(5);
        }
    }

    [Test]
    public async Task QueryJobsShouldReturnHeadersOverTheWire()
    {
        PagedResult<JobHeader> result = await client.QueryJobs(new JobQuery { Group = GroupMatcher<JobKey>.GroupEquals("alpha") });

        JobHeader header = result.Items.Single(x => x.Key.Equals(alphaJobOne));
        using (new AssertionScope())
        {
            header.Description.Should().Be("alpha job1");
            header.JobTypeName.Should().Be(new JobType(typeof(DummyJob)).FullName, "the listing reports the type name the store recorded");
            header.Durable.Should().BeTrue();
            header.RequestsRecovery.Should().BeTrue();
            header.ConcurrentExecutionDisallowed.Should().BeFalse();
            header.PersistJobDataAfterExecution.Should().BeFalse();
        }
    }

    [Test]
    public async Task QueryJobsShouldSupportEveryGroupMatcher()
    {
        await AssertJobGroups(GroupMatcher<JobKey>.AnyGroup(), alphaJobOne, alphaJobTwo, alphaJobThree, betaJobOne, betaJobTwo);
        await AssertJobGroups(GroupMatcher<JobKey>.GroupEquals("beta"), betaJobOne, betaJobTwo);
        await AssertJobGroups(GroupMatcher<JobKey>.GroupStartsWith("al"), alphaJobOne, alphaJobTwo, alphaJobThree);
        await AssertJobGroups(GroupMatcher<JobKey>.GroupEndsWith("ta"), betaJobOne, betaJobTwo);
        await AssertJobGroups(GroupMatcher<JobKey>.GroupContains("lph"), alphaJobOne, alphaJobTwo, alphaJobThree);

        async Task AssertJobGroups(GroupMatcher<JobKey> matcher, params JobKey[] expected)
        {
            PagedResult<JobHeader> result = await client.QueryJobs(new JobQuery { Group = matcher });
            result.Items.Select(x => x.Key).Should().Equal(expected);
        }
    }

    [Test]
    public async Task QueryTriggersShouldPageAndReturnHeaders()
    {
        PagedResult<TriggerHeader> all = await client.QueryTriggers(new TriggerQuery { IncludeTotalCount = true });
        using (new AssertionScope())
        {
            all.Items.Select(x => x.Key).Should().Equal(alphaTriggerOne, alphaTriggerTwo, alphaTriggerThree, alphaTriggerFour, betaTriggerOne, betaTriggerTwo);
            all.HasMore.Should().BeFalse();
            all.TotalCount.Should().Be(6);
        }

        PagedResult<TriggerHeader> page = await client.QueryTriggers(new TriggerQuery { Skip = 4, Take = 1, IncludeTotalCount = true });
        using (new AssertionScope())
        {
            page.Items.Select(x => x.Key).Should().Equal(betaTriggerOne);
            page.HasMore.Should().BeTrue();
            page.TotalCount.Should().Be(6);
        }

        TriggerHeader header = all.Items.Single(x => x.Key.Equals(alphaTriggerTwo));
        using (new AssertionScope())
        {
            header.JobKey.Should().Be(alphaJobOne);
            header.Description.Should().Be("trigger t2");
            header.TriggerType.Should().Be("SIMPLE");
            header.State.Should().Be(TriggerState.Normal);
            header.CalendarName.Should().Be("cal-a");
            header.Priority.Should().Be(7);
            header.ExecutionGroup.Should().Be("imports");
            header.NextFireTimeUtc.Should().NotBeNull();
            header.EndTimeUtc.Should().BeNull();
        }
    }

    [Test]
    public async Task QueryTriggersShouldFilterByJob()
    {
        PagedResult<TriggerHeader> result = await client.QueryTriggers(new TriggerQuery { Job = alphaJobOne });
        result.Items.Select(x => x.Key).Should().Equal(alphaTriggerOne, alphaTriggerTwo);

        PagedResult<TriggerHeader> none = await client.QueryTriggers(new TriggerQuery { Job = new JobKey("nope", "alpha") });
        none.Items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryTriggersShouldFilterByCalendar()
    {
        PagedResult<TriggerHeader> result = await client.QueryTriggers(new TriggerQuery { CalendarName = "cal-a" });
        result.Items.Select(x => x.Key).Should().Equal(alphaTriggerTwo);
    }

    [Test]
    public async Task QueryTriggersShouldFilterByState()
    {
        PagedResult<TriggerHeader> paused = await client.QueryTriggers(new TriggerQuery { State = TriggerState.Paused, IncludeTotalCount = true });
        using (new AssertionScope())
        {
            paused.Items.Select(x => x.Key).Should().Equal(betaTriggerOne, betaTriggerTwo);
            paused.Items.Should().AllSatisfy(x => x.State.Should().Be(TriggerState.Paused));
            paused.TotalCount.Should().Be(2);
        }

        PagedResult<TriggerHeader> normal = await client.QueryTriggers(new TriggerQuery { State = TriggerState.Normal });
        normal.Items.Select(x => x.Key).Should().Equal(alphaTriggerOne, alphaTriggerTwo, alphaTriggerThree, alphaTriggerFour);

        PagedResult<TriggerHeader> error = await client.QueryTriggers(new TriggerQuery { State = TriggerState.Error });
        error.Items.Should().BeEmpty();
    }

    [Test]
    public async Task QueryTriggersShouldCombineFilters()
    {
        PagedResult<TriggerHeader> result = await client.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals("alpha"),
            Job = alphaJobOne,
            CalendarName = "cal-a",
            State = TriggerState.Normal
        });

        result.Items.Select(x => x.Key).Should().Equal(alphaTriggerTwo);
    }

    [Test]
    public async Task QueryJobGroupsShouldReportPausedState()
    {
        PagedResult<JobGroup> all = await client.QueryJobGroups(new JobGroupQuery { IncludeTotalCount = true });
        using (new AssertionScope())
        {
            all.Items.Should().Equal(new JobGroup("alpha", Paused: false), new JobGroup("beta", Paused: true));
            all.TotalCount.Should().Be(2);
        }

        PagedResult<JobGroup> paused = await client.QueryJobGroups(new JobGroupQuery { Paused = true });
        paused.Items.Select(x => x.Name).Should().Equal("beta");

        PagedResult<JobGroup> notPaused = await client.QueryJobGroups(new JobGroupQuery { Paused = false });
        notPaused.Items.Select(x => x.Name).Should().Equal("alpha");

        (await client.IsJobGroupPaused("beta")).Should().BeTrue();
        (await client.IsJobGroupPaused("alpha")).Should().BeFalse();
    }

    [Test]
    public async Task QueryTriggerGroupsShouldReportPausedState()
    {
        PagedResult<TriggerGroup> all = await client.QueryTriggerGroups(new TriggerGroupQuery { IncludeTotalCount = true });
        using (new AssertionScope())
        {
            all.Items.Should().Equal(new TriggerGroup("alpha", Paused: false), new TriggerGroup("beta", Paused: true));
            all.TotalCount.Should().Be(2);
        }

        PagedResult<TriggerGroup> paused = await client.QueryTriggerGroups(new TriggerGroupQuery { Paused = true });
        paused.Items.Select(x => x.Name).Should().Equal("beta");

        PagedResult<TriggerGroup> notPaused = await client.QueryTriggerGroups(new TriggerGroupQuery { Paused = false });
        notPaused.Items.Select(x => x.Name).Should().Equal("alpha");

        (await client.IsTriggerGroupPaused("beta")).Should().BeTrue();
        (await client.IsTriggerGroupPaused("alpha")).Should().BeFalse();
        (await client.GetPausedTriggerGroups()).Should().Equal("beta");
    }

    [Test]
    public async Task QueryCalendarNamesShouldPage()
    {
        PagedResult<string> all = await client.QueryCalendarNames(new CalendarQuery());
        all.Items.Should().Equal("cal-a", "cal-b", "cal-c");

        PagedResult<string> page = await client.QueryCalendarNames(new CalendarQuery { Skip = 1, Take = 1, IncludeTotalCount = true });
        using (new AssertionScope())
        {
            page.Items.Should().Equal("cal-b");
            page.HasMore.Should().BeTrue();
            page.TotalCount.Should().Be(3);
        }
    }

    [Test]
    public async Task FetchJobsShouldReturnFoundJobsOnly()
    {
        JobKey missing = new("does_not_exist", "alpha");

        List<IJobDetail> jobDetails = await client.GetJobDetails([alphaJobOne, missing, betaJobTwo]);

        using (new AssertionScope())
        {
            jobDetails.ConvertAll(x => x.Key).Should().BeEquivalentTo(new[] { alphaJobOne, betaJobTwo });

            IJobDetail alpha = jobDetails.Single(x => x.Key.Equals(alphaJobOne));
            alpha.JobType.Type.Should().Be<DummyJob>();
            alpha.Description.Should().Be("alpha job1");
            alpha.Durable.Should().BeTrue();
            alpha.JobDataMap.GetString("owner").Should().Be("alpha");
        }
    }

    [Test]
    public async Task FetchTriggersShouldReturnFoundTriggersOnly()
    {
        TriggerKey missing = new("does_not_exist", "alpha");

        List<ITrigger> triggers = await client.GetTriggers([alphaTriggerTwo, missing, betaTriggerOne]);

        using (new AssertionScope())
        {
            triggers.ConvertAll(x => x.Key).Should().BeEquivalentTo(new[] { alphaTriggerTwo, betaTriggerOne });

            ITrigger trigger = triggers.Single(x => x.Key.Equals(alphaTriggerTwo));
            trigger.JobKey.Should().Be(alphaJobOne);
            trigger.CalendarName.Should().Be("cal-a");
            trigger.Description.Should().Be("trigger t2");
            trigger.Priority.Should().Be(7);
            trigger.ExecutionGroup.Should().Be("imports");
        }
    }

    [Test]
    public async Task LegacyListingMembersShouldRunOnTheQueryWire()
    {
        using (new AssertionScope())
        {
            (await client.GetJobKeys(GroupMatcher<JobKey>.AnyGroup())).Should().Equal(alphaJobOne, alphaJobTwo, alphaJobThree, betaJobOne, betaJobTwo);
            (await client.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("beta"))).Should().Equal(betaJobOne, betaJobTwo);
            (await client.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("beta"))).Should().Equal(betaTriggerOne, betaTriggerTwo);
            (await client.GetJobGroupNames()).Should().Equal("alpha", "beta");
            (await client.GetTriggerGroupNames()).Should().Equal("alpha", "beta");
            (await client.GetCalendarNames()).Should().Equal("cal-a", "cal-b", "cal-c");
        }
    }

    private async Task Seed()
    {
        await scheduler.AddCalendar("cal-a", new HolidayCalendar(), new AddCalendarOptions { Replace = true });
        await scheduler.AddCalendar("cal-b", new HolidayCalendar(), new AddCalendarOptions { Replace = true });
        await scheduler.AddCalendar("cal-c", new HolidayCalendar(), new AddCalendarOptions { Replace = true });

        await scheduler.ScheduleJob(Job(alphaJobOne), [Trigger(alphaTriggerOne, alphaJobOne), Trigger(alphaTriggerTwo, alphaJobOne, calendarName: "cal-a")], new ScheduleJobOptions { Replace = true });
        await scheduler.ScheduleJob(Job(alphaJobTwo), [Trigger(alphaTriggerThree, alphaJobTwo)], new ScheduleJobOptions { Replace = true });
        await scheduler.ScheduleJob(Job(alphaJobThree), [Trigger(alphaTriggerFour, alphaJobThree)], new ScheduleJobOptions { Replace = true });
        await scheduler.ScheduleJob(Job(betaJobOne), [Trigger(betaTriggerOne, betaJobOne)], new ScheduleJobOptions { Replace = true });
        await scheduler.ScheduleJob(Job(betaJobTwo), [Trigger(betaTriggerTwo, betaJobTwo)], new ScheduleJobOptions { Replace = true });

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("beta"));
        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("beta"));
    }

    private static IJobDetail Job(JobKey jobKey) => JobBuilder.Create<DummyJob>()
        .WithIdentity(jobKey)
        .WithDescription($"{jobKey.Group} {jobKey.Name}")
        .UsingJobData("owner", jobKey.Group)
        .StoreDurably()
        .RequestRecovery()
        .Build();

    private static ITrigger Trigger(TriggerKey triggerKey, JobKey jobKey, string? calendarName = null) => TriggerBuilder.Create()
        .WithIdentity(triggerKey)
        .ForJob(jobKey)
        .WithDescription("trigger " + triggerKey.Name)
        .WithCalendarName(calendarName)
        .StartAt(DateTimeOffset.UtcNow.AddDays(1))
        .WithPriority(7)
        .WithExecutionGroup("imports")
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
        .Build();
}
