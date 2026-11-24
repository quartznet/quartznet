using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FakeItEasy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The HTTP API serving a scheduler behind a target (#4218): a request names the target with
/// <c>?target=</c>, the host's own handler runs against the <c>HttpScheduler</c> it reaches the target
/// with, and the answer says which target it was.
/// </summary>
/// <remarks>
/// Two hosts in one process: the worker is the test host with a fake scheduler, and the gateway is an
/// application that registers the worker as target <c>w1</c> with <c>AddQuartzHttpClient</c> and maps the
/// API over it, as the dashboard host does.
/// </remarks>
public sealed class GatewayTargetOverHttpTest
{
    private const string Target = "w1";
    private const string SchedulerUrl = "schedulers/" + TestData.SchedulerName;

    private readonly List<WebApplication> gateways = [];
    private readonly List<HttpClient> clients = [];
    private readonly RecordingLoggerProvider logs = new();

    private WebApplicationFactory<Program> worker = null!;
    private IScheduler workerScheduler = null!;
    private HttpClient toWorker = null!;
    private readonly List<PauseDetails?> pausedWith = [];

    [SetUp]
    public void SetUp()
    {
        TestContentRoot.Apply();
        pausedWith.Clear();
        worker = new WebApplicationFactory<Program>();

        ISchedulerRepository repository = worker.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        workerScheduler = A.Fake<IScheduler>();
        A.CallTo(() => workerScheduler.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => workerScheduler.SchedulerInstanceId).Returns(TestData.SchedulerInstanceId);
        A.CallTo(() => workerScheduler.Status).Returns(SchedulerStatus.Running);
        A.CallTo(() => workerScheduler.GetStatus(A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => workerScheduler.GetSchedulerInstanceId(A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => workerScheduler.GetMetadata(A<CancellationToken>._)).Returns(TestData.Wire.Metadata);
        A.CallTo(() => workerScheduler.PauseAllWith(A<PauseDetails?>._, A<CancellationToken>._))
            .Invokes((PauseDetails? details, CancellationToken _) => pausedWith.Add(details));
        repository.Bind(workerScheduler);

        toWorker = worker.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (HttpClient client in clients)
        {
            client.Dispose();
        }

        foreach (WebApplication gateway in gateways)
        {
            await gateway.DisposeAsync();
        }

        clients.Clear();
        gateways.Clear();
        toWorker.Dispose();
        await workerScheduler.DisposeAsync();
        await worker.DisposeAsync();
        logs.Clear();
    }

    [Test]
    public async Task ARequestNamingTheTargetIsServedByTheTargetsSchedulerAndSaysSo()
    {
        HttpClient gateway = await Gateway();

        using HttpResponseMessage details = await gateway.GetAsync($"{SchedulerUrl}?target={Target}");
        using HttpResponseMessage paused = await gateway.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);

        details.StatusCode.Should().Be(HttpStatusCode.OK, await details.Content.ReadAsStringAsync());
        details.Headers.GetValues(HttpApiConstants.TargetHeader).Should().Equal([Target],
            "a client that named a target refuses a success that does not say it was the target's");
        (await details.Content.ReadAsStringAsync()).Should().Contain(TestData.Wire.Metadata.SchedulerInstanceId,
            "the details are the worker's, read through the HttpScheduler the gateway reaches it with");

        paused.StatusCode.Should().Be(HttpStatusCode.OK);
        A.CallTo(() => workerScheduler.PauseAll(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ABareNameHeldOnlyByATargetIsRefusedNamingTheTargetToAddress()
    {
        HttpClient gateway = await Gateway();

        using HttpResponseMessage bare = await gateway.GetAsync(SchedulerUrl);

        bare.StatusCode.Should().Be(HttpStatusCode.NotFound, "a bare key never reaches a targeted scheduler, as before 4.9");
        bare.Headers.Contains(HttpApiConstants.TargetHeader).Should().BeFalse("the request named no target");
        (await bare.Content.ReadAsStringAsync()).Should().Contain($"Unknown scheduler {TestData.SchedulerName}")
            .And.Contain($"?target={Target}", "the refusal says how to spell the request that would have resolved");
    }

    [TestCase("", "empty")]
    [TestCase("%20", "empty")]
    [TestCase("a%2Fb", "contains '/'")]
    public async Task AMalformedTargetIsRefusedBeforeAnythingElse(string target, string why)
    {
        HttpClient gateway = await Gateway();

        using HttpResponseMessage response = await gateway.GetAsync($"{SchedulerUrl}?target={target}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(why).And.Contain(HttpApiConstants.RequestRefusedExceptionType,
            "it is the refusal every malformed request gets, which HttpScheduler raises as such");
    }

    [Test]
    public async Task ATargetGivenTwiceIsRefused()
    {
        HttpClient gateway = await Gateway();

        using HttpResponseMessage response = await gateway.GetAsync($"{SchedulerUrl}?target={Target}&target=w2");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("2 times");
    }

    [Test]
    public async Task AnUnknownTargetIsNotFoundAndStillEchoed()
    {
        HttpClient gateway = await Gateway();

        using HttpResponseMessage response = await gateway.GetAsync($"{SchedulerUrl}?target=w9");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.GetValues(HttpApiConstants.TargetHeader).Should().Equal(["w9"],
            "every answer to a request that named a target carries it, so a client can tell this host is 4.9");
        (await response.Content.ReadAsStringAsync()).Should().Contain($"Unknown scheduler w9/{TestData.SchedulerName}",
            "HttpScheduler reads 'Unknown scheduler' as a scheduler no request can reach, rather than as an absent job");
    }

    [Test]
    public async Task TheListingNamesEachSchedulerWithItsTarget()
    {
        HttpClient gateway = await Gateway();

        SchedulerHeaderDto[] listed = (await gateway.GetFromJsonAsync<SchedulerHeaderDto[]>("schedulers", WireOptions()))!;

        listed.Should().ContainSingle(row => row.Name == TestData.SchedulerName)
            .Which.Target.Should().Be(Target, "the key a request names it by is the target and the name");
    }

    /// <summary>
    /// What a script does: <c>HttpScheduler</c> built from the key the gateway lists, which names the target
    /// on every request and is answered by the worker's scheduler.
    /// </summary>
    [Test]
    public async Task HttpSchedulerBuiltFromTheKeyDrivesTheTargetThroughTheGateway()
    {
        HttpClient gateway = await Gateway();
        await using HttpScheduler scheduler = new(new SchedulerRef(TestData.SchedulerName, Target), gateway);

        SchedulerMetadata metadata = await scheduler.GetMetadata();
        await scheduler.PauseAllWith(new PauseDetails { Reason = "release freeze" });

        metadata.SchedulerInstanceId.Should().Be(TestData.Wire.Metadata.SchedulerInstanceId);
        pausedWith.Should().ContainSingle().Which!.Reason.Should().Be("release freeze");
    }

    /// <summary>
    /// The pause a gateway caller makes without naming a requester is the caller's, and the target records
    /// it so: the gateway's handler fills it in, and <c>HttpScheduler</c> sends it on (G7).
    /// </summary>
    [Test]
    public async Task APauseThroughTheGatewayIsInTheGatewayCallersName()
    {
        HttpClient gateway = await Gateway(
            services =>
            {
                services.AddRoleAuthorization();
                services.AddQuartzHttpApi();
            },
            app => app.MapQuartzHttpApi("/quartz-api").RequireAuthorization());
        gateway.DefaultRequestHeaders.Add(RoleAuthenticationHandler.RoleHeaderName, "alice");

        using HttpResponseMessage response = await gateway.PostAsJsonAsync($"{SchedulerUrl}/pause-all?target={Target}", new { reason = "release freeze" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        PauseDetails details = pausedWith.Should().ContainSingle().Subject!;
        details.RequestedBy.Should().Be("alice-user",
            "the worker sees the gateway's credential, so who asked is the one thing the gateway's handler adds for it");
        details.Reason.Should().Be("release freeze");
    }

    /// <summary>
    /// The audit line of a targeted change names the key, which the path it also logs leaves out (G8).
    /// </summary>
    [Test]
    public async Task TheAuditLineOfATargetedChangeNamesTheKey()
    {
        HttpClient gateway = await Gateway();

        using HttpResponseMessage response = await gateway.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RecordedLogEntry audit = logs.WithEventId(9007).Should().ContainSingle().Subject;
        audit.Properties["SchedulerName"].Should().Be($"{Target}/{TestData.SchedulerName}");
        audit.Properties["Route"].Should().Be($"/quartz-api/{SchedulerUrl}/pause-all", "the template is unchanged: the route is the path");
    }

    /// <summary>
    /// Every audited change and every refusal for a role reaches the recorders registered beside the API —
    /// the dashboard's Action Log in a process that maps it — and a recorder that throws costs the request
    /// nothing.
    /// </summary>
    [Test]
    public async Task EveryAuditedChangeAndRefusalReachesTheRecordersAndAFailingOneCostsNothing()
    {
        // The worker says it paused the trigger: an answer of applied: false is entered as not done.
        A.CallTo(() => workerScheduler.PauseTrigger(A<TriggerKey>._, A<CancellationToken>._)).Returns(true);
        RecordingRecorder recorder = new();
        HttpClient gateway = await Gateway(
            services =>
            {
                services.AddRoleAuthorization();
                services.AddSingleton<Quartz.AspNetCore.IOperatorActionRecorder>(recorder);
                services.AddSingleton<Quartz.AspNetCore.IOperatorActionRecorder>(new ThrowingRecorder());
                services.AddQuartzHttpApi(options => options.OperationAuthorizationPolicy = RoleAuthenticationExtensions.OperatorRolesPolicy);
            },
            app => app.MapQuartzHttpApi("/quartz-api").AllowAnonymous());

        using HttpResponseMessage paused = await Client(gateway, RoleAuthenticationExtensions.Operator)
            .PostAsync($"{SchedulerUrl}/triggers/reports/nightly/pause?target={Target}", content: null);
        using HttpResponseMessage refused = await Client(gateway, RoleAuthenticationExtensions.Viewer)
            .PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);

        paused.StatusCode.Should().Be(HttpStatusCode.OK, "a recorder that throws is a bug in the recorder, and the change was made");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        recorder.Actions.Should().BeEquivalentTo(
        [
            new Quartz.AspNetCore.OperatorAction(Quartz.AspNetCore.OperatorChannel.HttpApi, RoleAuthenticationExtensions.Operator + "-user",
                $"{Target}/{TestData.SchedulerName}", "PauseTrigger", "reports.nightly", Succeeded: true),
            new Quartz.AspNetCore.OperatorAction(Quartz.AspNetCore.OperatorChannel.HttpApi, RoleAuthenticationExtensions.Viewer + "-user",
                $"{Target}/{TestData.SchedulerName}", "PauseAll", $"{Target}/{TestData.SchedulerName}", Succeeded: false)
            {
                Refused = true,
                Message = "needs Operate"
            }
        ], options => options.WithStrictOrdering());
        logs.WithEventId(9013).Should().HaveCount(2, "each failure of the throwing recorder is logged, once per action");
    }

    [Test]
    public async Task TheTargetsHistoryIsReadThroughTheGateway()
    {
        HttpClient gateway = await Gateway();

        using HttpResponseMessage response = await gateway.GetAsync($"{SchedulerUrl}/history/executions?target={Target}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.GetValues(HttpApiConstants.TargetHeader).Should().Equal([Target]);
    }

    /// <summary>
    /// Both policies are asked with the target, before anything is resolved, so a refused caller cannot
    /// tell a missing target from a forbidden one, and the listing asks per name and target (G4).
    /// </summary>
    [Test]
    public async Task BothPoliciesAreAskedWithTheTargetBeforeAnythingIsResolved()
    {
        List<SchedulerResource> asked = [];

        HttpClient gateway = await Gateway(
            services =>
            {
                services.AddRoleAuthorization();
                services.AddAuthorizationBuilder().AddPolicy("PerTarget", policy => policy.RequireAssertion(context =>
                {
                    SchedulerResource resource = (SchedulerResource) context.Resource!;
                    lock (asked)
                    {
                        asked.Add(resource);
                    }

                    return resource.Target != "forbidden";
                }));
                services.AddQuartzHttpApi(options =>
                {
                    options.SchedulerAuthorizationPolicy = "PerTarget";
                    options.OperationAuthorizationPolicy = RoleAuthenticationExtensions.OperatorRolesPolicy;
                });
            },
            app => app.MapQuartzHttpApi("/quartz-api").AllowAnonymous());

        HttpClient asOperator = Client(gateway, RoleAuthenticationExtensions.Operator);
        HttpClient asViewer = Client(gateway, RoleAuthenticationExtensions.Viewer);

        using HttpResponseMessage served = await asOperator.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        asked.Should().OnlyContain(resource => resource.Target == Target && resource.SchedulerName == TestData.SchedulerName,
            "the per-scheduler policy is asked with the key the request names");

        using HttpResponseMessage forbiddenExisting = await asViewer.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);
        using HttpResponseMessage forbiddenMissing = await asViewer.PostAsync($"{SchedulerUrl}/pause-all?target=w9", content: null);
        forbiddenExisting.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer may not pause");
        forbiddenMissing.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authorization comes before resolution, so a target that does not exist is refused as one that does");

        using HttpResponseMessage refusedByTarget = await asOperator.GetAsync($"{SchedulerUrl}?target=forbidden");
        refusedByTarget.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpResponseMessage withData = await asOperator.PostAsJsonAsync(
            $"{SchedulerUrl}/jobs/reports/nightly/trigger?target={Target}", new { jobData = new Dictionary<string, string> { ["x"] = "1" } });
        withData.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a trigger-now carrying data needs Administer, through a target as anywhere");
        (await withData.Content.ReadAsStringAsync()).Should().Contain("with job data").And.Contain($"{Target}/{TestData.SchedulerName}");

        asked.Clear();
        SchedulerHeaderDto[] listed = (await asOperator.GetFromJsonAsync<SchedulerHeaderDto[]>("schedulers", WireOptions()))!;
        listed.Should().ContainSingle(row => row.Target == Target);
        asked.Should().Contain(resource => resource.Target == Target, "the listing asks each registration with its target");
    }

    [Test]
    public async Task TheOperationPolicyResourceCarriesTheTarget()
    {
        List<SchedulerResource> asked = [];

        HttpClient gateway = await Gateway(
            services =>
            {
                services.AddAuthorizationBuilder().AddPolicy("Recording", policy => policy.RequireAssertion(context =>
                {
                    lock (asked)
                    {
                        asked.Add((SchedulerResource) context.Resource!);
                    }

                    return true;
                }));
                services.AddQuartzHttpApi(options => options.OperationAuthorizationPolicy = "Recording");
            },
            app => app.MapQuartzHttpApi("/quartz-api").AllowAnonymous());

        using HttpResponseMessage response = await gateway.PostAsync($"{SchedulerUrl}/standby?target={Target}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        asked.Should().ContainSingle().Which.Should().BeEquivalentTo(new SchedulerResource(TestData.SchedulerName)
        {
            Target = Target,
            Operation = SchedulerRoutes.Standby.Name,
            Access = SchedulerAccess.Operate
        });
    }

    private HttpClient Client(HttpClient gateway, string role)
    {
        HttpClient client = new(new ForwardingHandler(gateway)) { BaseAddress = gateway.BaseAddress };
        client.DefaultRequestHeaders.Add(RoleAuthenticationHandler.RoleHeaderName, role);
        clients.Add(client);
        return client;
    }

    private async Task<HttpClient> Gateway(Action<IServiceCollection>? configure = null, Action<WebApplication>? map = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Debug);

        builder.Services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = TestData.SchedulerName;
            options.Target = Target;
            options.CreateHttpClient = _ => toWorker;
        });

        if (configure is null)
        {
            builder.Services.AddQuartzHttpApi();
        }
        else
        {
            configure(builder.Services);
        }

        WebApplication app = builder.Build();
        gateways.Add(app);

        if (map is null)
        {
            app.MapQuartzHttpApi("/quartz-api").AllowAnonymous();
        }
        else
        {
            map(app);
        }

        await app.StartAsync();

        HttpClient client = app.GetTestClient();
        client.BaseAddress = new Uri(client.BaseAddress!, "quartz-api/");
        clients.Add(client);
        return client;
    }

    private static JsonSerializerOptions WireOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
    }

    private sealed class RecordingRecorder : Quartz.AspNetCore.IOperatorActionRecorder
    {
        public List<Quartz.AspNetCore.OperatorAction> Actions { get; } = [];

        public void Record(Quartz.AspNetCore.OperatorAction action)
        {
            lock (Actions)
            {
                Actions.Add(action);
            }
        }
    }

    private sealed class ThrowingRecorder : Quartz.AspNetCore.IOperatorActionRecorder
    {
        public void Record(Quartz.AspNetCore.OperatorAction action) => throw new InvalidOperationException("the recorder is broken");
    }

    /// <summary>
    /// Sends through another client's handler, so several callers with headers of their own share one
    /// test server.
    /// </summary>
    private sealed class ForwardingHandler(HttpClient inner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpRequestMessage copy = new(request.Method, request.RequestUri);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                copy.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken));
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                {
                    copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return await inner.SendAsync(copy, cancellationToken);
        }
    }
}
