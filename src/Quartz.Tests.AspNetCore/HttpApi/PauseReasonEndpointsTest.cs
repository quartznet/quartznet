using System.Net;
using System.Text;

using FakeItEasy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What the pause routes do with their optional body, and where a pause's record is read back from:
/// through <see cref="HttpScheduler" />, and as a client that is not one.
/// </summary>
public sealed class PauseReasonEndpointsTest : WebApiTest
{
    private static readonly TriggerKey triggerKey = new("nightly", "reports");
    private static readonly JobKey jobKey = new("export", "reports");
    private static readonly PauseDetails details = new() { Reason = "quarter close", RequestedBy = "finance" };
    private static readonly PauseInfo record = new("quarter close", "finance", new DateTimeOffset(2031, 3, 31, 18, 0, 0, TimeSpan.Zero));

    private string SchedulerUrl => "schedulers/" + HttpScheduler.SchedulerName;

    /// <summary>
    /// The base fake, whose <c>*With</c> members answer through the reasonless ones as a scheduler
    /// written before 4.3 does, so the tests below can arrange a pause once and see which member the
    /// route chose.
    /// </summary>
    protected override IScheduler CreateFakeScheduler()
    {
        IScheduler fake = base.CreateFakeScheduler();
        FakeSchedulers.AnswerThePausesWithDetailsFromTheReasonlessOnes(fake);
        return fake;
    }

    [Test]
    public async Task EveryPauseTheClientMakesWithDetailsReachesTheSchedulerWithThem()
    {
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.PauseJob(jobKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.PauseTriggerGroups(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => FakeScheduler.PauseJobGroups(A<GroupMatcher<JobKey>>._, A<CancellationToken>._)).Returns(new List<string> { "reports" });

        (await HttpScheduler.PauseTriggerWith(triggerKey, details)).Should().BeTrue("the applied flag round-trips as it does without details");
        (await HttpScheduler.PauseJobWith(jobKey, details)).Should().BeTrue();
        (await HttpScheduler.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("reports"), details)).Should().Equal(["reports"]);
        (await HttpScheduler.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals("reports"), details)).Should().Equal(["reports"]);
        await HttpScheduler.PauseAllWith(details);

        A.CallTo(() => FakeScheduler.PauseTriggerWith(triggerKey, A<PauseDetails>.That.Matches(sent => Carried(sent)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseJobWith(jobKey, A<PauseDetails>.That.Matches(sent => Carried(sent)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseTriggerGroupsWith(
                A<GroupMatcher<TriggerKey>>.That.Matches(m => m.CompareToValue == "reports"), A<PauseDetails>.That.Matches(sent => Carried(sent)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseJobGroupsWith(
                A<GroupMatcher<JobKey>>.That.Matches(m => m.CompareToValue == "reports"), A<PauseDetails>.That.Matches(sent => Carried(sent)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseAllWith(A<PauseDetails>.That.Matches(sent => Carried(sent)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task APauseWithoutABodyIsTheReasonlessPause()
    {
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKey, A<CancellationToken>._)).Returns(true);

        using HttpClient client = WebApplicationFactory.CreateClient();
        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/triggers/reports/nightly/pause", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a pause posted with nothing is every pause before 4.3, and the body is optional so that it still works");

        // Through the member every pause before 4.3 went through, so a scheduler that decorates it still
        // sees the pause.
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseTriggerWith(A<TriggerKey>._, A<PauseDetails>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ABodyThatSaysNothingIsTheReasonlessPause()
    {
        using HttpClient client = WebApplicationFactory.CreateClient();
        using StringContent body = new("""{ "reason": "  " }""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // No reason, and nobody authenticated to name: nothing to record, so the reasonless member.
        A.CallTo(() => FakeScheduler.PauseAll(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseAllWith(A<PauseDetails>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task TheClientSendsNoBodyForDetailsThatSayNothing()
    {
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.PauseJob(jobKey, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.PauseTriggerGroups(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._)).Returns(new List<string> { "reports" });
        A.CallTo(() => FakeScheduler.PauseJobGroups(A<GroupMatcher<JobKey>>._, A<CancellationToken>._)).Returns(new List<string> { "reports" });

        (await HttpScheduler.PauseTriggerWith(triggerKey, null)).Should().BeTrue();
        (await HttpScheduler.PauseJobWith(jobKey, new PauseDetails())).Should().BeTrue();
        (await HttpScheduler.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("reports"), null)).Should().Equal(["reports"]);
        (await HttpScheduler.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals("reports"), new PauseDetails { Reason = "" })).Should().Equal(["reports"]);
        await HttpScheduler.PauseAllWith(null);

        // Each arrives as the request a client before 4.3 sent, and is answered by the same member.
        A.CallTo(() => FakeScheduler.PauseTrigger(triggerKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseJob(jobKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseTriggerGroups(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseJobGroups(A<GroupMatcher<JobKey>>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseAll(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => FakeScheduler.PauseTriggerWith(A<TriggerKey>._, A<PauseDetails>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => FakeScheduler.PauseJobWith(A<JobKey>._, A<PauseDetails>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => FakeScheduler.PauseAllWith(A<PauseDetails>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ABodyMayNameOnlyAReason()
    {
        using HttpClient client = WebApplicationFactory.CreateClient();
        using StringContent body = new("""{ "reason": "vendor outage" }""", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        A.CallTo(() => FakeScheduler.PauseAllWith(
                A<PauseDetails>.That.Matches(d => d.Reason == "vendor outage" && d.RequestedBy == null),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task APausedTriggersStateCarriesItsPauseAndAnotherTriggersDoesNot()
    {
        TriggerKey running = new("hourly", "reports");
        A.CallTo(() => FakeScheduler.GetTriggerState(triggerKey, A<CancellationToken>._)).Returns(TriggerState.Paused);
        A.CallTo(() => FakeScheduler.GetTriggerPause(triggerKey, A<CancellationToken>._)).Returns(record);
        A.CallTo(() => FakeScheduler.GetTriggerState(running, A<CancellationToken>._)).Returns(TriggerState.Normal);

        (await HttpScheduler.GetTriggerPause(triggerKey)).Should().Be(record,
            "the state route answers the pause beside the state, so the client reads both in one round trip");
        (await HttpScheduler.GetTriggerPause(running)).Should().BeNull();

        // Only a paused trigger has a pause to report, so no other is asked.
        A.CallTo(() => FakeScheduler.GetTriggerPause(running, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task APausedGroupCarriesItsPause()
    {
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(A<TriggerGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroup>([new TriggerGroup("reports", Paused: true)], HasMore: false));
        A.CallTo(() => FakeScheduler.QueryJobGroups(A<JobGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobGroup>([new JobGroup("reports", Paused: true)], HasMore: false));
        A.CallTo(() => FakeScheduler.GetTriggerGroupPause("reports", A<CancellationToken>._)).Returns(record);
        A.CallTo(() => FakeScheduler.GetJobGroupPause("reports", A<CancellationToken>._)).Returns(record);

        (await HttpScheduler.GetTriggerGroupPause("reports")).Should().Be(record);
        (await HttpScheduler.GetJobGroupPause("reports")).Should().Be(record);
    }

    [Test]
    public async Task AGroupThatIsNotPausedIsNotAskedForAPause()
    {
        A.CallTo(() => FakeScheduler.QueryTriggerGroups(A<TriggerGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroup>([], HasMore: false));

        (await HttpScheduler.GetTriggerGroupPause("reports")).Should().BeNull();

        A.CallTo(() => FakeScheduler.GetTriggerGroupPause(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ThePausedTriggersOwnRecordTravelsWithTheListing()
    {
        TriggerHeader header = new(
            triggerKey, jobKey, Description: null, TriggerType: "SIMPLE", State: TriggerState.Paused,
            StartTimeUtc: record.PausedAtUtc, EndTimeUtc: null, NextFireTimeUtc: null, PreviousFireTimeUtc: null,
            CalendarName: null, Priority: 5, ExecutionGroup: null, RetryPolicy: null, RetryAttempt: 0)
        {
            Pause = record
        };

        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([header], HasMore: false));

        PagedResult<TriggerHeader> page = await HttpScheduler.QueryTriggers(new TriggerQuery());

        page.Items.Should().ContainSingle().Which.Pause.Should().Be(record,
            "a listing that dropped the record would make a dashboard over HTTP read one trigger at a time");
    }

    private static bool Carried(PauseDetails sent) => sent.Reason == "quarter close" && sent.RequestedBy == "finance";
}

/// <summary>
/// Who a pause posted by an authenticated caller names as its requester.
/// </summary>
public sealed class PauseRequesterEndpointTest
{
    private const string SchedulerUrl = "schedulers/" + TestData.SchedulerName;

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private HttpClient client = null!;
    private IScheduler fake = null!;

    [SetUp]
    public void StartApi()
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddQuartzHttpApi();
            services.AddTenantAuthorization();
        }));
        factories.Add(configured);

        fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        FakeSchedulers.AnswerThePausesWithDetailsFromTheReasonlessOnes(fake);

        client = configured.CreateClient();

        ISchedulerRepository repository = configured.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
    }

    [TearDown]
    public async Task TearDown()
    {
        client.Dispose();
        await fake.DisposeAsync();

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }
    }

    [Test]
    public async Task ABodyThatNamesNoRequesterIsTheAuthenticatedCallers()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{SchedulerUrl}/jobs/reports/export/pause")
        {
            Content = new StringContent("""{ "reason": "quarter close" }""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(TenantAuthenticationHandler.TenantHeaderName, "ops@example.com");

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        A.CallTo(() => fake.PauseJobWith(
                new JobKey("export", "reports"),
                A<PauseDetails>.That.Matches(d => d.Reason == "quarter close" && d.RequestedBy == "ops@example.com"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly(); // the name the audit line records the request under
    }

    [Test]
    public async Task ARequesterTheBodyNamesWins()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{SchedulerUrl}/triggers/pause?groupEquals=reports")
        {
            Content = new StringContent("""{ "requestedBy": "alice" }""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(TenantAuthenticationHandler.TenantHeaderName, "dashboard-service");

        A.CallTo(() => fake.PauseTriggerGroups(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._)).Returns(new List<string>());

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        A.CallTo(() => fake.PauseTriggerGroupsWith(
                A<GroupMatcher<TriggerKey>>._,
                A<PauseDetails>.That.Matches(d => d.Reason == null && d.RequestedBy == "alice"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly(); // a dashboard calling as a service names the operator it acts for
    }

    [Test]
    public async Task APauseWithNoBodyIsTheReasonlessOneEvenForAnAuthenticatedCaller()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{SchedulerUrl}/jobs/reports/export/pause");
        request.Headers.Add(TenantAuthenticationHandler.TenantHeaderName, "ops@example.com");

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // No body is the request a 4.2 client sends, so it is answered as 4.2 answered it; the caller
        // is still in the audit line.
        A.CallTo(() => fake.PauseJob(new JobKey("export", "reports"), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fake.PauseJobWith(A<JobKey>._, A<PauseDetails>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
