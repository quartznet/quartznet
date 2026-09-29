using System.Net;
using System.Text;

using Quartz.Impl;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The reader of a remote scheduler's execution history: what it asks for, what it hands back, and the
/// two things it refuses to pretend about.
/// </summary>
public class HttpExecutionHistoryStoreTest
{
    private StubHandler handler;
    private HttpClient httpClient;

    [SetUp]
    public void SetUp()
    {
        handler = new StubHandler();
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080/")
        };
    }

    [TearDown]
    public void TearDown()
    {
        httpClient?.Dispose();
        handler?.Dispose();
        httpClient = null;
        handler = null;
    }

    [Test]
    public async Task ExecutionsAreReadFromTheTargetsHistoryRoute()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "schedulerInstanceId": "node-a",
                  "jobGroup": "DummyGroup",
                  "jobName": "nightly",
                  "triggerGroup": "DummyTriggerGroup",
                  "triggerName": "at-midnight",
                  "firedAtUtc": "2026-08-26T12:00:00+00:00",
                  "duration": "00:00:01.5000000",
                  "succeeded": false,
                  "exceptionMessage": "the job threw"
                }
              ],
              "hasMore": true,
              "totalCount": 7
            }
            """);

        PagedResult<ExecutionHistoryEntry> page = await Store().QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = "Remote",
            SchedulerInstanceId = "node-a",
            JobContains = "night",
            TriggerContains = "midnight",
            Skip = 10,
            Take = 5,
            IncludeTotalCount = true
        });

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/executions"
            + "?skip=10&take=5&includeTotalCount=true&schedulerInstanceId=node-a&triggerContains=midnight&jobContains=night");

        ExecutionHistoryEntry entry = page.Items.Should().ContainSingle().Subject;
        entry.SchedulerName.Should().Be("Remote", "the rows do not carry it — the route said it, and this client knows it");
        entry.JobName.Should().Be("nightly");
        entry.Duration.Should().Be(TimeSpan.FromMilliseconds(1500));
        entry.Succeeded.Should().BeFalse();
        entry.ExceptionMessage.Should().Be("the job threw");
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(7);
    }

    [Test]
    public async Task TheAttemptAndTheRetryFlagTravelWithEachRow()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "schedulerInstanceId": "node-a",
                  "jobGroup": "DummyGroup",
                  "jobName": "nightly",
                  "triggerGroup": "DummyTriggerGroup",
                  "triggerName": "at-midnight",
                  "firedAtUtc": "2026-08-26T12:00:00+00:00",
                  "duration": "00:00:01.5000000",
                  "succeeded": false,
                  "exceptionMessage": "the job threw",
                  "retryAttempt": 2,
                  "retryScheduled": true
                },
                {
                  "schedulerInstanceId": "node-a",
                  "jobGroup": "DummyGroup",
                  "jobName": "hourly",
                  "triggerGroup": "DummyTriggerGroup",
                  "triggerName": "on-the-hour",
                  "firedAtUtc": "2026-08-26T11:00:00+00:00",
                  "duration": "00:00:00.5000000",
                  "succeeded": true,
                  "exceptionMessage": null
                }
              ],
              "hasMore": false,
              "totalCount": 2
            }
            """);

        PagedResult<ExecutionHistoryEntry> page = await Store().QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = "Remote"
        });

        page.Items[0].RetryAttempt.Should().Be(2);
        page.Items[0].RetryScheduled.Should().BeTrue();

        page.Items[1].RetryAttempt.Should().Be(0, "a host that sends neither is a host with nothing to say about retries");
        page.Items[1].RetryScheduled.Should().BeFalse();
    }

    [TestCase(true, "failedFinally=true")]
    [TestCase(false, "failedFinally=false")]
    public async Task TheFinalFailureFilterIsAskedForOverTheWire(bool failedFinally, string expected)
    {
        handler.Respond(HttpStatusCode.OK, """{ "items": [], "hasMore": false, "totalCount": 0 }""");

        await Store().QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = "Remote",
            FailedFinally = failedFinally
        });

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/executions?take=250&" + expected,
            "a filter this client dropped would be a page that quietly answered a different question");
    }

    [Test]
    public async Task AnUnaskedFinalFailureFilterIsNotSent()
    {
        handler.Respond(HttpStatusCode.OK, """{ "items": [], "hasMore": false, "totalCount": 0 }""");

        await Store().QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "Remote" });

        handler.LastRequestUri.Should().Be("http://localhost:8080/schedulers/Remote/history/executions?take=250");
    }

    [Test]
    public async Task MisfiresAreReadFromTheTargetsHistoryRoute()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "schedulerInstanceId": "node-a",
                  "triggerGroup": "DummyTriggerGroup",
                  "triggerName": "at-midnight",
                  "jobKey": { "name": "nightly", "group": "DummyGroup" },
                  "misfiredAtUtc": "2026-08-26T12:00:00+00:00",
                  "scheduledFireTimeUtc": "2026-08-26T11:55:00+00:00"
                }
              ],
              "hasMore": false,
              "totalCount": 1
            }
            """);

        PagedResult<MisfireHistoryEntry> page = await Store().QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = "Remote",
            TriggerContains = "midnight"
        });

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/misfires?take=250&triggerContains=midnight"
            + "&reasons=Missed&reasons=Overlap&reasons=Vetoed",
            "a 4.4 host that is asked for no reason leaves Vetoed out for older clients, so this one names every reason it can read");
        handler.Requests.Should().ContainSingle("naming every reason is what a host before 4.4 answers anyway, so no version is asked");

        MisfireHistoryEntry entry = page.Items.Should().ContainSingle().Subject;
        entry.JobKey.Should().Be(new JobKey("nightly", "DummyGroup"));
        entry.ScheduledFireTimeUtc.Should().Be(new DateTimeOffset(2026, 8, 26, 11, 55, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task MisfiresAreCountedOverAWindow()
    {
        handler.Respond(HttpStatusCode.OK, """{"count":3}""");

        int count = await Store().CountMisfires("Remote", new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/misfires/count?since=2026-08-26T12%3A00%3A00.0000000%2B00%3A00");
        count.Should().Be(3);
    }

    /// <summary>
    /// A target whose API has no history route is a target that serves no history, which is something a
    /// page can say — rather than an error a page has to render as a failure.
    /// </summary>
    /// <remarks>
    /// An unmatched route answers <c>404</c> with no body at all, which is what a Quartz HTTP API older
    /// than 4.1 does for these routes.
    /// </remarks>
    [Test]
    public async Task ATargetWithoutTheHistoryRoutesSaysItServesNoHistory()
    {
        handler.Respond(HttpStatusCode.NotFound, body: "");

        Func<Task> act = async () => await Store().QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "Remote" });

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*does not serve history*");
    }

    /// <summary>
    /// The other <c>404</c> — the one that names an unknown scheduler — is a mistake rather than a
    /// missing capability, and still arrives as itself.
    /// </summary>
    [Test]
    public async Task AnUnknownSchedulerIsStillAnUnknownScheduler()
    {
        handler.Respond(HttpStatusCode.NotFound, """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
              "title": "Not Found",
              "status": 404,
              "detail": "Unknown scheduler Remote"
            }
            """);

        Func<Task> act = async () => await Store().QueryMisfires(new MisfireHistoryQuery { SchedulerName = "Remote" });

        await act.Should().ThrowAsync<HttpClientException>()
            .WithMessage("*Scheduler not found*");
    }

    [Test]
    public async Task OneExecutionIsReadFromItsOwnRouteWithItsLog()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "schedulerInstanceId": "node-a",
              "jobGroup": "DummyGroup",
              "jobName": "nightly",
              "triggerGroup": "DummyTriggerGroup",
              "triggerName": "at-midnight",
              "firedAtUtc": "2026-08-26T12:00:00+00:00",
              "duration": "00:00:01.5000000",
              "succeeded": true,
              "exceptionMessage": null,
              "entryId": "a?b#c",
              "log": "first\nsecond"
            }
            """);

        ExecutionHistoryEntry entry = await Store().GetExecution("Remote", "a?b#c");

        handler.LastRequestUri.Should().Be("http://localhost:8080/schedulers/Remote/history/executions/a%3Fb%23c",
            "the key is one path segment, escaped, however it is spelled");

        entry.Should().NotBeNull();
        entry.SchedulerName.Should().Be("Remote");
        entry.EntryId.Should().Be("a?b#c");
        entry.Log.Should().Be("first\nsecond", "the single-entry route is the one read that carries the log");
    }

    /// <summary>
    /// An id with a <c>/</c> cannot be one path segment: ASP.NET Core keeps <c>%2F</c> escaped, so the target
    /// would look up <c>a%2Fb</c> and answer that there is no such row (#3917).
    /// </summary>
    [Test]
    public async Task AnEntryIdWithASlashIsRefusedBeforeAnythingIsSent()
    {
        Func<Task> act = async () => await Store().GetExecution("Remote", "a/b");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("The entryId 'a/b' cannot be sent in the path of GetExecution*");
        handler.LastRequestUri.Should().BeNull("a request the target would answer for another id is not sent");
    }

    [Test]
    public async Task AnExecutionTheTargetDoesNotHaveIsNull()
    {
        handler.Respond(HttpStatusCode.NotFound, """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
              "title": "Not Found",
              "status": 404,
              "detail": "Unknown execution gone"
            }
            """);

        (await Store().GetExecution("Remote", "gone")).Should().BeNull(
            "the target answered, and what it said is that there is no such row");
    }

    /// <summary>
    /// A 4.2 target has the listing but not the single-entry route, and says so the way a target without
    /// history routes does.
    /// </summary>
    [Test]
    public async Task ATargetWithoutTheSingleEntryRouteSaysItServesNoSingleExecutions()
    {
        handler.Respond(HttpStatusCode.NotFound, body: "");

        Func<Task> act = async () => await Store().GetExecution("Remote", "entry-1");

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*does not serve single executions*");
    }

    [Test]
    public async Task NothingIsRecordedThroughTheWire()
    {
        ExecutionHistoryEntry execution = new(
            "Remote", "node-a", "DummyGroup", "nightly", "DummyTriggerGroup", "at-midnight",
            DateTimeOffset.UtcNow, TimeSpan.Zero, Succeeded: true, ExceptionMessage: null);

        Func<Task> writeExecution = async () => await Store().AddExecution(execution);
        Func<Task> writeMisfire = async () => await Store().AddMisfire(new MisfireHistoryEntry(
            "Remote", "node-a", "DummyTriggerGroup", "at-midnight", JobKey: null, DateTimeOffset.UtcNow, null));

        await writeExecution.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*history is recorded where the scheduler runs*");
        await writeMisfire.Should().ThrowAsync<NotSupportedException>();
    }

    [Test]
    public async Task TheFourFiltersAreSentToAHostThatReadsThem()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.4.0.0"));
        handler.Respond(HttpStatusCode.OK, EmptyPage);

        await Store().QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = "Remote",
            Job = new JobKey("release-stale", "billing"),
            FiredFrom = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            FiredBefore = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            Results = [JobRunResult.Failed, JobRunResult.Cancelled]
        });

        handler.Requests.Should().Equal(
        [
            "GET http://localhost:8080/schedulers/Remote",
            "GET http://localhost:8080/schedulers/Remote/history/executions?take=250&jobGroup=billing&jobName=release-stale"
            + "&firedFrom=2026-09-01T00%3A00%3A00.0000000%2B00%3A00&firedBefore=2026-09-02T00%3A00%3A00.0000000%2B00%3A00"
            + "&results=Failed&results=Cancelled"
        ], "the host's version is read before the first filtered read, and every filter the query carries is then sent");
    }

    /// <summary>
    /// A 4.3 host ignores query parameters it does not know, so a filter sent to it would come back as an
    /// unfiltered page that looks filtered.
    /// </summary>
    [TestCase("job")]
    [TestCase("firedFrom")]
    [TestCase("firedBefore")]
    [TestCase("results")]
    public async Task AFilteredReadIsRefusedBeforeItReachesAnOlderHost(string filter)
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.3.0.0"));
        handler.Respond(HttpStatusCode.OK, EmptyPage);

        ExecutionHistoryQuery query = new() { SchedulerName = "Remote" };
        query = filter switch
        {
            "job" => query with { Job = new JobKey("nightly", "reports") },
            "firedFrom" => query with { FiredFrom = DateTimeOffset.UnixEpoch },
            "firedBefore" => query with { FiredBefore = DateTimeOffset.UnixEpoch },
            _ => query with { Results = [JobRunResult.Skipped] }
        };

        Func<Task> act = async () => await Store().QueryExecutions(query);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*'Remote'*4.3.0.0*4.4*");
        handler.Requests.Should().Equal(["GET http://localhost:8080/schedulers/Remote"],
            "the history route is never asked a question the host would answer wrongly");
    }

    [Test]
    public async Task AHostSeenToFilterIsNotAskedItsVersionAgain()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.4.0.0"));
        handler.Respond(HttpStatusCode.OK, EmptyPage);
        HttpExecutionHistoryStore store = Store();

        await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "Remote", Results = [JobRunResult.Failed] });
        await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = "Remote", Job = new JobKey("nightly", "reports") });

        handler.Requests.Count(request => request.EndsWith("/schedulers/Remote", StringComparison.Ordinal)).Should().Be(1,
            "a page that refreshes every second must not ask the host its version every second");
    }

    [Test]
    public async Task AnOlderHostIsAskedAgainSoAnUpgradeIsNoticed()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.3.0.0"));
        handler.Respond(HttpStatusCode.OK, EmptyPage);
        HttpExecutionHistoryStore store = Store();
        ExecutionHistoryQuery query = new() { SchedulerName = "Remote", Results = [JobRunResult.Failed] };

        Func<Task> refused = async () => await store.QueryExecutions(query);
        await refused.Should().ThrowAsync<NotSupportedException>();

        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.4.1.0"));
        await store.QueryExecutions(query);

        handler.Requests.Should().EndWith("GET http://localhost:8080/schedulers/Remote/history/executions?take=250&results=Failed",
            "a host upgraded under a dashboard that keeps running serves the filter as soon as it can");
    }

    [Test]
    public async Task AnEmptySetListsNothingWithoutAsking()
    {
        PagedResult<ExecutionHistoryEntry> executions = await Store().QueryExecutions(
            new ExecutionHistoryQuery { SchedulerName = "Remote", Results = [], IncludeTotalCount = true });
        PagedResult<MisfireHistoryEntry> misfires = await Store().QueryMisfires(
            new MisfireHistoryQuery { SchedulerName = "Remote", Reasons = [] });

        executions.Items.Should().BeEmpty();
        executions.TotalCount.Should().Be(0);
        misfires.Items.Should().BeEmpty();
        handler.Requests.Should().BeEmpty("an empty set matches nothing, which needs no host to say so");
    }

    [Test]
    public async Task AMisfireReasonOfTheCallersOwnNeedsAHostThatReadsIt()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.3.0.0"));
        handler.Respond(HttpStatusCode.OK, EmptyPage);

        Func<Task> act = async () => await Store().QueryMisfires(
            new MisfireHistoryQuery { SchedulerName = "Remote", Reasons = [MisfireReason.Missed] });

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*job and reason*",
            "a 4.3 host would answer the overlaps too");
    }

    [Test]
    public async Task TheReasonsAskedForAreSentByName()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.4.0.0"));
        handler.Respond(HttpStatusCode.OK, EmptyPage);

        await Store().QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = "Remote",
            Job = new JobKey("nightly", "reports"),
            Reasons = [MisfireReason.Vetoed]
        });

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/misfires?take=250&jobGroup=reports&jobName=nightly&reasons=Vetoed");
    }

    /// <summary>
    /// What a 4.4 row adds travels with it, and its metrics come back as the text the recorder wrote.
    /// </summary>
    [Test]
    public async Task ARunsResultSummaryMetricsAndOriginTravelWithIt()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "schedulerInstanceId": "node-a",
                  "jobGroup": "billing",
                  "jobName": "release-stale",
                  "triggerGroup": "DEFAULT",
                  "triggerName": "MT_1",
                  "firedAtUtc": "2026-09-01T12:00:00+00:00",
                  "duration": "00:00:00.2500000",
                  "succeeded": true,
                  "exceptionMessage": null,
                  "result": "Skipped",
                  "summary": "no stale reservations",
                  "metrics": {"scanned":1200,"released":0,"note":"café"},
                  "manual": true,
                  "fireInstanceId": "node-a-17"
                }
              ],
              "hasMore": false,
              "totalCount": 1
            }
            """);

        ExecutionHistoryEntry entry = (await Store().QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "Remote" }))
            .Items.Should().ContainSingle().Subject;

        entry.Result.Should().Be(JobRunResult.Skipped);
        entry.Summary.Should().Be("no stale reservations");
        entry.MetricsJson.Should().Be("""{"scanned":1200,"released":0,"note":"café"}""",
            "the object travels as an object, and is handed back as the text it was, escapes included");
        entry.Manual.Should().BeTrue();
        entry.FireInstanceId.Should().Be("node-a-17");
    }

    [Test]
    public async Task ARowFromAnOlderHostHasNoResult()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "schedulerInstanceId": "node-a",
                  "jobGroup": "billing",
                  "jobName": "release-stale",
                  "triggerGroup": "DEFAULT",
                  "triggerName": "nightly",
                  "firedAtUtc": "2026-09-01T12:00:00+00:00",
                  "duration": "00:00:00.2500000",
                  "succeeded": false,
                  "exceptionMessage": "boom"
                }
              ],
              "hasMore": false
            }
            """);

        ExecutionHistoryEntry entry = (await Store().QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "Remote" }))
            .Items.Should().ContainSingle().Subject;

        entry.Result.Should().BeNull("a 4.3 host sends no result");
        entry.EffectiveResult.Should().Be(JobRunResult.Failed, "which the row's success still answers for");
        entry.MetricsJson.Should().BeNull();
        entry.Manual.Should().BeFalse();
    }

    [Test]
    public async Task StatusesAreListedFromTheStatusRoute()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "job": { "name": "release-stale", "group": "billing" },
                  "lastFiredAtUtc": "2026-09-01T12:00:00+00:00",
                  "lastResult": "Failed",
                  "lastFailureMessage": "the upstream system is down",
                  "consecutiveFailures": 3,
                  "runCount": 40,
                  "failureCount": 5
                }
              ],
              "hasMore": true,
              "totalCount": 9
            }
            """);

        PagedResult<JobRunStatus> page = await Store().QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = "Remote",
            Failing = true,
            Take = 1,
            IncludeTotalCount = true
        });

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/job-status?take=1&includeTotalCount=true&failing=true");

        JobRunStatus status = page.Items.Should().ContainSingle().Subject;
        status.SchedulerName.Should().Be("Remote", "the route said it, and the status belongs to it");
        status.Job.Should().Be(new JobKey("release-stale", "billing"));
        status.LastResult.Should().Be(JobRunResult.Failed);
        status.ConsecutiveFailures.Should().Be(3);
        status.RunCount.Should().Be(40);
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(9);
    }

    [Test]
    public async Task OneStatusIsReadFromItsOwnRoute()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "job": { "name": "release-stale", "group": "billing" },
              "lastFiredAtUtc": "2026-09-01T12:00:00+00:00",
              "lastResult": "Skipped",
              "lastSucceededAtUtc": "2026-09-01T12:00:00+00:00"
            }
            """);

        JobRunStatus status = await Store().GetJobRunStatus("Remote", new JobKey("release-stale", "billing"));

        handler.LastRequestUri.Should().Be("http://localhost:8080/schedulers/Remote/history/job-status/billing/release-stale");
        status.Should().NotBeNull();
        status.LastResult.Should().Be(JobRunResult.Skipped);
        status.LastSucceededAtUtc.Should().Be(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task AJobWithNoRecordedRunHasNoStatus()
    {
        handler.Respond(HttpStatusCode.NotFound, """
            { "title": "Not Found", "status": 404, "detail": "No recorded run of job billing.release-stale" }
            """);

        (await Store().GetJobRunStatus("Remote", new JobKey("release-stale", "billing"))).Should().BeNull(
            "the host answered, and what it said is that it has recorded no run of the job");
    }

    [Test]
    public async Task NamedJobsAreFetchedAndPagedHere()
    {
        handler.Respond(HttpStatusCode.OK, """
            [
              { "job": { "name": "b", "group": "g" }, "lastFiredAtUtc": "2026-09-01T12:00:00+00:00", "lastResult": "Failed", "consecutiveFailures": 1 },
              { "job": { "name": "a", "group": "g" }, "lastFiredAtUtc": "2026-09-01T12:00:00+00:00", "lastResult": "Failed", "consecutiveFailures": 2 },
              { "job": { "name": "c", "group": "g" }, "lastFiredAtUtc": "2026-09-01T12:00:00+00:00", "lastResult": "Succeeded" }
            ]
            """);

        PagedResult<JobRunStatus> page = await Store().QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = "Remote",
            Jobs = [new JobKey("a", "g"), new JobKey("b", "g"), new JobKey("c", "g"), new JobKey("a", "g")],
            Failing = true,
            Take = 1
        });

        handler.Requests.Should().Equal(["POST http://localhost:8080/schedulers/Remote/history/job-status/fetch"]);
        handler.LastRequestBody.Should().Be("""{"jobs":[{"name":"a","group":"g"},{"name":"b","group":"g"},{"name":"c","group":"g"}]}""",
            "the keys travel in the body, each once");

        page.Items.Should().ContainSingle().Which.Job.Name.Should().Be("a",
            "the fetch answers every named job, so the failing filter and the page are applied here, by group and then name");
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(2);
    }

    [Test]
    public async Task MoreKeysThanOneFetchTakesAreSentInBatches()
    {
        handler.Respond(HttpStatusCode.OK, "[]");

        JobKey[] jobs = Enumerable.Range(0, 1001).Select(index => new JobKey("job" + index, "bulk")).ToArray();
        await Store().QueryJobRunStatuses(new JobRunStatusQuery { SchedulerName = "Remote", Jobs = jobs });

        handler.Requests.Should().HaveCount(2, "the host takes at most a thousand keys at once");
    }

    /// <summary>
    /// A host that predates the status routes answers them <c>404</c> without problem details.
    /// </summary>
    [Test]
    public async Task AHostThatPredatesTheStatusRoutesSaysItKeepsNoStatus()
    {
        handler.Respond(HttpStatusCode.NotFound, body: "");

        Func<Task> query = async () => await Store().QueryJobRunStatuses(new JobRunStatusQuery { SchedulerName = "Remote" });
        Func<Task> single = async () => await Store().GetJobRunStatus("Remote", new JobKey("nightly", "reports"));
        Func<Task> fetch = async () => await Store().QueryJobRunStatuses(
            new JobRunStatusQuery { SchedulerName = "Remote", Jobs = [new JobKey("nightly", "reports")] });

        await query.Should().ThrowAsync<NotSupportedException>().WithMessage("*'Remote'*older than 4.4*");
        await single.Should().ThrowAsync<NotSupportedException>().WithMessage("*older than 4.4*");
        await fetch.Should().ThrowAsync<NotSupportedException>().WithMessage("*older than 4.4*");
    }

    /// <summary>
    /// A 4.4 host whose history store keeps rows only answers <c>501</c>, and its detail is the store's own
    /// explanation.
    /// </summary>
    [Test]
    public async Task AHostWhoseStoreKeepsNoStatusSaysSo()
    {
        handler.Respond(HttpStatusCode.NotImplemented, """
            {
              "title": "Not Implemented",
              "status": 501,
              "detail": "AcmeHistoryStore keeps no per-job run status.",
              "Quartz-ExceptionType": "NotSupportedException"
            }
            """);

        Func<Task> act = async () => await Store().GetJobRunStatus("Remote", new JobKey("nightly", "reports"));

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("AcmeHistoryStore keeps no per-job run status.",
            "the host's store said why, and that is the sentence a page can show");
    }

    private const string DetailsPath = "/schedulers/Remote";

    private const string EmptyPage = """{ "items": [], "hasMore": false, "totalCount": 0 }""";

    private static string SchedulerDetails(string version) => $$"""
        {
          "schedulerInstanceId": "NON_CLUSTERED",
          "name": "Remote",
          "status": "Running",
          "threadPool": { "type": "Quartz.Impl.DefaultThreadPool", "size": 10 },
          "jobStore": { "type": "Quartz.Impl.RAMJobStore", "clustered": false, "persistent": false },
          "statistics": { "version": "{{version}}", "runningSince": null, "jobsExecuted": 0, "localExecutingJobs": 0 }
        }
        """;

    private HttpExecutionHistoryStore Store() => new("Remote", httpClient);

    /// <summary>
    /// Answers a request by its path when a route was given for it, and with the default answer otherwise,
    /// recording every request it was sent.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> byPath = new(StringComparer.Ordinal);
        private HttpStatusCode statusCode = HttpStatusCode.OK;
        private string body = "";

        public string LastRequestUri { get; private set; }

        public string LastRequestBody { get; private set; }

        public List<string> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body)
        {
            statusCode = status;
            this.body = body;
        }

        public void RespondTo(string path, HttpStatusCode status, string body)
        {
            byPath[path] = (status, body);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add($"{request.Method} {LastRequestUri}");

            (HttpStatusCode status, string content) = byPath.TryGetValue(request.RequestUri!.AbsolutePath, out (HttpStatusCode Status, string Body) routed)
                ? routed
                : (statusCode, body);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}
