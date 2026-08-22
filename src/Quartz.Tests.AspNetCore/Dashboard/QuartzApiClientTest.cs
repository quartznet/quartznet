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
