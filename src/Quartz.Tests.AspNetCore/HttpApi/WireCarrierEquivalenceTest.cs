using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Quartz.AspNetCore;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Holds the readers of the operation catalogue's route table to one another: the endpoints the HTTP
/// API maps, the requests <see cref="HttpScheduler" /> sends, and the catalogue's own routing, which a
/// carrier that is not ASP.NET Core dispatches with.
/// </summary>
/// <remarks>
/// <para>
/// Two hand-maintained mappings of one contract drift, and nothing notices — the HTTP-backed dashboard
/// client 4.0 deleted did it twice. The catalogue makes the mapping one table; this is what keeps every
/// reader on it. A member of <see cref="IScheduler" /> that <see cref="HttpScheduler" /> implements without
/// a row below fails, and so does a route no client sends that nobody has said is for someone else.
/// </para>
/// <para>
/// Every request goes to a live host, so "the server serves it" means ASP.NET Core routed the path the
/// client built to the endpoint the catalogue names, bound what it carried, and answered with a success.
/// </para>
/// </remarks>
public sealed class WireCarrierEquivalenceTest
{
    /// <summary>
    /// The names the sweep puts in paths. They carry spaces, so the catalogue's reading of a path is held to
    /// ASP.NET Core's on values that are not plain words.
    /// </summary>
    private static readonly Names Plain = new(
        new JobKey("nightly export", "reports"),
        new TriggerKey("at midnight", "reports"),
        Calendar: "holidays",
        Group: "night shift",
        FireInstanceId: "fire-1");

    private static readonly PauseDetails Why = new() { Reason = "maintenance window", RequestedBy = "ops@example.com" };

    private WebApplicationFactory<Program> root = null!;
    private WebApplicationFactory<Program> host = null!;
    private HttpClient httpClient = null!;
    private ServedRequests served = null!;
    private RecordingTransport transport = null!;
    private HttpScheduler scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        TestContentRoot.Apply();

        served = new ServedRequests();
        root = new WebApplicationFactory<Program>();
        host = root.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddSingleton<IStartupFilter>(served)));

        ISchedulerRepository repository = host.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(AnsweringScheduler());

        httpClient = host.CreateClient();
        transport = new RecordingTransport(new HttpWireTransport(httpClient));
        scheduler = new HttpScheduler(TestData.SchedulerName, transport, jsonSerializerOptions: null, serializerRegistry: null);
    }

    [TearDown]
    public async Task TearDown()
    {
        await scheduler.DisposeAsync();
        httpClient.Dispose();
        await host.DisposeAsync();
        await root.DisposeAsync();
    }

    /// <summary>
    /// The server maps exactly the catalogue's routes, each at its template, with its method and under its
    /// name.
    /// </summary>
    /// <remarks>
    /// Under an API path of its own, so the template is seen to be relative to wherever the API is mapped.
    /// The name is the endpoint's OpenAPI operation id and the word the mutation audit logs, so it is as
    /// much the contract as the path is.
    /// </remarks>
    [Test]
    public async Task EveryRouteOfTheCatalogueIsMappedAsItSaysAndNothingElseIs()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartz();
        builder.Services.AddQuartzHttpApi();
        await using WebApplication app = builder.Build();
        app.MapQuartzHttpApi("/quartz-api").AllowAnonymous();

        List<string> mapped = ((IEndpointRouteBuilder) app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<QuartzEndpointMarker>() is not null)
            .Select(endpoint => Describe(
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []),
                endpoint.RoutePattern.RawText))
            .ToList();

        List<string> catalogued = SchedulerRoutes.All
            .Select(route => Describe(route.Name, route.Method, "/quartz-api/" + route.Template))
            .ToList();

        mapped.Should().BeEquivalentTo(catalogued,
            "every endpoint the API maps is a route of the catalogue, at the catalogue's template, with its method and under its name");

        static string Describe(string? name, string methods, string? pattern) => $"{name}: {methods} {pattern}";
    }

    /// <summary>
    /// Every <see cref="IScheduler" /> member <see cref="HttpScheduler" /> answers over the wire sends a
    /// request built from a catalogue route, which the server routes to the endpoint of that route's name
    /// and the catalogue routes back to the same route with the same values.
    /// </summary>
    [Test]
    public async Task EveryMemberHttpSchedulerImplementsSendsACatalogueRouteTheServerServesUnderItsName()
    {
        Dictionary<string, Row> rows = OverTheWire(Plain);

        List<string> implemented = ImplementedMembers()
            .Select(Signature)
            .Where(signature => !NotOverTheWire.ContainsKey(signature))
            .ToList();

        implemented.Should().BeEquivalentTo(rows.Keys,
            "every IScheduler member HttpScheduler implements needs a row, so a member added without one fails here rather than going unchecked");

        HashSet<WireRoute> sent = [];
        using (new AssertionScope())
        {
            foreach ((string signature, Row row) in rows)
            {
                transport.Sent.Clear();
                served.Clear();

                // An assertion rather than an abort, so one member answered wrongly still lets the rest of
                // the table be checked.
                Func<Task> call = () => row.Invoke(scheduler);
                await call.Should().NotThrowAsync($"{signature} is answered with a success by the endpoint its route names");

                transport.Sent.Select(request => request.Route).Should().Equal(row.Routes,
                    $"{signature} is answered by the catalogue's {string.Join(" then ", row.Routes.Select(route => route.Name))}");
                served.Requests.Should().HaveSameCount(transport.Sent, $"every request {signature} sent reached the host");

                for (int i = 0; i < Math.Min(transport.Sent.Count, served.Requests.Count); i++)
                {
                    WireRequest request = transport.Sent[i];
                    Served answer = served.Requests[i];
                    sent.Add(request.Route);

                    answer.EndpointName.Should().Be(request.Route.Name,
                        $"{signature} sent {request.Route}, which the server has to route to the endpoint the catalogue names");
                    answer.Method.Should().Be(request.Route.Method, $"{signature} sends the route's own method");
                    answer.Status.Should().BeInRange(200, 299,
                        $"the endpoint {request.Route.Name} accepts what {signature} sends and answers it");

                    (WireRoute Route, Dictionary<string, string> Values)? match = SchedulerRoutes.Match(request.Route.Method, request.Path);
                    match.Should().NotBeNull($"the catalogue routes {request.Route.Method} {request.Path} as the server does");
                    if (match is { } matched)
                    {
                        matched.Route.Should().BeSameAs(request.Route,
                            $"the catalogue routes the path {signature} built back to the route it was built from");
                        matched.Values.Should().BeEquivalentTo(answer.RouteValues,
                            $"the catalogue reads the values out of the path {signature} built exactly as ASP.NET Core does");
                    }
                }
            }
        }

        SchedulerRoutes.All.Except(sent).Select(route => route.Name).Should().BeEquivalentTo(ServedToOtherClients.Keys,
            "a route HttpScheduler never sends is one that another client reads, and says which");
    }

    /// <summary>
    /// The same sweep with a character in every name and group that means something in a URL (#3917).
    /// Escaped, each arrives as written. A <c>/</c> cannot: every member that would put it in a path refuses
    /// before sending anything, and every member that carries its keys in the body still works.
    /// </summary>
    /// <remarks>
    /// The <c>%</c> is followed by two hex digits, so it reads as an escape sequence unless it is escaped
    /// itself. A <c>%</c> followed by anything else was already sent intact.
    /// </remarks>
    [TestCase("?")]
    [TestCase("#")]
    [TestCase("%41")]
    [TestCase("&")]
    [TestCase(" ")]
    [TestCase("/")]
    public async Task ANameWithACharacterThatMeansSomethingInAUrlReachesTheServerAsWritten(string character)
    {
        Names names = new(
            new JobKey($"nightly{character}export", $"re{character}ports"),
            new TriggerKey($"at{character}midnight", $"re{character}ports"),
            Calendar: $"holi{character}days",
            Group: $"re{character}ports",
            FireInstanceId: $"fire{character}1");

        Dictionary<string, string> written = names.InPaths();

        using (new AssertionScope())
        {
            foreach ((string signature, Row row) in OverTheWire(names))
            {
                transport.Sent.Clear();
                served.Clear();

                Func<Task> call = () => row.Invoke(scheduler);

                if (row.Routes.Any(route => route.Parameters.Any(parameter => written[parameter].Contains('/', StringComparison.Ordinal))))
                {
                    await call.Should().ThrowAsync<ArgumentException>().WithMessage("*cannot be sent in the path of*'/'*",
                        $"{signature} would put a '/' in a path, which the server would read as another name");
                    transport.Sent.Should().BeEmpty($"{signature} refuses before it sends anything");
                    continue;
                }

                await call.Should().NotThrowAsync($"{signature} is answered with a success by the endpoint its route names");
                served.Requests.Should().HaveSameCount(transport.Sent, $"every request {signature} sent reached the host");

                for (int i = 0; i < Math.Min(transport.Sent.Count, served.Requests.Count); i++)
                {
                    WireRequest request = transport.Sent[i];
                    Served answer = served.Requests[i];

                    answer.EndpointName.Should().Be(request.Route.Name, $"{signature} reaches the endpoint the catalogue names");
                    answer.Status.Should().BeInRange(200, 299, $"the endpoint {request.Route.Name} answers what {signature} sends");

                    foreach (string parameter in request.Route.Parameters)
                    {
                        answer.RouteValues.Should().Contain(parameter, written[parameter],
                            $"{signature} put {parameter} in the path escaped, and ASP.NET Core read it back as written");
                    }

                    SchedulerRoutes.Match(request.Route.Method, request.Path)?.Values.Should().BeEquivalentTo(answer.RouteValues,
                        $"the catalogue reads the escaped values out of the path {signature} built exactly as ASP.NET Core does");
                }
            }
        }
    }

    /// <summary>
    /// Why a <c>/</c> is refused rather than escaped: ASP.NET Core keeps <c>%2F</c> escaped and routing does
    /// not unescape a value, so the endpoint reads a name that was never written.
    /// </summary>
    [Test]
    public async Task AnEscapedSlashReachesTheEndpointStillEscaped()
    {
        using HttpResponseMessage response = await httpClient.GetAsync($"schedulers/{TestData.SchedulerName}/jobs/re%2Fports/nightly/exists");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Requests.Should().ContainSingle().Which.RouteValues.Should().Contain("jobGroup", "re%2Fports",
            "a name spelled 're%2Fports' arrives the same way, so the server cannot tell the two apart");
    }

    /// <summary>
    /// A matcher's value travels in the query string, escaped, so a character that ends a parameter, a query
    /// or a value there reaches the scheduler as written. A <c>+</c> would otherwise arrive as a space, and
    /// a <c>%</c> before hex digits as the character they encode.
    /// </summary>
    [TestCase("&")]
    [TestCase("#")]
    [TestCase("+")]
    [TestCase("%41")]
    [TestCase(" ")]
    [TestCase("=")]
    public async Task AMatcherValueWithACharacterThatMeansSomethingInAQueryReachesTheSchedulerAsWritten(string character)
    {
        string value = $"night{character}shift";
        IScheduler answering = host.Services.GetRequiredService<ISchedulerRepository>().Lookup(TestData.SchedulerName)!;

        await scheduler.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals(value));
        await scheduler.QueryTriggerGroups(new TriggerGroupQuery { Name = NameMatcher.NameStartsWith(value) });

        A.CallTo(() => answering.PauseJobGroups(
                A<GroupMatcher<JobKey>>.That.Matches(matcher => matcher.CompareToValue == value), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => answering.QueryTriggerGroups(
                A<TriggerGroupQuery>.That.Matches(query => query.Name!.CompareToValue == value), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The members the table does not send: none of them makes a request, and the two that cannot be
    /// honoured over a wire say so.
    /// </summary>
    [Test]
    public void TheMembersThatAreNotOverTheWireSendNothing()
    {
        using (new AssertionScope())
        {
            scheduler.SchedulerName.Should().Be(TestData.SchedulerName);
            scheduler.TimeProvider.Should().BeSameAs(TimeProvider.System);

            Action context = () => _ = scheduler.Context;
            context.Should().Throw<NotSupportedException>();

            Action listenerManager = () => _ = scheduler.ListenerManager;
            listenerManager.Should().Throw<NotSupportedException>();
        }

        transport.Sent.Should().BeEmpty("these members answer from what the client holds, or refuse, and never ask the target");
    }

    /// <summary>
    /// A listing the catalogue refuses is answered exactly as a body the endpoint refuses: a <c>400</c>
    /// with the same problem-details members, under the exception name the wire has always used.
    /// </summary>
    /// <remarks>
    /// The catalogue cannot raise ASP.NET Core's <c>BadHttpRequestException</c> and raises its own; a client
    /// matching on <c>Quartz-ExceptionType</c> must not be able to tell which layer said no.
    /// </remarks>
    [Test]
    public async Task TheCatalogueRefusesAMalformedListingAsTheEndpointRefusesAMalformedBody()
    {
        using HttpResponseMessage fromCatalogue = await httpClient.GetAsync($"schedulers/{TestData.SchedulerName}/jobs?take=not-a-number");
        using StringContent invalidJob = new("""{"replace":true}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage fromEndpoint = await httpClient.PostAsync($"schedulers/{TestData.SchedulerName}/jobs", invalidJob);

        using JsonDocument catalogueBody = JsonDocument.Parse(await fromCatalogue.Content.ReadAsStringAsync());
        using JsonDocument endpointBody = JsonDocument.Parse(await fromEndpoint.Content.ReadAsStringAsync());

        fromCatalogue.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        fromEndpoint.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        Members(catalogueBody).Should().Equal(Members(endpointBody), "the two refusals are one shape");
        catalogueBody.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString()
            .Should().Be("BadHttpRequestException", "that is what the wire has always named a request the API refused");
        catalogueBody.RootElement.GetProperty("detail").GetString()
            .Should().Contain("take must be a number", "the refusal says what to fix");

        static List<string> Members(JsonDocument document) => document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A member's call, and the routes it is answered by, in the order it sends them.
    /// </summary>
    private sealed record Row(WireRoute[] Routes, Func<IScheduler, Task> Invoke);

    /// <summary>
    /// The names a row puts in paths: a job, a trigger, a calendar, the group the two group-paused reads
    /// name, and a fire instance.
    /// </summary>
    private sealed record Names(JobKey Job, TriggerKey Trigger, string Calendar, string Group, string FireInstanceId)
    {
        /// <summary>
        /// Each route parameter a row fills, with the name written into it. The group-paused reads fill a
        /// group parameter with <see cref="Group" />, so this holds for them only when it is the job's and the
        /// trigger's group as well.
        /// </summary>
        public Dictionary<string, string> InPaths() => new(StringComparer.Ordinal)
        {
            ["schedulerName"] = TestData.SchedulerName,
            ["jobGroup"] = Job.Group,
            ["jobName"] = Job.Name,
            ["triggerGroup"] = Trigger.Group,
            ["triggerName"] = Trigger.Name,
            ["calendarName"] = Calendar,
            ["fireInstanceId"] = FireInstanceId,
        };
    }

    /// <summary>
    /// One row per <see cref="IScheduler" /> member <see cref="HttpScheduler" /> implements and answers over
    /// the wire, keyed by its signature. A row that has two ways to send — a body or none, a limit or its
    /// clearing — sends both.
    /// </summary>
    /// <remarks>
    /// Every name a row puts in a path comes from <paramref name="names" />, so the same rows can be sent
    /// with names that are not plain words.
    /// </remarks>
    private static Dictionary<string, Row> OverTheWire(Names names) => new(StringComparer.Ordinal)
    {
        ["get_SchedulerInstanceId()"] = new([SchedulerRoutes.GetSchedulerDetails], s => Read(s.SchedulerInstanceId)),
        ["GetSchedulerInstanceId(CancellationToken)"] = new([SchedulerRoutes.GetSchedulerDetails], s => s.GetSchedulerInstanceId().AsTask()),
        ["get_Status()"] = new([SchedulerRoutes.GetSchedulerDetails], s => Read(s.Status)),
        ["GetStatus(CancellationToken)"] = new([SchedulerRoutes.GetSchedulerDetails], s => s.GetStatus().AsTask()),
        ["GetMetadata(CancellationToken)"] = new([SchedulerRoutes.GetSchedulerDetails], s => s.GetMetadata().AsTask()),
        ["QueryFireInstances(FireInstanceQuery, CancellationToken)"] = new([SchedulerRoutes.QueryFireInstances], s => s.QueryFireInstances(new FireInstanceQuery { State = null }).AsTask()),
        ["QueryClusterNodes(CancellationToken)"] = new([SchedulerRoutes.GetClusterNodes], s => s.QueryClusterNodes().AsTask()),

        ["Start(CancellationToken)"] = new([SchedulerRoutes.Start], s => s.Start().AsTask()),
        ["StartDelayed(TimeSpan, CancellationToken)"] = new([SchedulerRoutes.Start], s => s.StartDelayed(TimeSpan.FromSeconds(30)).AsTask()),
        ["Standby(CancellationToken)"] = new([SchedulerRoutes.Standby], s => s.Standby().AsTask()),
        ["Shutdown(Boolean, CancellationToken)"] = new([SchedulerRoutes.Shutdown], s => s.Shutdown(waitForJobsToComplete: true).AsTask()),
        ["Clear(CancellationToken)"] = new([SchedulerRoutes.Clear], s => s.Clear().AsTask()),
        ["PauseAll(CancellationToken)"] = new([SchedulerRoutes.PauseAll], s => s.PauseAll().AsTask()),
        ["PauseAllWith(PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseAll], s => s.PauseAllWith(Why).AsTask()),
        ["ResumeAll(CancellationToken)"] = new([SchedulerRoutes.ResumeAll], s => s.ResumeAll().AsTask()),
        ["SetExecutionLimits(ExecutionLimits, CancellationToken)"] = new([SchedulerRoutes.SetExecutionLimits, SchedulerRoutes.ClearExecutionLimits], async s =>
        {
            await s.SetExecutionLimits(ExecutionLimitsBuilder.Create().ForGroup("imports", 2).Build());
            await s.SetExecutionLimits(null);
        }),
        ["GetExecutionLimits(CancellationToken)"] = new([SchedulerRoutes.GetExecutionLimits], s => s.GetExecutionLimits().AsTask()),

        ["ScheduleJob(IJobDetail, ITrigger, ScheduleJobOptions, CancellationToken)"] = new([SchedulerRoutes.ScheduleJob], s => s.ScheduleJob(TestData.JobDetail, TestData.Wire.SimpleTrigger).AsTask()),
        ["ScheduleJob(ITrigger, ScheduleJobOptions, CancellationToken)"] = new([SchedulerRoutes.ScheduleJob], s => s.ScheduleJob(TestData.Wire.SimpleTrigger).AsTask()),
        ["ScheduleTrigger(ITrigger, TriggerConflict, CancellationToken)"] = new([SchedulerRoutes.ScheduleJob], s => s.ScheduleTrigger(TestData.Wire.SimpleTrigger, TriggerConflict.Keep).AsTask()),
        ["ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>>, ScheduleJobOptions, CancellationToken)"] = new([SchedulerRoutes.ScheduleJobs], s => s.ScheduleJobs(
            new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [TestData.JobDetail] = [TestData.Wire.SimpleTrigger] }).AsTask()),
        ["ScheduleJob(IJobDetail, IReadOnlyCollection<ITrigger>, ScheduleJobOptions, CancellationToken)"] = new([SchedulerRoutes.ScheduleJobs], s => s.ScheduleJob(TestData.JobDetail, [TestData.Wire.SimpleTrigger]).AsTask()),
        ["UnscheduleJob(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.UnscheduleJob], s => s.UnscheduleJob(names.Trigger).AsTask()),
        ["UnscheduleJobs(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.UnscheduleJobs], s => s.UnscheduleJobs([names.Trigger]).AsTask()),
        ["UnscheduleJobs(GroupMatcher<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.UnscheduleJobsByGroup], s => s.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals("group")).AsTask()),
        ["RescheduleJob(TriggerKey, ITrigger, CancellationToken)"] = new([SchedulerRoutes.RescheduleJob], s => s.RescheduleJob(names.Trigger, TestData.Wire.SimpleTrigger).AsTask()),
        ["UpdateTriggerDetails(TriggerKey, TriggerDetailsUpdate, CancellationToken)"] = new([SchedulerRoutes.UpdateTriggerDetails], s => s.UpdateTriggerDetails(names.Trigger, new TriggerDetailsUpdate().WithPriority(3)).AsTask()),
        ["Backfill(TriggerKey, DateTimeOffset, DateTimeOffset, BackfillOptions, CancellationToken)"] = new([SchedulerRoutes.BackfillTrigger], s => ((IBackfillingScheduler) s).Backfill(
            names.Trigger, TestData.Wire.StartTime.AddDays(-1), TestData.Wire.StartTime.AddDays(180), new BackfillOptions { Spacing = TimeSpan.FromSeconds(30) }).AsTask()),

        ["AddJob(IJobDetail, AddJobOptions, CancellationToken)"] = new([SchedulerRoutes.AddJob], s => s.AddJob(TestData.JobDetail).AsTask()),
        ["DeleteJob(JobKey, CancellationToken)"] = new([SchedulerRoutes.DeleteJob], s => s.DeleteJob(names.Job).AsTask()),
        ["DeleteJobs(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.DeleteJobs], s => s.DeleteJobs([names.Job]).AsTask()),
        ["DeleteJobs(GroupMatcher<JobKey>, CancellationToken)"] = new([SchedulerRoutes.DeleteJobsByGroup], s => s.DeleteJobs(GroupMatcher<JobKey>.GroupEquals("group")).AsTask()),
        ["TriggerJob(JobKey, JobDataMap, CancellationToken)"] = new([SchedulerRoutes.TriggerJob, SchedulerRoutes.TriggerJob], async s =>
        {
            await s.TriggerJob(names.Job);
            await s.TriggerJob(names.Job, new JobDataMap { ["reason"] = "rerun" });
        }),
        ["Interrupt(JobKey, CancellationToken)"] = new([SchedulerRoutes.InterruptJob], s => s.Interrupt(names.Job).AsTask()),
        ["InterruptFireInstance(String, CancellationToken)"] = new([SchedulerRoutes.InterruptJobInstance], s => s.InterruptFireInstance(names.FireInstanceId).AsTask()),

        ["PauseJob(JobKey, CancellationToken)"] = new([SchedulerRoutes.PauseJob], s => s.PauseJob(names.Job).AsTask()),
        ["PauseJobWith(JobKey, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseJob], s => s.PauseJobWith(names.Job, Why).AsTask()),
        ["PauseJobs(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.PauseJobKeys], s => s.PauseJobs([names.Job]).AsTask()),
        ["PauseJobGroups(GroupMatcher<JobKey>, CancellationToken)"] = new([SchedulerRoutes.PauseJobs], s => s.PauseJobGroups(GroupMatcher<JobKey>.AnyGroup()).AsTask()),
        ["PauseJobGroupsWith(GroupMatcher<JobKey>, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseJobs], s => s.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupStartsWith("gr"), Why).AsTask()),
        ["ResumeJob(JobKey, CancellationToken)"] = new([SchedulerRoutes.ResumeJob], s => s.ResumeJob(names.Job).AsTask()),
        ["ResumeJobs(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeJobKeys], s => s.ResumeJobs([names.Job]).AsTask()),
        ["ResumeJobGroups(GroupMatcher<JobKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeJobs], s => s.ResumeJobGroups(GroupMatcher<JobKey>.GroupEndsWith("up")).AsTask()),
        ["PauseTrigger(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.PauseTrigger], s => s.PauseTrigger(names.Trigger).AsTask()),
        ["PauseTriggerWith(TriggerKey, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseTrigger], s => s.PauseTriggerWith(names.Trigger, Why).AsTask()),
        ["PauseTriggers(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.PauseTriggerKeys], s => s.PauseTriggers([names.Trigger]).AsTask()),
        ["PauseTriggerGroups(GroupMatcher<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.PauseTriggers], s => s.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupContains("ou")).AsTask()),
        ["PauseTriggerGroupsWith(GroupMatcher<TriggerKey>, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseTriggers], s => s.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("group"), Why).AsTask()),
        ["ResumeTrigger(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.ResumeTrigger], s => s.ResumeTrigger(names.Trigger).AsTask()),
        ["ResumeTriggers(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeTriggerKeys], s => s.ResumeTriggers([names.Trigger]).AsTask()),
        ["ResumeTriggerGroups(GroupMatcher<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeTriggers], s => s.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("group")).AsTask()),
        ["GetTriggerPause(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.GetTriggerState], s => s.GetTriggerPause(names.Trigger).AsTask()),
        ["GetTriggerGroupPause(String, CancellationToken)"] = new([SchedulerRoutes.IsTriggerGroupPaused], s => s.GetTriggerGroupPause(names.Group).AsTask()),
        ["GetJobGroupPause(String, CancellationToken)"] = new([SchedulerRoutes.IsJobGroupPaused], s => s.GetJobGroupPause(names.Group).AsTask()),

        ["QueryJobs(JobQuery, CancellationToken)"] = new([SchedulerRoutes.QueryJobs], s => s.QueryJobs(new JobQuery { Group = GroupMatcher<JobKey>.GroupEquals("group"), Skip = 1, Take = 5, IncludeTotalCount = true }).AsTask()),
        ["QueryTriggers(TriggerQuery, CancellationToken)"] = new([SchedulerRoutes.QueryTriggers], s => s.QueryTriggers(new TriggerQuery { Job = names.Job, State = TriggerState.Paused }).AsTask()),
        ["QueryJobGroups(JobGroupQuery, CancellationToken)"] = new([SchedulerRoutes.QueryJobGroups], s => s.QueryJobGroups(new JobGroupQuery { Paused = true }).AsTask()),
        ["QueryTriggerGroups(TriggerGroupQuery, CancellationToken)"] = new([SchedulerRoutes.QueryTriggerGroups], s => s.QueryTriggerGroups(new TriggerGroupQuery { Name = NameMatcher.NameStartsWith("gr") }).AsTask()),
        ["QueryCalendarNames(CalendarQuery, CancellationToken)"] = new([SchedulerRoutes.QueryCalendarNames], s => s.QueryCalendarNames(new CalendarQuery { Take = PagedQuery.All }).AsTask()),
        ["GetJobDetails(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.FetchJobs], s => s.GetJobDetails([names.Job]).AsTask()),
        ["GetTriggers(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.FetchTriggers], s => s.GetTriggers([names.Trigger]).AsTask()),
        ["GetJobDetail(JobKey, CancellationToken)"] = new([SchedulerRoutes.GetJobDetails], s => s.GetJobDetail(names.Job).AsTask()),
        ["GetTrigger(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.GetTrigger], s => s.GetTrigger(names.Trigger).AsTask()),
        ["GetTriggerState(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.GetTriggerState], s => s.GetTriggerState(names.Trigger).AsTask()),
        ["ResetTriggerFromErrorState(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.ResetTriggerFromErrorState], s => s.ResetTriggerFromErrorState(names.Trigger).AsTask()),
        ["ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.ResetTriggerKeysFromErrorState], s => s.ResetTriggersFromErrorState([names.Trigger]).AsTask()),
        ["Exists(JobKey, CancellationToken)"] = new([SchedulerRoutes.CheckJobExists], s => s.Exists(names.Job).AsTask()),
        ["Exists(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.CheckTriggerExists], s => s.Exists(names.Trigger).AsTask()),

        ["AddCalendar(String, ICalendar, AddCalendarOptions, CancellationToken)"] = new([SchedulerRoutes.AddCalendar], s => s.AddCalendar(names.Calendar, TestData.Wire.HolidayCalendar).AsTask()),
        ["DeleteCalendar(String, CancellationToken)"] = new([SchedulerRoutes.DeleteCalendar], s => s.DeleteCalendar(names.Calendar).AsTask()),
        ["GetCalendar(String, CancellationToken)"] = new([SchedulerRoutes.GetCalendar], s => s.GetCalendar(names.Calendar).AsTask()),
        ["Exists(String, CancellationToken)"] = new([SchedulerRoutes.CheckCalendarExists], s => s.Exists(names.Calendar).AsTask()),
    };

    /// <summary>
    /// The members <see cref="HttpScheduler" /> implements without asking the target, and why.
    /// </summary>
    private static readonly Dictionary<string, string> NotOverTheWire = new(StringComparer.Ordinal)
    {
        ["get_SchedulerName()"] = "the name the client was constructed with, which every request is addressed to",
        ["get_TimeProvider()"] = "the system clock: a clock cannot be fetched over a wire",
        ["get_Context()"] = "not supported: the context is a live object in the scheduler's own process",
        ["get_ListenerManager()"] = "not supported: listeners run where the jobs run",
    };

    /// <summary>
    /// The routes <see cref="HttpScheduler" /> never sends, and who does.
    /// </summary>
    private static readonly Dictionary<string, string> ServedToOtherClients = new(StringComparer.Ordinal)
    {
        [SchedulerRoutes.GetAllSchedulers.Name] = "a listing of the target's schedulers, which no IScheduler member asks for",
        [SchedulerRoutes.GetSchedulerContext.Name] = "the context, which HttpScheduler refuses to pretend to hold",
        [SchedulerRoutes.GetJobTriggers.Name] = "GetTriggersOfJob is an extension over QueryTriggers and GetTriggers",
        [SchedulerRoutes.StreamEvents.Name] = "the event stream, read by HttpSchedulerEventReader",
        [SchedulerRoutes.QueryExecutionHistory.Name] = "history, read by HttpExecutionHistoryStore",
        [SchedulerRoutes.GetExecution.Name] = "history, read by HttpExecutionHistoryStore",
        [SchedulerRoutes.QueryMisfireHistory.Name] = "history, read by HttpExecutionHistoryStore",
        [SchedulerRoutes.CountMisfires.Name] = "history, read by HttpExecutionHistoryStore",
    };

    private static Task Read<T>(T value)
    {
        GC.KeepAlive(value);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The members <see cref="HttpScheduler" /> implements itself, named as the contract declares them: every
    /// one of <see cref="IScheduler" />'s it does not leave to a default body, and
    /// <see cref="IBackfillingScheduler.Backfill" />, the internal contract the backfill extension sends to it.
    /// </summary>
    private static IEnumerable<MethodInfo> ImplementedMembers()
    {
        foreach (Type contract in (Type[]) [typeof(IScheduler), typeof(IBackfillingScheduler)])
        {
            InterfaceMapping map = typeof(HttpScheduler).GetInterfaceMap(contract);
            for (int i = 0; i < map.TargetMethods.Length; i++)
            {
                if (map.TargetMethods[i].DeclaringType == typeof(HttpScheduler))
                {
                    yield return map.InterfaceMethods[i];
                }
            }
        }
    }

    private static string Signature(MethodInfo method)
    {
        return $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => TypeName(parameter.ParameterType)))})";
    }

    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    /// <summary>
    /// A scheduler that answers every read <see cref="HttpScheduler" /> makes with something the endpoint
    /// can put on the wire, so each request is seen through to a success.
    /// </summary>
    private static IScheduler AnsweringScheduler()
    {
        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => fake.GetMetadata(A<CancellationToken>._)).Returns(TestData.Wire.Metadata);
        A.CallTo(() => fake.GetExecutionLimits(A<CancellationToken>._)).Returns((ExecutionLimits?) null);
        A.CallTo(() => fake.GetJobDetail(A<JobKey>._, A<CancellationToken>._)).Returns(TestData.JobDetail);
        A.CallTo(() => fake.GetTrigger(A<TriggerKey>._, A<CancellationToken>._)).Returns(TestData.Wire.SimpleTrigger);
        A.CallTo(() => fake.GetCalendar(A<string>._, A<CancellationToken>._)).Returns(TestData.Wire.HolidayCalendar);
        A.CallTo(() => fake.QueryJobs(A<JobQuery>._, A<CancellationToken>._)).Returns(new PagedResult<JobHeader>([], HasMore: false));
        A.CallTo(() => fake.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._)).Returns(new PagedResult<TriggerHeader>([], HasMore: false));
        A.CallTo(() => fake.QueryJobGroups(A<JobGroupQuery>._, A<CancellationToken>._)).Returns(new PagedResult<JobGroup>([], HasMore: false));
        A.CallTo(() => fake.QueryTriggerGroups(A<TriggerGroupQuery>._, A<CancellationToken>._)).Returns(new PagedResult<TriggerGroup>([], HasMore: false));
        A.CallTo(() => fake.QueryCalendarNames(A<CalendarQuery>._, A<CancellationToken>._)).Returns(new PagedResult<string>([], HasMore: false));
        A.CallTo(() => fake.QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._)).Returns(new PagedResult<FireInstance>([], HasMore: false));

        // The backfill the host runs reads its own clock, and finds none of the slots it schedules stored yet.
        A.CallTo(() => fake.TimeProvider).Returns(TimeProvider.System);
        A.CallTo(() => fake.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).Returns(new List<ITrigger>());
        return fake;
    }

    /// <summary>
    /// A transport that remembers every request it carries before handing it on.
    /// </summary>
    private sealed class RecordingTransport : IWireTransport
    {
        private readonly IWireTransport inner;

        public RecordingTransport(IWireTransport inner)
        {
            this.inner = inner;
        }

        public List<WireRequest> Sent { get; } = [];

        public ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return inner.Send(request, cancellationToken);
        }
    }

    /// <summary>
    /// One request as the host served it: which endpoint ASP.NET Core routed it to, the values it read
    /// out of the path, and the status it answered with.
    /// </summary>
    private sealed record Served(string Method, string? EndpointName, Dictionary<string, string?> RouteValues, int Status);

    /// <summary>
    /// Wraps the host's whole pipeline, so that once a request has been answered the endpoint routing chose
    /// for it is still there to be read.
    /// </summary>
    private sealed class ServedRequests : IStartupFilter
    {
        private readonly Lock gate = new();
        private readonly List<Served> requests = [];

        public List<Served> Requests
        {
            get
            {
                lock (gate)
                {
                    return [.. requests];
                }
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                requests.Clear();
            }
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                await nextMiddleware(context);

                Served request = new(
                    context.Request.Method,
                    context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    context.Request.RouteValues.ToDictionary(value => value.Key, value => value.Value?.ToString()),
                    context.Response.StatusCode);

                lock (gate)
                {
                    requests.Add(request);
                }
            });

            next(app);
        };
    }
}
