using System.Net;
using System.Text;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What the API records about a request that changed something: one <c>9007</c> line naming the caller,
/// the operation, the scheduler and the route.
/// </summary>
/// <remarks>
/// The events the API raised until 4.1 were failures only, so a <c>POST …/shutdown</c> that worked was
/// recorded nowhere at all and "who paused this trigger" could not be answered for a caller that came
/// over HTTP. The line is written in the same wrapper that refuses a mutation while the API is read-only,
/// off the same marker, which is what ties the two together: a route that is audited is a route that can
/// be refused, and neither is a list somebody has to remember to add to.
/// </remarks>
public sealed class MutationAuditTest
{
    private const int AuditEventId = 9007;
    private const int RefusedEventId = 9005;
    private const string SchedulerUrl = "schedulers/" + TestData.SchedulerName;

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly RecordingLoggerProvider logs = new();

    private HttpClient client = null!;
    private IScheduler fake = null!;

    [TearDown]
    public async Task TearDown()
    {
        client?.Dispose();

        if (fake is not null)
        {
            await fake.DisposeAsync();
        }

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
        logs.Clear();
    }

    /// <summary>
    /// One route from every family that changes something, driven until it answers <c>2xx</c>: each one
    /// writes exactly one audit line, and the line says which operation it was.
    /// </summary>
    /// <remarks>
    /// Every shape a mutating route takes is here — the target in the path, the target in the query, the
    /// targets in a body, and a <c>DELETE</c> — because what decides whether a request is audited is the
    /// marker on the route rather than anything about the request.
    /// </remarks>
    [Test]
    public async Task EveryMutatingRouteFamilyWritesOneAuditLineWhenItSucceeds()
    {
        StartApi();

        using (new AssertionScope())
        {
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/start", "Start");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/standby", "Standby");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/shutdown", "Shutdown");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/clear", "Clear");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/pause-all", "PauseAll");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/resume-all", "ResumeAll");
            await Audited(HttpMethod.Delete, $"{SchedulerUrl}/execution-limits", "ClearExecutionLimits");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/pause", "PauseJob");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/resume", "ResumeJob");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/trigger", "TriggerJob");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/interrupt", "InterruptJob");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/jobs/interrupt/fire-instance-1", "InterruptJobInstance");
            await Audited(HttpMethod.Delete, $"{SchedulerUrl}/jobs/group/existing", "DeleteJob");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/jobs/pause?groupEquals=group", "PauseJobs");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/jobs/keys/pause", "PauseJobKeys",
                requestJson: """{"jobs":[{"name":"existing","group":"group"}]}""");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/triggers/group/existing/pause", "PauseTrigger");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/triggers/group/existing/reset-from-error-state", "ResetTriggerFromErrorState");
            await Audited(HttpMethod.Post, $"{SchedulerUrl}/triggers/group/existing/unschedule", "UnscheduleJob");
            await Audited(HttpMethod.Delete, $"{SchedulerUrl}/calendars/holidays", "DeleteCalendar");
        }
    }

    /// <summary>
    /// The two routes an operator most wants to read afterwards take a body, and are audited by the same
    /// line: scheduling something, and adding a calendar every trigger is then held to.
    /// </summary>
    /// <remarks>
    /// Driven through <see cref="HttpScheduler" /> rather than with hand-written JSON, because the bodies
    /// are the whole wire format and the client is what writes it.
    /// </remarks>
    [Test]
    public async Task TheRoutesThatTakeABodyAreAuditedToo()
    {
        StartApi();
        A.CallTo(() => fake.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.FiredAt);

        using HttpClient typedClient = factories[^1].CreateClient();
        await using HttpScheduler scheduler = new(TestData.SchedulerName, typedClient);

        await scheduler.ScheduleJob(TestData.CronTrigger);
        await scheduler.AddCalendar("holidays", new HolidayCalendar(), AddCalendarOptions.Replacing);

        logs.WithEventId(AuditEventId).Select(x => x.Properties["Operation"]).Should()
            .Equal(["ScheduleJob", "AddCalendar"], "a mutation that arrives as a body is a mutation");
    }

    /// <summary>
    /// A refusal is not a mutation. Nothing was changed, so the audit line that says something was is not
    /// written — the <c>9005</c> that says the request was turned away is.
    /// </summary>
    [Test]
    public async Task ARefusedMutationIsNotAudited()
    {
        StartApi(readOnly: true);

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        logs.WithEventId(AuditEventId).Should().BeEmpty(
            "auditing a refusal would put actions nobody was allowed to take into the record of what was done");
        logs.WithEventId(RefusedEventId).Should().ContainSingle(
            "the refusal is still logged, as it was before there was an audit line at all");
    }

    /// <summary>
    /// A read writes nothing, whichever verb it arrives with. The two bulk fetches are <c>POST</c>s that
    /// change nothing, so a rule written per verb would have filled the audit trail with them.
    /// </summary>
    [Test]
    public async Task AReadIsNotAudited()
    {
        StartApi();

        using (new AssertionScope())
        {
            await Served(HttpMethod.Get, "schedulers");
            await Served(HttpMethod.Get, $"{SchedulerUrl}/jobs");
            await Served(HttpMethod.Get, $"{SchedulerUrl}/triggers");
            await Served(HttpMethod.Post, $"{SchedulerUrl}/jobs/fetch", """[{"name":"existing","group":"group"}]""");
            await Served(HttpMethod.Post, $"{SchedulerUrl}/triggers/fetch", """[{"name":"existing","group":"group"}]""");
        }

        logs.WithEventId(AuditEventId).Should().BeEmpty("nothing was changed, so there is nothing to record");
    }

    /// <summary>
    /// A mutating route that failed is not audited either: what is recorded is what the scheduler did,
    /// not what was asked of it.
    /// </summary>
    [Test]
    public async Task AMutationThatFailedIsNotAudited()
    {
        StartApi();
        A.CallTo(() => fake.PauseAll(A<CancellationToken>._)).Throws(new SchedulerException("the store said no"));

        using HttpResponseMessage notFound = await client.PostAsync("schedulers/no-such-scheduler/pause-all", content: null);
        using HttpResponseMessage failed = await client.PostAsync($"{SchedulerUrl}/pause-all", content: null);

        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        failed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        logs.WithEventId(AuditEventId).Should().BeEmpty(
            "an operator reading the audit trail is reading what happened, and neither of these happened");
    }

    /// <summary>
    /// The line names the caller, which is the point of it: the identity the request authenticated as, or
    /// <c>(anonymous)</c> where nothing did.
    /// </summary>
    [Test]
    public async Task TheAuditLineNamesTheCaller()
    {
        StartApi(authenticated: true);

        using HttpRequestMessage identified = new(HttpMethod.Post, $"{SchedulerUrl}/pause-all");
        identified.Headers.Add(TenantAuthenticationHandler.TenantHeaderName, "ops@example.com");
        using HttpResponseMessage identifiedResponse = await client.SendAsync(identified);
        identifiedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage anonymousResponse = await client.PostAsync($"{SchedulerUrl}/resume-all", content: null);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        logs.WithEventId(AuditEventId).Select(x => x.Properties["User"]).Should()
            .Equal(["ops@example.com", "(anonymous)"],
                "an API mapped with AllowAnonymous() records that somebody who could reach it did this, which is all it knows");
    }

    /// <summary>
    /// The line arrives under the API's own logging category, with the refusals and the faults, and at
    /// <c>Information</c> — the only level anything about a request that succeeded is written at.
    /// </summary>
    [Test]
    public async Task TheAuditLineIsTheApisOwnCategoryAtInformation()
    {
        StartApi();

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        RecordedLogEntry entry = logs.WithEventId(AuditEventId).Should().ContainSingle().Which;
        entry.Category.Should().Be("Quartz.HttpApi",
            "everything the API logs about a request is filtered by one category, whichever type wrote it");
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Be($"Api user (anonymous) performed PauseAll on scheduler {TestData.SchedulerName}: /{SchedulerUrl}/pause-all");
    }

    /// <summary>
    /// Drives one mutating route and asserts the audit line it wrote.
    /// </summary>
    private async Task Audited(HttpMethod method, string url, string operation, string? requestJson = null)
    {
        logs.Clear();

        using HttpRequestMessage request = new(method, url);
        if (requestJson is not null)
        {
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        ((int) response.StatusCode).Should().BeInRange(200, 299, $"{method} {url} has to succeed to be audited, body was {body}");

        List<RecordedLogEntry> audited = logs.WithEventId(AuditEventId);
        audited.Should().ContainSingle($"{method} {url} changed something, exactly once").Which.Properties.Should()
            .Contain(new KeyValuePair<string, string?>("Operation", operation))
            .And.Contain(new KeyValuePair<string, string?>("SchedulerName", TestData.SchedulerName))
            .And.Contain(new KeyValuePair<string, string?>("Route", "/" + url.Split('?')[0]));
    }

    /// <summary>
    /// Drives one read and asserts only that it was served, so that the audit assertion the caller makes
    /// is about a request that reached a handler.
    /// </summary>
    private async Task Served(HttpMethod method, string url, string? requestJson = null)
    {
        using HttpRequestMessage request = new(method, url);
        if (requestJson is not null)
        {
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{method} {url} is a read, body was {body}");
    }

    /// <summary>
    /// The API, its logs captured, with one fake scheduler bound under the test scheduler's name.
    /// </summary>
    /// <remarks>
    /// A fake rather than a real scheduler because what is under test is what the API records, not what
    /// the scheduler does with it: every route then answers <c>2xx</c> without a store, and <c>shutdown</c>
    /// does not take the rest of the rows with it.
    /// </remarks>
    private void StartApi(bool readOnly = false, bool authenticated = false)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.AddQuartzHttpApi(options => options.ReadOnly = readOnly);
                if (authenticated)
                {
                    services.AddTenantAuthorization();
                }
            });
        });

        factories.Add(configured);

        fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => fake.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([], HasMore: false));
        A.CallTo(() => fake.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));
        A.CallTo(() => fake.PauseJobGroups(A<GroupMatcher<JobKey>>._, A<CancellationToken>._)).Returns(new List<string>());
        A.CallTo(() => fake.PauseJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._)).Returns(new List<JobKey>());

        client = configured.CreateClient();

        ISchedulerRepository repository = configured.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);

        // Nothing the audit line carries comes from the scheduler, so the noise of every other package in
        // the host is dropped rather than filtered on: what is left is what the API wrote.
        logs.Clear();
    }
}
