using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Pins the JSON bodies the HTTP API puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this folder reads the API through <see cref="HttpScheduler" />, which means a
/// serializer change that renamed, dropped or re-cased a field would keep passing as long as both ends
/// changed together — and every consumer that is not this client would break. So these tests speak raw
/// <see cref="HttpClient" /> and snapshot the bytes.
/// </para>
/// <para>
/// The data comes from <see cref="TestData.Wire" />, which is fixed instants and UTC throughout, so
/// almost nothing here needs scrubbing. Reviewing a diff on one of these files means asking whether
/// the API's consumers were meant to see it.
/// </para>
/// </remarks>
public class WireFormatSnapshotTest : WebApiTest
{
    private const string SchedulerUrl = "schedulers/" + TestData.SchedulerName;

    private static readonly JobKey jobKey = TestData.JobDetail.Key;

    [Test]
    public async Task JobDetailBody()
    {
        A.CallTo(() => FakeScheduler.GetJobDetail(jobKey, A<CancellationToken>._)).Returns(TestData.JobDetail);

        string body = await Get($"{SchedulerUrl}/jobs/{jobKey.Group}/{jobKey.Name}");
        await VerifyBody(body);
    }

    [Test]
    public async Task SimpleTriggerBody() => await VerifyTriggerBody(TestData.Wire.SimpleTrigger);

    [Test]
    public async Task CronTriggerBody() => await VerifyTriggerBody(TestData.Wire.CronTrigger);

    [Test]
    public async Task CalendarIntervalTriggerBody() => await VerifyTriggerBody(TestData.Wire.CalendarIntervalTrigger);

    [Test]
    public async Task DailyTimeIntervalTriggerBody() => await VerifyTriggerBody(TestData.Wire.DailyTimeIntervalTrigger);

    [Test]
    public async Task RecurrenceTriggerBody() => await VerifyTriggerBody(TestData.Wire.RecurrenceTrigger);

    [Test]
    public async Task CalendarBody()
    {
        A.CallTo(() => FakeScheduler.GetCalendar("HolidayCalendar", A<CancellationToken>._)).Returns(TestData.Wire.HolidayCalendar);

        string body = await Get($"{SchedulerUrl}/calendars/HolidayCalendar");
        await VerifyBody(body);
    }

    [Test]
    public async Task PagedListingBody()
    {
        // one page of a larger result: the envelope has to carry both the "there is more" flag and the
        // total the caller asked to be counted
        A.CallTo(() => FakeScheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([JobHeaderFor(TestData.JobDetail)], HasMore: true, TotalCount: 5));

        string body = await Get($"{SchedulerUrl}/jobs?skip=2&take=1&includeTotalCount=true");
        await VerifyBody(body);
    }

    [Test]
    public async Task SchedulerBody()
    {
        A.CallTo(() => FakeScheduler.GetMetadata(A<CancellationToken>._)).Returns(TestData.Wire.Metadata);
        A.CallTo(() => FakeScheduler.IsStarted).Returns(TestData.Wire.Metadata.Started);
        A.CallTo(() => FakeScheduler.InStandbyMode).Returns(TestData.Wire.Metadata.InStandbyMode);
        A.CallTo(() => FakeScheduler.IsShutdown).Returns(TestData.Wire.Metadata.Shutdown);

        string body = await Get(SchedulerUrl);
        await VerifyBody(body);
    }

    [Test]
    public async Task ValidationProblemDetailsBody()
    {
        // a job detail with no name: the request never reaches the scheduler, and the caller gets the
        // validation message rather than a bare status code
        const string requestJson = """
            {
              "job": { "group": "DummyGroup", "jobType": "Quartz.Tests.AspNetCore.Support.DummyJob" },
              "replace": false
            }
            """;

        string body = await Post($"{SchedulerUrl}/jobs", requestJson, HttpStatusCode.BadRequest);

        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<AddJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
        await VerifyBody(body);
    }

    [Test]
    public async Task UnknownSchedulerProblemDetailsBody()
    {
        string body = await Get("schedulers/no-such-scheduler", HttpStatusCode.NotFound);
        await VerifyBody(body);
    }

    [Test]
    public async Task UnknownJobProblemDetailsBody()
    {
        A.CallTo(() => FakeScheduler.GetJobDetail(A<JobKey>._, A<CancellationToken>._)).Returns(null);

        string body = await Get($"{SchedulerUrl}/jobs/DummyGroup/no-such-job", HttpStatusCode.NotFound);
        await VerifyBody(body);
    }

    /// <summary>
    /// The status code an operation answers with is as much of a contract as the body, and the
    /// conventions are not the obvious ones: a mutation that found nothing to change still answers 200
    /// with a flag in the body, and only a missing scheduler or a missing read target is a 404.
    /// </summary>
    [Test]
    public async Task StatusCodesFollowTheApiConventions()
    {
        JobKey existingJob = new("existing", "group");
        JobKey missingJob = new("missing", "group");
        TriggerKey existingTrigger = new("existing", "group");
        TriggerKey missingTrigger = new("missing", "group");

        A.CallTo(() => FakeScheduler.GetJobDetail(existingJob, A<CancellationToken>._)).Returns(TestData.JobDetail);
        A.CallTo(() => FakeScheduler.GetJobDetail(missingJob, A<CancellationToken>._)).Returns(null);
        A.CallTo(() => FakeScheduler.GetTrigger(existingTrigger, A<CancellationToken>._)).Returns(TestData.Wire.SimpleTrigger);
        A.CallTo(() => FakeScheduler.GetTrigger(missingTrigger, A<CancellationToken>._)).Returns(null);
        A.CallTo(() => FakeScheduler.GetCalendar("existing", A<CancellationToken>._)).Returns(TestData.Wire.HolidayCalendar);
        A.CallTo(() => FakeScheduler.GetCalendar("missing", A<CancellationToken>._)).Returns(null);
        A.CallTo(() => FakeScheduler.PauseJob(existingJob, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.PauseJob(missingJob, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => FakeScheduler.ResumeJob(existingJob, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.DeleteJob(existingJob, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.DeleteJob(missingJob, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => FakeScheduler.DeleteCalendar("existing", A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.DeleteCalendar("missing", A<CancellationToken>._)).Returns(false);

        const string addJobJson = """
            {
              "job": {
                "name": "existing",
                "group": "group",
                "jobType": "Quartz.Tests.AspNetCore.Support.DummyJob",
                "durable": true,
                "requestsRecovery": false,
                "concurrentExecutionDisallowed": false,
                "persistJobDataAfterExecution": false,
                "jobDataMap": { "TestKey": "TestValue" }
              },
              "replace": true
            }
            """;

        using HttpClient httpClient = WebApplicationFactory.CreateClient();
        using (new AssertionScope())
        {
            // reads: found is 200, missing is 404 with problem details
            await Row(HttpMethod.Get, $"{SchedulerUrl}/jobs/group/existing", HttpStatusCode.OK);
            await Row(HttpMethod.Get, $"{SchedulerUrl}/jobs/group/missing", HttpStatusCode.NotFound);
            await Row(HttpMethod.Get, $"{SchedulerUrl}/triggers/group/existing", HttpStatusCode.OK);
            await Row(HttpMethod.Get, $"{SchedulerUrl}/triggers/group/missing", HttpStatusCode.NotFound);
            await Row(HttpMethod.Get, $"{SchedulerUrl}/calendars/existing", HttpStatusCode.OK);
            await Row(HttpMethod.Get, $"{SchedulerUrl}/calendars/missing", HttpStatusCode.NotFound);

            // an unknown scheduler is a 404 whatever the operation was
            await Row(HttpMethod.Get, "schedulers/no-such-scheduler", HttpStatusCode.NotFound);
            await Row(HttpMethod.Get, "schedulers/no-such-scheduler/jobs/group/existing", HttpStatusCode.NotFound);
            await Row(HttpMethod.Post, "schedulers/no-such-scheduler/jobs/group/existing/pause", HttpStatusCode.NotFound);

            // writes that succeeded but produced nothing to say: 200 with an empty body
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs", HttpStatusCode.OK, addJobJson, expectedBody: "");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/trigger", HttpStatusCode.OK, expectedBody: "");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/pause-all", HttpStatusCode.OK, expectedBody: "");

            // pause/resume: a key that was not there is not an error, it is 200 with applied=false
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/pause", HttpStatusCode.OK, expectedBody: """{"applied":true}""");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/missing/pause", HttpStatusCode.OK, expectedBody: """{"applied":false}""");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/resume", HttpStatusCode.OK, expectedBody: """{"applied":true}""");

            // deletes follow the same rule, under a name of their own
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/jobs/group/existing", HttpStatusCode.OK, expectedBody: """{"jobFound":true}""");
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/jobs/group/missing", HttpStatusCode.OK, expectedBody: """{"jobFound":false}""");
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/calendars/existing", HttpStatusCode.OK, expectedBody: """{"calendarFound":true}""");
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/calendars/missing", HttpStatusCode.OK, expectedBody: """{"calendarFound":false}""");

            // a malformed request is a 400, never a 404 or a 500
            await Row(HttpMethod.Get, $"{SchedulerUrl}/jobs?skip=-1", HttpStatusCode.BadRequest);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs", HttpStatusCode.BadRequest, """{"replace":true}""");
        }

        async Task Row(HttpMethod method, string url, HttpStatusCode expectedStatusCode, string? requestJson = null, string? expectedBody = null)
        {
            using HttpRequestMessage request = new(method, url);
            if (requestJson is not null)
            {
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            }

            using HttpResponseMessage response = await httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(expectedStatusCode, $"{method} {url} answers {expectedStatusCode:D}, body was {body}");
            if (expectedBody is not null)
            {
                body.Should().Be(expectedBody, $"{method} {url} answers with exactly this body");
            }
        }
    }

    private async Task VerifyTriggerBody(ITrigger trigger, [CallerMemberName] string testMethod = "")
    {
        A.CallTo(() => FakeScheduler.GetTrigger(trigger.Key, A<CancellationToken>._)).Returns(trigger);

        string body = await Get($"{SchedulerUrl}/triggers/{trigger.Key.Group}/{trigger.Key.Name}");
        await VerifyBody(body, testMethod);
    }

    private async Task<string> Get(string url, HttpStatusCode expectedStatusCode = HttpStatusCode.OK)
    {
        using HttpClient httpClient = WebApplicationFactory.CreateClient();
        using HttpResponseMessage response = await httpClient.GetAsync(url);

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatusCode, $"GET {url} answers {expectedStatusCode:D}, body was {body}");
        return body;
    }

    private async Task<string> Post(string url, string requestJson, HttpStatusCode expectedStatusCode = HttpStatusCode.OK)
    {
        using HttpClient httpClient = WebApplicationFactory.CreateClient();
        using StringContent content = new(requestJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.PostAsync(url, content);

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatusCode, $"POST {url} answers {expectedStatusCode:D}, body was {body}");
        return body;
    }

    private static SettingsTask VerifyBody(string body, [CallerMemberName] string testMethod = "")
    {
        return VerifyJson(body)
            // strict JSON, so that the snapshot keeps the one thing a relaxed rendering drops: whether a
            // value went out as a string or as a number. A job data map entry's JSON type is part of the
            // contract - the reader branches on it.
            .UseStrictJson()
            // an empty job data map is a field the API emits and its readers parse, so it belongs in the
            // snapshot; Verify drops empty collections unless told not to
            .DontIgnoreEmptyCollections()
            // the instants in these bodies are fixed by TestData.Wire, and pinning them is the point -
            // Verify's default scrubbing would replace them with placeholders
            .DontScrubDateTimes()
            .DontScrubGuids()
            .UseDirectory("../Verify")
            .UseFileName($"WireFormatSnapshotTest_{testMethod}")
            .DisableRequireUniquePrefix();
    }

    private static JobHeader JobHeaderFor(IJobDetail jobDetail) => new(
        jobDetail.Key,
        Description: jobDetail.Description,
        JobTypeName: jobDetail.JobType.FullName!,
        Durable: jobDetail.Durable,
        ConcurrentExecutionDisallowed: jobDetail.ConcurrentExecutionDisallowed,
        PersistJobDataAfterExecution: jobDetail.PersistJobDataAfterExecution,
        RequestsRecovery: jobDetail.RequestsRecovery);
}
