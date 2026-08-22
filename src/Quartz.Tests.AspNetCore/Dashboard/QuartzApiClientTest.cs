using FakeItEasy;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.HttpApi;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The dashboard's HTTP-backed client, driven against the real HTTP API.
/// </summary>
/// <remarks>
/// This is the half of <see cref="IQuartzApiClient" /> where the contract's types actually have to
/// survive a serializer: the in-process client hands the objects straight over, while this one has to
/// write a trigger, a calendar and a job data map in the discriminated shape the API reads, and read
/// them back out of it.
/// </remarks>
public class QuartzApiClientTest : WebApiTest
{
    [Test]
    public async Task GetTriggerReadsTheTriggerBackOffTheWire()
    {
        ITrigger expected = TestData.CronTrigger;
        A.CallTo(() => FakeScheduler.GetTrigger(expected.Key, A<CancellationToken>._)).Returns(expected);

        ITrigger trigger = await CreateClient().GetTrigger(
            TestData.SchedulerName,
            new TriggerKeyDto(expected.Key.Group, expected.Key.Name));

        ICronTrigger cron = trigger.Should().BeAssignableTo<ICronTrigger>(
            "a trigger's kind is on the wire, and the client is the thing that has to read it").Subject;
        cron.CronExpressionString.Should().Be("0/25 * * * * ?");
        cron.Key.Should().Be(expected.Key);
        cron.JobKey.Should().Be(expected.JobKey);
    }

    [Test]
    public async Task GetCalendarReadsTheCalendarBackOffTheWire()
    {
        A.CallTo(() => FakeScheduler.GetCalendar("holidays", A<CancellationToken>._)).Returns(TestData.CronCalendar);

        ICalendar calendar = await CreateClient().GetCalendar(TestData.SchedulerName, "holidays");

        CronCalendar cron = calendar.Should().BeOfType<CronCalendar>().Subject;
        cron.CronExpression.CronExpressionString.Should().Be("0 0 * * * ?");
        cron.Description.Should().Be("Test CronCalendar");
    }

    [Test]
    public async Task GetJobReadsTheJobDataMapBackOffTheWire()
    {
        A.CallTo(() => FakeScheduler.GetJobDetail(TestData.JobDetail.Key, A<CancellationToken>._)).Returns(TestData.JobDetail);

        JobDetailDto job = await CreateClient().GetJob(
            TestData.SchedulerName,
            new JobKeyDto(TestData.JobDetail.Key.Group, TestData.JobDetail.Key.Name));

        job.JobDataMap["TestKey"].Should().Be("TestValue");
        job.JobType.Should().Be(TestData.JobDetail.JobType.FullName);
    }

    [Test]
    public async Task RescheduleJobSendsATriggerTheServerCanRead()
    {
        ITrigger replacement = TriggerBuilder.Create()
            .WithIdentity("CronTriggerKey", "CronTriggerGroup")
            .ForJob("CronJobKey", "CronJobGroup")
            .WithExecutionGroup("imports")
            .WithCronSchedule("0 0 2 * * ?")
            .Build();

        await CreateClient().RescheduleJob(
            TestData.SchedulerName,
            new TriggerKeyDto("CronTriggerGroup", "CronTriggerKey"),
            new RescheduleRequest(replacement));

        A.CallTo(() => FakeScheduler.RescheduleJob(A<TriggerKey>._, A<ITrigger>._, A<CancellationToken>._))
            .WhenArgumentsMatch((TriggerKey key, ITrigger trigger, CancellationToken _) =>
                key == replacement.Key
                && ((ICronTrigger) trigger).CronExpressionString == "0 0 2 * * ?"
                && trigger.ExecutionGroup == "imports")
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AddCalendarSendsACalendarTheServerCanRead()
    {
        CronCalendar calendar = new(baseCalendar: null, "0 0 3 * * ?", TimeZoneInfo.Utc)
        {
            Description = "maintenance window"
        };

        await CreateClient().AddCalendar(
            TestData.SchedulerName,
            new AddCalendarRequest("maintenance", calendar, Replace: true, UpdateTriggers: false));

        A.CallTo(() => FakeScheduler.AddCalendar(A<string>._, A<ICalendar>._, A<AddCalendarOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((string name, ICalendar received, AddCalendarOptions options, CancellationToken _) =>
                name == "maintenance"
                && ((CronCalendar) received).CronExpression.CronExpressionString == "0 0 3 * * ?"
                && received.Description == "maintenance window"
                && options.Replace)
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TriggerJobWithDataSendsTheJobDataMap()
    {
        JobDataMap overrides = new() { ["colour"] = "green" };

        await CreateClient().TriggerJobWithData(
            TestData.SchedulerName,
            new JobKeyDto("DummyGroup", "DummyJob"),
            overrides);

        A.CallTo(() => FakeScheduler.TriggerJob(A<JobKey>._, A<JobDataMap>._, A<CancellationToken>._))
            .WhenArgumentsMatch((JobKey key, JobDataMap? data, CancellationToken _) =>
                key == new JobKey("DummyJob", "DummyGroup") && data is not null && Equals(data["colour"], "green"))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task GetJobReadsAnAbsentJobDataMapAsAnEmptyOne()
    {
        IJobDetail withoutData = JobBuilder.Create<DummyJob>()
            .WithIdentity("no-data", "DummyGroup")
            .StoreDurably()
            .Build();
        A.CallTo(() => FakeScheduler.GetJobDetail(withoutData.Key, A<CancellationToken>._)).Returns(withoutData);

        JobDetailDto job = await CreateClient().GetJob(TestData.SchedulerName, new JobKeyDto("DummyGroup", "no-data"));

        job.JobDataMap.Should().BeEmpty("a job with no data is not a job whose data could not be read");
    }

    /// <summary>
    /// The associated-triggers table loads the triggers, so it can name the kind and summarise the
    /// schedule — and it names them the way the in-process client does.
    /// </summary>
    /// <remarks>
    /// This used to echo the wire's <c>triggerType</c> discriminator, so the same trigger read
    /// <c>CronTrigger</c> here and <c>Cron</c> in process. Both go through
    /// <see cref="TriggerDisplay" /> now, and this is the test that says so.
    /// </remarks>
    [Test]
    public async Task GetJobTriggersNamesTheKindTheWayTheInProcessClientDoes()
    {
        JobKey jobKey = new("DummyJob", "DummyGroup");
        ITrigger cron = TriggerBuilder.Create()
            .WithIdentity("nightly", "reports")
            .ForJob(jobKey)
            .WithExecutionGroup("imports")
            .WithCronSchedule("0 0 1 * * ?")
            .Build();
        ITrigger simple = TriggerBuilder.Create()
            .WithIdentity("often", "reports")
            .ForJob(jobKey)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).WithRepeatCount(2))
            .Build();

        // GetTriggersOfJob is an extension over QueryTriggers + GetTriggers, so those are what a fake
        // scheduler can be told about
        A.CallTo(() => FakeScheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<ITrigger> { cron, simple });
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>(
                [HeaderFor(cron, TriggerState.Paused), HeaderFor(simple, TriggerState.Normal)],
                HasMore: false,
                TotalCount: 2));

        List<TriggerHeaderDto> headers = await CreateClient().GetJobTriggers(
            TestData.SchedulerName,
            new JobKeyDto(jobKey.Group, jobKey.Name));

        headers.Should().HaveCount(2);

        TriggerHeaderDto cronHeader = headers.Single(x => x.Name == "nightly");
        cronHeader.TriggerType.Should().Be("Cron", "not CronTrigger, which is the wire's discriminator");
        cronHeader.ScheduleSummary.Should().Be("0 0 1 * * ?");
        cronHeader.Group.Should().Be("reports");
        cronHeader.ExecutionGroup.Should().Be("imports");
        cronHeader.State.Should().Be(TriggerState.Paused, "the states come from one listing, not one call per trigger");

        TriggerHeaderDto simpleHeader = headers.Single(x => x.Name == "often");
        simpleHeader.TriggerType.Should().Be("Simple");
        simpleHeader.ScheduleSummary.Should().Contain("Every").And.Contain("2 time(s)");
        simpleHeader.State.Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// The trigger listing does not load the triggers, so it reports no kind and no schedule summary —
    /// deliberately, because the store's discriminator is not the display name above.
    /// </summary>
    [Test]
    public async Task GetTriggersListsHeadersWithoutNamingTheKind()
    {
        ITrigger cron = TriggerBuilder.Create()
            .WithIdentity("nightly", "reports")
            .ForJob("DummyJob", "DummyGroup")
            .WithExecutionGroup("imports")
            .WithCronSchedule("0 0 1 * * ?")
            .Build();

        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([HeaderFor(cron, TriggerState.Error)], HasMore: true, TotalCount: 7));

        PagedResult<TriggerHeaderDto> page = await CreateClient().GetTriggers(
            TestData.SchedulerName,
            new DashboardTriggerQuery { Take = 1, State = TriggerState.Error });

        TriggerHeaderDto header = page.Items.Should().ContainSingle().Subject;
        header.Group.Should().Be("reports");
        header.Name.Should().Be("nightly");
        header.State.Should().Be(TriggerState.Error);
        header.ExecutionGroup.Should().Be("imports");
        header.TriggerType.Should().BeNull("a listing has no trigger to read a kind off");
        header.ScheduleSummary.Should().BeNull("and no schedule to summarise");

        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(7);
    }

    private static TriggerHeader HeaderFor(ITrigger trigger, TriggerState state) => new(
        trigger.Key,
        trigger.JobKey,
        Description: trigger.Description,
        TriggerType: "CRON",
        State: state,
        StartTimeUtc: trigger.StartTimeUtc,
        EndTimeUtc: trigger.EndTimeUtc,
        NextFireTimeUtc: trigger.NextFireTimeUtc,
        PreviousFireTimeUtc: trigger.PreviousFireTimeUtc,
        CalendarName: trigger.CalendarName,
        Priority: trigger.Priority,
        ExecutionGroup: trigger.ExecutionGroup);

    private QuartzApiClient CreateClient()
    {
        IHttpClientFactory httpClientFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpClientFactory.CreateClient(A<string>._)).ReturnsLazily(() => WebApplicationFactory.CreateClient());

        IHttpContextAccessor httpContextAccessor = A.Fake<IHttpContextAccessor>();
        A.CallTo(() => httpContextAccessor.HttpContext).Returns(null);

        // The test host maps the API at the root, and the client the factory hands out already carries
        // its base address.
        return new QuartzApiClient(
            httpClientFactory,
            httpContextAccessor,
            Options.Create(new QuartzDashboardOptions { ApiPath = "/" }),
            new DashboardSerializerOptions(new SystemTextJsonSerializerRegistry()));
    }
}
