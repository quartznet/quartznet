using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging.Testing;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// What a client makes of an answer from a host newer than itself: an enum member it has no name for, and a
/// member of a body it has never heard of.
/// </summary>
/// <remarks>
/// <para>
/// Every name below is one no version of Quartz has, standing for the one a later release will add. Until
/// 4.4 each of them failed the whole call, so every release hid its new names on the server instead. A 4.4
/// client reads the name as the member its contract keeps for a value it cannot name, as absent, or — where
/// the item cannot be built without it — leaves that one item out of its listing and says so in the log.
/// </para>
/// <para>
/// The decisions are per enum, and these tests are the table: one test per place an enum is read.
/// </para>
/// </remarks>
public class NewerHostNamesTest
{
    private StubHandler handler;
    private HttpClient httpClient;
    private FakeLogger logger;

    [SetUp]
    public void SetUp()
    {
        handler = new StubHandler();
        httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        logger = new FakeLogger();
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
    public async Task ATriggerInAStateThisClientDoesNotKnowIsLeftOutOfTheListing()
    {
        handler.Respond(Page(Trigger("parked", "Hibernating"), Trigger("nightly", "Normal")));

        PagedResult<TriggerHeader> page = await Scheduler().QueryTriggers(new TriggerQuery { Take = 10, IncludeTotalCount = true });

        page.Items.Should().ContainSingle("the trigger in a state this client cannot name is the one left out")
            .Which.Key.Should().Be(new TriggerKey("nightly", "reports"));
        page.TotalCount.Should().Be(2, "the host counted the trigger it sent, and the count is the host's");

        LeftOut().Should().ContainSingle()
            .Which.Message.Should().Be(
                "Left a trigger out of a listing from scheduler Remote: its TriggerState is Hibernating, which this "
                + "version of Quartz.HttpClient does not know. Upgrade the client to list it.");
    }

    /// <summary>
    /// A dashboard asks the same page every few seconds, and the answer does not change until somebody
    /// upgrades the client.
    /// </summary>
    [Test]
    public async Task TheSameUnknownNameIsLoggedOncePerClient()
    {
        handler.Respond(Page(Trigger("parked", "Hibernating"), Trigger("frozen", "Hibernating")));
        HttpScheduler scheduler = Scheduler();

        await scheduler.QueryTriggers(new TriggerQuery { Take = 10 });
        await scheduler.QueryTriggers(new TriggerQuery { Take = 10 });

        LeftOut().Should().ContainSingle("two items and two pages carrying one name are one thing to tell an operator");
    }

    [Test]
    public async Task AnOverlapPolicyThisClientDoesNotKnowReadsAsDefault()
    {
        handler.Respond(Page(Trigger("nightly", "Normal", """, "overlapPolicy": "Staggered" """)));

        PagedResult<TriggerHeader> page = await Scheduler().QueryTriggers(new TriggerQuery { Take = 10 });

        page.Items.Should().ContainSingle().Which.OverlapPolicy.Should().Be(OverlapPolicy.Default,
            "a trigger body already reads an overlap policy it does not know as Default, and a listing says the same thing");
        ReadAs().Should().ContainSingle().Which.Message.Should().Contain("OverlapPolicy Staggered").And.Contain("as Default");
    }

    [Test]
    public async Task AContinuationConditionThisClientDoesNotKnowReadsAsAbsent()
    {
        handler.Respond(Page(Trigger("report", "Awaiting", """
            , "continuesAfterTriggerName": "extract", "continuesAfterTriggerGroup": "reports", "continuationCondition": "OnTimeout"
            """)));

        PagedResult<TriggerHeader> page = await Scheduler().QueryTriggers(new TriggerQuery { Take = 10 });

        TriggerHeader header = page.Items.Should().ContainSingle().Subject;
        header.ContinuesAfter.Should().Be(new TriggerKey("extract", "reports"), "only the condition was unreadable");
        header.ContinuationCondition.Should().BeNull("the member may be absent, and absent is all this client can say of it");
        ReadAs().Should().ContainSingle().Which.Message.Should().Contain("as null");
    }

    [Test]
    public async Task AFiringInAStateThisClientDoesNotKnowIsLeftOutOfTheListing()
    {
        handler.Respond(Page(Firing("fire-1", "Suspended"), Firing("fire-2", "Executing")));

        PagedResult<FireInstance> page = await Scheduler().QueryFireInstances(new FireInstanceQuery { Take = 10 });

        page.Items.Should().ContainSingle().Which.FireInstanceId.Should().Be("fire-2");
        LeftOut().Should().ContainSingle().Which.Message.Should().Contain("Left a firing out").And.Contain("FireInstanceState is Suspended");
    }

    [Test]
    public async Task ANodeInAStateThisClientDoesNotKnowIsLeftOutOfTheListing()
    {
        handler.Respond("""
            [
              { "instanceId": "node-a", "state": "Quarantined", "isCurrentNode": false },
              { "instanceId": "node-b", "state": "Alive", "isCurrentNode": true }
            ]
            """);

        List<ClusterNode> nodes = await Scheduler().QueryClusterNodes();

        nodes.Should().ContainSingle().Which.InstanceId.Should().Be("node-b");
        LeftOut().Should().ContainSingle().Which.Message.Should().Contain("Left a cluster node out");
    }

    [Test]
    public async Task ASchedulerStatusThisClientDoesNotKnowReadsAsUnknown()
    {
        handler.Respond(SchedulerDetails("Draining"));

        SchedulerStatus status = await Scheduler().GetStatus();

        status.Should().Be(SchedulerStatus.Unknown, "the contract keeps Unknown for a state a reader cannot determine");
        ReadAs().Should().ContainSingle().Which.Message.Should().Be(
            "Read the SchedulerStatus Draining from scheduler Remote as Unknown: this version of Quartz.HttpClient does not know the name.");
    }

    /// <summary>
    /// A single read has no listing to leave the item out of, and no member to read the name as.
    /// </summary>
    [Test]
    public async Task OneTriggersStateThisClientDoesNotKnowFailsTheReadAndSaysWhy()
    {
        handler.Respond("""{ "state": "Hibernating" }""");

        Func<Task> act = async () => await Scheduler().GetTriggerState(new TriggerKey("parked", "reports"));

        (await act.Should().ThrowAsync<JsonException>())
            .WithMessage("The host sent 'Hibernating' as a TriggerState, which this version of Quartz.HttpClient does not know.*");
    }

    /// <summary>
    /// The limits are read to be written back: a limit dropped, or read as per-node, would be lost or changed
    /// by the next <c>SetExecutionLimits</c> built from what was read.
    /// </summary>
    [Test]
    public async Task ALimitScopeThisClientDoesNotKnowFailsTheReadRatherThanLosingTheLimit()
    {
        handler.Respond("""{ "limits": { "reports": { "maxConcurrent": 2, "scope": "Region" } } }""");

        Func<Task> act = async () => await Scheduler().GetExecutionLimits();

        (await act.Should().ThrowAsync<JsonException>()).WithMessage("*'Region' as a ExecutionLimitScope*");
    }

    [Test]
    public async Task AScheduleOutcomeThisClientDoesNotKnowReadsAsCreated()
    {
        handler.Respond("""{ "firstFireTimeUtc": "2026-10-05T12:00:00+00:00", "outcome": "Merged" }""");

        ITrigger trigger = TriggerBuilder.Create().WithIdentity("nightly", "reports").ForJob("nightly", "reports").StartNow().Build();
        ScheduleTriggerResult result = await Scheduler().ScheduleTrigger(trigger, TriggerConflict.KeepEarlier);

        result.Outcome.Should().Be(ScheduleOutcome.Created,
            "an outcome that reads as absent is what a host before 4.3 answers, and the client already reads that as Created");
    }

    /// <summary>
    /// Only a name this client does not know leaves an item out. A body that is wrong is still a failure.
    /// </summary>
    [Test]
    public async Task AMalformedItemStillFailsTheListing()
    {
        handler.Respond(Page(Trigger("nightly", "true")));

        Func<Task> act = async () => await Scheduler().QueryTriggers(new TriggerQuery { Take = 10 });

        (await act.Should().ThrowAsync<JsonException>()).Which.Should().NotBeOfType<UnknownWireNameException>();
        logger.Collector.Count.Should().Be(0, "nothing was left out: the listing failed");
    }

    [Test]
    public async Task AMemberThisClientDoesNotKnowIsSkipped()
    {
        handler.Respond(Page(Trigger("nightly", "Normal", """, "parkedUntilUtc": "2027-01-01T00:00:00+00:00", "lease": { "owner": "node-a" } """)));

        PagedResult<TriggerHeader> page = await Scheduler().QueryTriggers(new TriggerQuery { Take = 10 });

        page.Items.Should().ContainSingle().Which.State.Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// The caller's options are the caller's, but one that disallows unmapped members would otherwise fail on
    /// every member a newer host adds.
    /// </summary>
    [Test]
    public async Task AMemberThisClientDoesNotKnowIsSkippedWhateverTheCallersOptionsSay()
    {
        handler.Respond(Page(Trigger("nightly", "Normal", """, "parkedUntilUtc": "2027-01-01T00:00:00+00:00" """)));
        JsonSerializerOptions strict = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

        HttpScheduler scheduler = new("Remote", httpClient, strict);
        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery { Take = 10 });

        page.Items.Should().ContainSingle();
        strict.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Disallow, "the caller's own instance is left as it was");
    }

    [Test]
    public async Task AnExecutionResultThisClientDoesNotKnowReadsAsARowWithoutOne()
    {
        handler.Respond(Page(Execution("TimedOut", succeeded: false)));

        PagedResult<ExecutionHistoryEntry> page = await History().QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "Remote" });

        ExecutionHistoryEntry entry = page.Items.Should().ContainSingle().Subject;
        entry.Result.Should().BeNull("a row with no result this client can name is read as one written before 4.4");
        entry.EffectiveResult.Should().Be(JobRunResult.Failed, "which falls back on the row's own Succeeded");
    }

    [Test]
    public async Task AMisfireReasonThisClientDoesNotKnowIsLeftOutOfTheListing()
    {
        handler.Respond(Page(Misfire("Throttled"), Misfire("Overlap")));

        PagedResult<MisfireHistoryEntry> page = await History().QueryMisfires(new MisfireHistoryQuery { SchedulerName = "Remote" });

        page.Items.Should().ContainSingle().Which.Reason.Should().Be(MisfireReason.Overlap,
            "reading a reason this client cannot name as Missed would count it as a missed firing, which it may not be");
        LeftOut().Should().ContainSingle().Which.Message.Should().Contain("Left a misfire out").And.Contain("MisfireReason is Throttled");
    }

    [Test]
    public async Task AJobRunStatusWhoseResultThisClientDoesNotKnowIsLeftOutOfTheListing()
    {
        handler.Respond(Page(Status("a", "TimedOut"), Status("b", "Failed")));

        PagedResult<JobRunStatus> page = await History().QueryJobRunStatuses(new JobRunStatusQuery { SchedulerName = "Remote" });

        page.Items.Should().ContainSingle().Which.Job.Should().Be(new JobKey("b", "g"));
        LeftOut().Should().ContainSingle().Which.Message.Should().Contain("Left a job run status out");
    }

    [Test]
    public async Task AFetchedJobRunStatusWhoseResultThisClientDoesNotKnowIsLeftOut()
    {
        handler.Respond($"[{Status("a", "TimedOut")}, {Status("b", "Skipped")}]");

        PagedResult<JobRunStatus> page = await History().QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = "Remote",
            Jobs = [new JobKey("a", "g"), new JobKey("b", "g")]
        });

        page.Items.Should().ContainSingle().Which.LastResult.Should().Be(JobRunResult.Skipped);
    }

    [Test]
    public async Task OneJobRunStatusWhoseResultThisClientDoesNotKnowFailsTheRead()
    {
        handler.Respond(Status("a", "TimedOut"));

        Func<Task> act = async () => await History().GetJobRunStatus("Remote", new JobKey("a", "g"));

        await act.Should().ThrowAsync<JsonException>().WithMessage("*'TimedOut' as a JobRunResult*");
    }

    /// <summary>
    /// The client's tolerance is the client's: the server reads the same contract strictly, and a request
    /// naming a value it does not know is still a <c>400</c>.
    /// </summary>
    [Test]
    public void EveryEnumTheWireCarriesIsReadTolerantlyByTheClientAndStrictlyByTheServer()
    {
        JsonSerializerOptions server = new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
        JsonSerializerOptions client = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureClientWireFormat(new SystemTextJsonSerializerRegistry(), new UnknownWireNames(logger, "Remote"));

        List<Type> enums = server.Converters
            .Select(x => x.GetType())
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>))
            .Select(x => x.GetGenericArguments()[0])
            .ToList();

        enums.Should().NotBeEmpty("the server names its enums one converter each, and this test reads that list");

        foreach (Type enumType in enums)
        {
            client.GetConverter(enumType).GetType().Name.Should().Be("Required",
                $"the client reads {enumType.Name} through its tolerant converter rather than the server's");
            client.GetConverter(typeof(Nullable<>).MakeGenericType(enumType)).GetType().Name.Should().Be("Optional",
                $"a nullable {enumType.Name} is read through the tolerant converter too, so an unknown name can be null");
        }

        Action strict = () => JsonSerializer.Deserialize<TriggerStateDto>("""{ "state": "Hibernating" }""", server);
        strict.Should().Throw<JsonException>().Which.Should().NotBeOfType<UnknownWireNameException>();
    }

    /// <summary>
    /// What the client writes is unchanged: the tolerant converter writes through the server's.
    /// </summary>
    [Test]
    public void TheClientWritesEnumsAsTheServerDoes()
    {
        JsonSerializerOptions server = new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
        JsonSerializerOptions client = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureClientWireFormat(new SystemTextJsonSerializerRegistry(), new UnknownWireNames(logger, "Remote"));

        TriggerHeaderDto header = new("nightly", "reports", "nightly", "reports", null, "Cron", TriggerState.Paused,
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), null, null, null, null, 5, null, null, 0,
            ContinuationCondition: ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation,
            OverlapPolicy: OverlapPolicy.Skip);
        TriggerHeaderDto[] listing = [header];
        ScheduleJobResponse response = new(header.StartTimeUtc) { Outcome = ScheduleOutcome.Kept };

        JsonSerializer.Serialize(listing, client).Should().Be(JsonSerializer.Serialize(listing, server));
        JsonSerializer.Serialize(response, client).Should().Be(JsonSerializer.Serialize(response, server));
        JsonSerializer.Serialize(new ScheduleJobResponse(header.StartTimeUtc), client)
            .Should().Be(JsonSerializer.Serialize(new ScheduleJobResponse(header.StartTimeUtc), server));
    }

    /// <summary>
    /// A host that answers a stream of distinct names costs the client nothing that grows.
    /// </summary>
    [Test]
    public void TheNamesReportedAreBounded()
    {
        UnknownWireNames names = new(logger, "Remote");

        for (int i = 0; i < UnknownWireNames.MaxRemembered + 50; i++)
        {
            names.EventKindSkipped($"Kind{i}");
        }

        logger.Collector.Count.Should().Be(UnknownWireNames.MaxRemembered);
    }

    private IReadOnlyList<FakeLogRecord> LeftOut() => logger.Collector.GetSnapshot().Where(x => x.Id.Id == 9202).ToList();

    private IReadOnlyList<FakeLogRecord> ReadAs() => logger.Collector.GetSnapshot().Where(x => x.Id.Id == 9203).ToList();

    private HttpScheduler Scheduler() => new("Remote", httpClient, jsonSerializerOptions: null, serializerRegistry: null, logger);

    private HttpExecutionHistoryStore History() => new("Remote", httpClient, jsonSerializerOptions: null, logger);

    private static string Page(params string[] items) => $$"""{ "items": [{{string.Join(", ", items)}}], "hasMore": false, "totalCount": {{items.Length}} }""";

    /// <param name="state">The state, spelled as the JSON value: a name is quoted here, anything else is not.</param>
    private static string Trigger(string name, string state, string more = "")
    {
        string stateValue = state is "true" ? state : $"\"{state}\"";
        return $$"""
            {
              "name": "{{name}}", "group": "reports", "jobName": "nightly", "jobGroup": "reports",
              "triggerType": "Cron", "state": {{stateValue}}, "startTimeUtc": "2026-10-05T00:00:00+00:00",
              "priority": 5, "retryAttempt": 0 {{more}}
            }
            """;
    }

    private static string Firing(string id, string state) => $$"""
        {
          "fireInstanceId": "{{id}}", "triggerName": "nightly", "triggerGroup": "reports",
          "schedulerInstanceId": "node-a", "state": "{{state}}", "fireTimeUtc": "2026-10-05T00:00:00+00:00"
        }
        """;

    private static string Execution(string result, bool succeeded) => $$"""
        {
          "schedulerInstanceId": "node-a", "jobGroup": "reports", "jobName": "nightly",
          "triggerGroup": "reports", "triggerName": "nightly", "firedAtUtc": "2026-10-05T00:00:00+00:00",
          "duration": "00:00:01", "succeeded": {{(succeeded ? "true" : "false")}}, "result": "{{result}}"
        }
        """;

    private static string Misfire(string reason) => $$"""
        {
          "schedulerInstanceId": "node-a", "triggerGroup": "reports", "triggerName": "nightly",
          "misfiredAtUtc": "2026-10-05T00:00:00+00:00", "reason": "{{reason}}"
        }
        """;

    private static string Status(string job, string lastResult) => $$"""
        { "job": { "name": "{{job}}", "group": "g" }, "lastFiredAtUtc": "2026-10-05T00:00:00+00:00", "lastResult": "{{lastResult}}" }
        """;

    private static string SchedulerDetails(string status) => $$"""
        {
          "schedulerInstanceId": "NON_CLUSTERED",
          "name": "Remote",
          "status": "{{status}}",
          "threadPool": { "type": "Quartz.Impl.DefaultThreadPool", "size": 10 },
          "jobStore": { "type": "Quartz.Impl.RAMJobStore", "clustered": false, "persistent": false },
          "statistics": { "version": "4.9.0", "runningSince": null, "jobsExecuted": 0, "localExecutingJobs": 0 }
        }
        """;

    /// <summary>
    /// Answers every request with the one body the test gave it.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private string body = "";

        public void Respond(string body) => this.body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
