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
    public async Task TriggerListingBody()
    {
        // the trigger listing is where the wire's enums live: a header carries the trigger's state, and
        // it goes out as its name for the same reason the trigger body's repeatIntervalUnit does
        A.CallTo(() => FakeScheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([TriggerHeaderFor(TestData.Wire.CronTrigger, TriggerState.Paused)], HasMore: false, TotalCount: null));

        string body = await Get($"{SchedulerUrl}/triggers");
        await VerifyBody(body);
    }

    [Test]
    public async Task FireInstanceListingBody()
    {
        // Both shapes in one page: a running firing with every member populated, and a reserved one
        // whose job key, scheduled time and execution group are all absent. The state goes out as its
        // name, and the absent members as nulls rather than as missing properties.
        A.CallTo(() => FakeScheduler.QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstance>(
                [TestData.ExecutingFireInstance, TestData.AcquiredFireInstance],
                HasMore: false,
                TotalCount: 2));

        string body = await Get($"{SchedulerUrl}/jobs/fire-instances?includeTotalCount=true&state=Any");
        await VerifyBody(body);
    }

    [Test]
    public async Task ClusterNodesBody()
    {
        // Three shapes in one body: the node answering, a peer the cluster has given up on, and one
        // whose store keeps no check-in history at all. The state goes out as its name, the interval as
        // a TimeSpan like every other duration the API carries, and the absent times as nulls rather
        // than as zeros or as missing properties.
        A.CallTo(() => FakeScheduler.QueryClusterNodes(A<CancellationToken>._))
            .Returns(new List<ClusterNode>
            {
                TestData.CurrentClusterNode,
                TestData.FailedClusterNode,
                TestData.ClusterNodeWithoutCheckIn
            });

        string body = await Get($"{SchedulerUrl}/nodes");
        await VerifyBody(body);
    }

    [Test]
    public async Task TriggerStateBody()
    {
        TriggerKey triggerKey = TestData.Wire.CronTrigger.Key;
        A.CallTo(() => FakeScheduler.GetTriggerState(triggerKey, A<CancellationToken>._)).Returns(TriggerState.Blocked);

        string body = await Get($"{SchedulerUrl}/triggers/{triggerKey.Group}/{triggerKey.Name}/state");
        await VerifyBody(body);
    }

    /// <summary>
    /// The scheduler listing, whose two shapes are the point of it: a scheduler that exists, and a
    /// registration nothing has built.
    /// </summary>
    /// <remarks>
    /// The test application registers a default scheduler and never builds it, so the second shape is
    /// there without arranging anything — <c>status</c> and <c>schedulerInstanceId</c> both <c>null</c>,
    /// rather than the entry being missing as it was before the listing read the registry. <c>origin</c>
    /// goes out as its name, like every other enum on this wire.
    /// </remarks>
    [Test]
    public async Task SchedulerListingBody()
    {
        A.CallTo(() => FakeScheduler.Status).Returns(SchedulerStatus.Running);

        string body = await Get("schedulers");
        await VerifyBody(body);
    }

    [Test]
    public async Task SchedulerBody()
    {
        A.CallTo(() => FakeScheduler.GetMetadata(A<CancellationToken>._)).Returns(TestData.Wire.Metadata);
        A.CallTo(() => FakeScheduler.Status).Returns(TestData.Wire.Metadata.Status);

        string body = await Get(SchedulerUrl);
        await VerifyBody(body);
    }

    /// <summary>
    /// The scheduler context, which is the one body whose values the application chose the types of.
    /// </summary>
    /// <remarks>
    /// Every one of them goes out as a JSON string: the context is a <c>Map&lt;String, Object&gt;</c> in
    /// the scheduler's own process, and text is what a remote reader can use. The number and the instant
    /// are here to pin the rendering as invariant — an <c>int</c> must not pick up a group separator and
    /// a <c>DateTimeOffset</c> must not pick up the server's date format — and the null to say an absent
    /// value stays absent rather than becoming the empty string.
    /// </remarks>
    [Test]
    public async Task SchedulerContextBody()
    {
        A.CallTo(() => FakeScheduler.Context).Returns(new SchedulerContext
        {
            { "tenant", "acme" },
            { "retries", 4352 },
            { "activeFrom", new DateTimeOffset(2025, 6, 1, 12, 30, 0, TimeSpan.Zero) },
            { "nothing", null }
        });

        string body = await Get($"{SchedulerUrl}/context");
        await VerifyBody(body);
    }

    [Test]
    public async Task AppliedJobKeysBody()
    {
        // a key-set pause answers with the keys it applied to: the plural of {"applied": bool}. The key
        // the store did not find is absent from the answer rather than present with a false beside it.
        A.CallTo(() => FakeScheduler.PauseJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { new JobKey("first", "group") });

        const string requestJson = """
            {
              "jobs": [
                { "name": "first", "group": "group" },
                { "name": "missing", "group": "group" }
              ]
            }
            """;

        string body = await Post($"{SchedulerUrl}/jobs/keys/pause", requestJson);
        await VerifyBody(body);
    }

    [Test]
    public async Task AppliedTriggerKeysBody()
    {
        A.CallTo(() => FakeScheduler.ResetTriggersFromErrorState(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { new TriggerKey("first", "group"), new TriggerKey("second", "group") });

        const string requestJson = """
            {
              "triggers": [
                { "name": "first", "group": "group" },
                { "name": "second", "group": "group" }
              ]
            }
            """;

        string body = await Post($"{SchedulerUrl}/triggers/keys/reset-from-error-state", requestJson);
        await VerifyBody(body);
    }

    [Test]
    public async Task AppliedKeysBodyIsEmptyWhenNothingApplied()
    {
        A.CallTo(() => FakeScheduler.ResumeTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey>());

        const string requestJson = """{ "triggers": [ { "name": "first", "group": "group" } ] }""";

        string body = await Post($"{SchedulerUrl}/triggers/keys/resume", requestJson);
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

    /// <summary>
    /// The other way to earn a 400: the request was well formed, and the scheduler refused it.
    /// </summary>
    /// <remarks>
    /// This body had no pin at all, which is how the two 400 shapes came to differ unnoticed. Read it
    /// beside <see cref="ValidationProblemDetailsBody" />: the members are the same, and only the
    /// <c>detail</c> and the exception name say which layer rejected the request.
    /// </remarks>
    [Test]
    public async Task SchedulerExceptionProblemDetailsBody()
    {
        A.CallTo(() => FakeScheduler.PauseAll(A<CancellationToken>._))
            .Throws(_ => new SchedulerException("The scheduler has been shut down"));

        string body = await Post($"{SchedulerUrl}/pause-all", requestJson: "", HttpStatusCode.BadRequest);
        await VerifyBody(body);
    }

    /// <summary>
    /// A fault the caller cannot act on: the same problem-details members, without the exception type.
    /// </summary>
    /// <remarks>
    /// The client-actionable errors name the type they came from so that a caller can reconstruct and
    /// handle them. A 500 is not one of those, and naming the type behind it would only say something
    /// about this server's internals — so the member's absence here is contract, and pinned as such.
    /// </remarks>
    [Test]
    public async Task ServerFaultProblemDetailsBody()
    {
        A.CallTo(() => FakeScheduler.PauseAll(A<CancellationToken>._))
            .Throws(_ => new InvalidOperationException("Something the API never promised"));

        string body = await Post($"{SchedulerUrl}/pause-all", requestJson: "", HttpStatusCode.InternalServerError);
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
    /// saying so in the body — a flag, or an empty list of applied keys — and only a missing scheduler
    /// or a missing read target is a 404.
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
        A.CallTo(() => FakeScheduler.PauseJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { existingJob });
        A.CallTo(() => FakeScheduler.DeleteJob(existingJob, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.DeleteJob(missingJob, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => FakeScheduler.DeleteJobs(A<IReadOnlyCollection<JobKey>>._, A<CancellationToken>._))
            .Returns(new List<JobKey> { existingJob });
        A.CallTo(() => FakeScheduler.UnscheduleJobs(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { existingTrigger });
        A.CallTo(() => FakeScheduler.DeleteCalendar("existing", A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.DeleteCalendar("missing", A<CancellationToken>._)).Returns(false);
        A.CallTo(() => FakeScheduler.UnscheduleJob(existingTrigger, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.UnscheduleJob(missingTrigger, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => FakeScheduler.Interrupt(existingJob, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.Interrupt(missingJob, A<CancellationToken>._)).Returns(false);

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

            // the key-set forms answer with the keys they applied to — the plural of the same rule, so a
            // key the store did not find is missing from the list rather than an error
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/keys/pause", HttpStatusCode.OK,
                """{"jobs":[{"name":"existing","group":"group"},{"name":"missing","group":"group"}]}""",
                expectedBody: """{"jobs":[{"name":"existing","group":"group"}]}""");

            // ...and a key set with a nameless key never reaches the scheduler
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/keys/pause", HttpStatusCode.BadRequest,
                """{"jobs":[{"group":"group"}]}""");

            // deletes, unschedules and interrupts say the same word: every mutation that can be a
            // no-op answers {"applied": …}, whatever it is a mutation of
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/jobs/group/existing", HttpStatusCode.OK, expectedBody: """{"applied":true}""");
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/jobs/group/missing", HttpStatusCode.OK, expectedBody: """{"applied":false}""");
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/calendars/existing", HttpStatusCode.OK, expectedBody: """{"applied":true}""");
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/calendars/missing", HttpStatusCode.OK, expectedBody: """{"applied":false}""");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/triggers/group/existing/unschedule", HttpStatusCode.OK, expectedBody: """{"applied":true}""");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/triggers/group/missing/unschedule", HttpStatusCode.OK, expectedBody: """{"applied":false}""");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/interrupt", HttpStatusCode.OK, expectedBody: """{"applied":true}""");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/missing/interrupt", HttpStatusCode.OK, expectedBody: """{"applied":false}""");

            // the plural delete and unschedule are key sets, so they answer the way every other key
            // set does: with what they applied to. A partial hit deletes the keys it found and names
            // exactly those, which no single flag could say.
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/delete", HttpStatusCode.OK,
                """{"jobs":[{"name":"existing","group":"group"},{"name":"missing","group":"group"}]}""",
                expectedBody: """{"jobs":[{"name":"existing","group":"group"}]}""");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/triggers/unschedule", HttpStatusCode.OK,
                """{"triggers":[{"name":"existing","group":"group"}]}""",
                expectedBody: """{"triggers":[{"name":"existing","group":"group"}]}""");

            // a malformed request is a 400, never a 404 or a 500
            await Row(HttpMethod.Get, $"{SchedulerUrl}/jobs?skip=-1", HttpStatusCode.BadRequest);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs", HttpStatusCode.BadRequest, """{"replace":true}""");

            // take is read by the endpoint rather than bound by the framework, because ?take=all is a
            // spelling only the endpoint knows - so an unparseable one carries problem details saying so
            await Row(HttpMethod.Get, $"{SchedulerUrl}/jobs?take=not-a-number", HttpStatusCode.BadRequest);

            // ...but a 400 does not always carry a body: a query parameter the framework could not bind
            // never reaches the endpoint, so it answers with the status code alone, where a request the
            // endpoint rejected answers with problem details saying why
            await Row(HttpMethod.Get, $"{SchedulerUrl}/jobs?skip=not-a-number", HttpStatusCode.BadRequest, expectedBody: "");
            await Row(HttpMethod.Get, $"{SchedulerUrl}/triggers?state=not-a-state", HttpStatusCode.BadRequest, expectedBody: "");
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

    private static TriggerHeader TriggerHeaderFor(ITrigger trigger, TriggerState state) => new(
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
        ExecutionGroup: trigger.ExecutionGroup,
        RetryPolicy: trigger.RetryPolicy?.ToStoredString(),
        RetryAttempt: trigger.RetryAttempt);

    private static JobHeader JobHeaderFor(IJobDetail jobDetail) => new(
        jobDetail.Key,
        Description: jobDetail.Description,
        JobTypeName: jobDetail.JobType.FullName!,
        Durable: jobDetail.Durable,
        ConcurrentExecutionDisallowed: jobDetail.ConcurrentExecutionDisallowed,
        PersistJobDataAfterExecution: jobDetail.PersistJobDataAfterExecution,
        RequestsRecovery: jobDetail.RequestsRecovery);
}
