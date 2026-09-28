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
    private static readonly JobKey Job = new("nightly export", "reports");
    private static readonly TriggerKey Trigger = new("at midnight", "reports");
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
        List<string> implemented = ImplementedMembers()
            .Select(Signature)
            .Where(signature => !NotOverTheWire.ContainsKey(signature))
            .ToList();

        implemented.Should().BeEquivalentTo(OverTheWire.Keys,
            "every IScheduler member HttpScheduler implements needs a row, so a member added without one fails here rather than going unchecked");

        HashSet<WireRoute> sent = [];
        using (new AssertionScope())
        {
            foreach ((string signature, Row row) in OverTheWire)
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
    /// One row per <see cref="IScheduler" /> member <see cref="HttpScheduler" /> implements and answers over
    /// the wire, keyed by its signature. A row that has two ways to send — a body or none, a limit or its
    /// clearing — sends both.
    /// </summary>
    /// <remarks>
    /// The keys carry spaces, which the client puts into a path unescaped and the one group read escapes,
    /// so the catalogue's reading of a path is held to ASP.NET Core's on values that are not plain words.
    /// </remarks>
    private static readonly Dictionary<string, Row> OverTheWire = new(StringComparer.Ordinal)
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
        ["UnscheduleJob(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.UnscheduleJob], s => s.UnscheduleJob(Trigger).AsTask()),
        ["UnscheduleJobs(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.UnscheduleJobs], s => s.UnscheduleJobs([Trigger]).AsTask()),
        ["UnscheduleJobs(GroupMatcher<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.UnscheduleJobsByGroup], s => s.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals("group")).AsTask()),
        ["RescheduleJob(TriggerKey, ITrigger, CancellationToken)"] = new([SchedulerRoutes.RescheduleJob], s => s.RescheduleJob(Trigger, TestData.Wire.SimpleTrigger).AsTask()),
        ["UpdateTriggerDetails(TriggerKey, TriggerDetailsUpdate, CancellationToken)"] = new([SchedulerRoutes.UpdateTriggerDetails], s => s.UpdateTriggerDetails(Trigger, new TriggerDetailsUpdate().WithPriority(3)).AsTask()),

        ["AddJob(IJobDetail, AddJobOptions, CancellationToken)"] = new([SchedulerRoutes.AddJob], s => s.AddJob(TestData.JobDetail).AsTask()),
        ["DeleteJob(JobKey, CancellationToken)"] = new([SchedulerRoutes.DeleteJob], s => s.DeleteJob(Job).AsTask()),
        ["DeleteJobs(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.DeleteJobs], s => s.DeleteJobs([Job]).AsTask()),
        ["DeleteJobs(GroupMatcher<JobKey>, CancellationToken)"] = new([SchedulerRoutes.DeleteJobsByGroup], s => s.DeleteJobs(GroupMatcher<JobKey>.GroupEquals("group")).AsTask()),
        ["TriggerJob(JobKey, JobDataMap, CancellationToken)"] = new([SchedulerRoutes.TriggerJob, SchedulerRoutes.TriggerJob], async s =>
        {
            await s.TriggerJob(Job);
            await s.TriggerJob(Job, new JobDataMap { ["reason"] = "rerun" });
        }),
        ["Interrupt(JobKey, CancellationToken)"] = new([SchedulerRoutes.InterruptJob], s => s.Interrupt(Job).AsTask()),
        ["InterruptFireInstance(String, CancellationToken)"] = new([SchedulerRoutes.InterruptJobInstance], s => s.InterruptFireInstance("fire-1").AsTask()),

        ["PauseJob(JobKey, CancellationToken)"] = new([SchedulerRoutes.PauseJob], s => s.PauseJob(Job).AsTask()),
        ["PauseJobWith(JobKey, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseJob], s => s.PauseJobWith(Job, Why).AsTask()),
        ["PauseJobs(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.PauseJobKeys], s => s.PauseJobs([Job]).AsTask()),
        ["PauseJobGroups(GroupMatcher<JobKey>, CancellationToken)"] = new([SchedulerRoutes.PauseJobs], s => s.PauseJobGroups(GroupMatcher<JobKey>.AnyGroup()).AsTask()),
        ["PauseJobGroupsWith(GroupMatcher<JobKey>, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseJobs], s => s.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupStartsWith("gr"), Why).AsTask()),
        ["ResumeJob(JobKey, CancellationToken)"] = new([SchedulerRoutes.ResumeJob], s => s.ResumeJob(Job).AsTask()),
        ["ResumeJobs(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeJobKeys], s => s.ResumeJobs([Job]).AsTask()),
        ["ResumeJobGroups(GroupMatcher<JobKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeJobs], s => s.ResumeJobGroups(GroupMatcher<JobKey>.GroupEndsWith("up")).AsTask()),
        ["PauseTrigger(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.PauseTrigger], s => s.PauseTrigger(Trigger).AsTask()),
        ["PauseTriggerWith(TriggerKey, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseTrigger], s => s.PauseTriggerWith(Trigger, Why).AsTask()),
        ["PauseTriggers(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.PauseTriggerKeys], s => s.PauseTriggers([Trigger]).AsTask()),
        ["PauseTriggerGroups(GroupMatcher<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.PauseTriggers], s => s.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupContains("ou")).AsTask()),
        ["PauseTriggerGroupsWith(GroupMatcher<TriggerKey>, PauseDetails, CancellationToken)"] = new([SchedulerRoutes.PauseTriggers], s => s.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("group"), Why).AsTask()),
        ["ResumeTrigger(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.ResumeTrigger], s => s.ResumeTrigger(Trigger).AsTask()),
        ["ResumeTriggers(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeTriggerKeys], s => s.ResumeTriggers([Trigger]).AsTask()),
        ["ResumeTriggerGroups(GroupMatcher<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.ResumeTriggers], s => s.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("group")).AsTask()),
        ["GetTriggerPause(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.GetTriggerState], s => s.GetTriggerPause(Trigger).AsTask()),
        ["GetTriggerGroupPause(String, CancellationToken)"] = new([SchedulerRoutes.IsTriggerGroupPaused], s => s.GetTriggerGroupPause("night shift").AsTask()),
        ["GetJobGroupPause(String, CancellationToken)"] = new([SchedulerRoutes.IsJobGroupPaused], s => s.GetJobGroupPause("night shift").AsTask()),

        ["QueryJobs(JobQuery, CancellationToken)"] = new([SchedulerRoutes.QueryJobs], s => s.QueryJobs(new JobQuery { Group = GroupMatcher<JobKey>.GroupEquals("group"), Skip = 1, Take = 5, IncludeTotalCount = true }).AsTask()),
        ["QueryTriggers(TriggerQuery, CancellationToken)"] = new([SchedulerRoutes.QueryTriggers], s => s.QueryTriggers(new TriggerQuery { Job = Job, State = TriggerState.Paused }).AsTask()),
        ["QueryJobGroups(JobGroupQuery, CancellationToken)"] = new([SchedulerRoutes.QueryJobGroups], s => s.QueryJobGroups(new JobGroupQuery { Paused = true }).AsTask()),
        ["QueryTriggerGroups(TriggerGroupQuery, CancellationToken)"] = new([SchedulerRoutes.QueryTriggerGroups], s => s.QueryTriggerGroups(new TriggerGroupQuery { Name = NameMatcher.NameStartsWith("gr") }).AsTask()),
        ["QueryCalendarNames(CalendarQuery, CancellationToken)"] = new([SchedulerRoutes.QueryCalendarNames], s => s.QueryCalendarNames(new CalendarQuery { Take = PagedQuery.All }).AsTask()),
        ["GetJobDetails(IReadOnlyCollection<JobKey>, CancellationToken)"] = new([SchedulerRoutes.FetchJobs], s => s.GetJobDetails([Job]).AsTask()),
        ["GetTriggers(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.FetchTriggers], s => s.GetTriggers([Trigger]).AsTask()),
        ["GetJobDetail(JobKey, CancellationToken)"] = new([SchedulerRoutes.GetJobDetails], s => s.GetJobDetail(TestData.JobDetail.Key).AsTask()),
        ["GetTrigger(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.GetTrigger], s => s.GetTrigger(TestData.Wire.SimpleTrigger.Key).AsTask()),
        ["GetTriggerState(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.GetTriggerState], s => s.GetTriggerState(Trigger).AsTask()),
        ["ResetTriggerFromErrorState(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.ResetTriggerFromErrorState], s => s.ResetTriggerFromErrorState(Trigger).AsTask()),
        ["ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey>, CancellationToken)"] = new([SchedulerRoutes.ResetTriggerKeysFromErrorState], s => s.ResetTriggersFromErrorState([Trigger]).AsTask()),
        ["Exists(JobKey, CancellationToken)"] = new([SchedulerRoutes.CheckJobExists], s => s.Exists(Job).AsTask()),
        ["Exists(TriggerKey, CancellationToken)"] = new([SchedulerRoutes.CheckTriggerExists], s => s.Exists(Trigger).AsTask()),

        ["AddCalendar(String, ICalendar, AddCalendarOptions, CancellationToken)"] = new([SchedulerRoutes.AddCalendar], s => s.AddCalendar("holidays", TestData.Wire.HolidayCalendar).AsTask()),
        ["DeleteCalendar(String, CancellationToken)"] = new([SchedulerRoutes.DeleteCalendar], s => s.DeleteCalendar("holidays").AsTask()),
        ["GetCalendar(String, CancellationToken)"] = new([SchedulerRoutes.GetCalendar], s => s.GetCalendar("holidays").AsTask()),
        ["Exists(String, CancellationToken)"] = new([SchedulerRoutes.CheckCalendarExists], s => s.Exists("holidays").AsTask()),
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
        [SchedulerRoutes.BackfillTrigger.Name] = "Backfill is an extension over GetTrigger, GetCalendar, GetTriggers and ScheduleJob; the route runs it on the host for a client that is not .NET",
    };

    private static Task Read<T>(T value)
    {
        GC.KeepAlive(value);
        return Task.CompletedTask;
    }

    private static IEnumerable<MethodInfo> ImplementedMembers()
    {
        InterfaceMapping map = typeof(HttpScheduler).GetInterfaceMap(typeof(IScheduler));
        return map.TargetMethods.Where(method => method.DeclaringType == typeof(HttpScheduler));
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
