using System.Net;
using System.Text;
using System.Text.Json;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What <see cref="QuartzHttpApiOptions.OperationAuthorizationPolicy" /> does to the API: every route needs
/// the operator role the route table gives it, a trigger-now with job data needs Administer, the listing is
/// what the caller may view, and a refusal is <c>403</c> before anything reaches the scheduler.
/// </summary>
/// <remarks>
/// The policy is a real one, <c>RequireSchedulerRoles</c> over three roles, and the caller is a principal
/// with a role claim. The scheduler is a fake, which records whether a refused call reached it.
/// </remarks>
public sealed class OperationAuthorizationApiTest
{
    private const string Scheduler = "schedulers/" + TestData.SchedulerName;
    private const int RefusedEventId = 9009;

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly RecordingLoggerProvider logs = new();

    private WebApplicationFactory<Program> application = null!;
    private IScheduler scheduler = null!;

    [TearDown]
    public async Task TearDown()
    {
        if (scheduler is not null)
        {
            await scheduler.DisposeAsync();
        }

        // Named as well as enumerated: it is in the list below, and disposing twice is a no-op, but the
        // analyzer reads the field rather than the list.
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
        logs.Clear();
    }

    /// <summary>
    /// The first test: with no operation policy every route answers what it did in 4.7, to an anonymous
    /// caller as to any other — no route is refused, and a read-only API refuses exactly the routes that
    /// change something.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task WithoutAnOperationPolicyTheApiIsWhatItWas(bool readOnly)
    {
        Start(policy: null, readOnly: readOnly);

        List<string> wrong = [];
        foreach (WireRoute route in SweptRoutes())
        {
            HttpStatusCode status = await Send(route, role: null);
            bool refused = status == HttpStatusCode.Forbidden;
            if (refused != (readOnly && route.Mutates))
            {
                wrong.Add($"{route} answered {(int) status}");
            }
        }

        wrong.Should().BeEmpty("with no operation policy only ReadOnly refuses, and only the routes that change something");
    }

    /// <summary>
    /// Every route, asked by each role: refused when the route needs more than the role grants, and
    /// answered — whatever the answer — otherwise.
    /// </summary>
    [TestCase(RoleAuthenticationExtensions.Viewer, SchedulerAccess.View)]
    [TestCase(RoleAuthenticationExtensions.Operator, SchedulerAccess.Operate)]
    [TestCase(RoleAuthenticationExtensions.Administrator, SchedulerAccess.Administer)]
    public async Task EveryRouteNeedsItsLevel(string role, SchedulerAccess granted)
    {
        Start(RoleAuthenticationExtensions.OperatorRolesPolicy);

        List<string> wrong = [];
        foreach (WireRoute route in SweptRoutes())
        {
            HttpStatusCode status = await Send(route, role);
            bool refused = status == HttpStatusCode.Forbidden;
            if (refused != route.Access > granted)
            {
                wrong.Add($"{route} needs {route.Access}; {role} got {(int) status}");
            }
        }

        wrong.Should().BeEmpty();
    }

    /// <summary>
    /// A signed-in caller in no role may do nothing, reads included, and the listing names nothing to them.
    /// </summary>
    [Test]
    public async Task ACallerInNoRoleMayDoNothing()
    {
        Start(RoleAuthenticationExtensions.OperatorRolesPolicy);

        List<string> answered = [];
        foreach (WireRoute route in SweptRoutes().Where(route => route != SchedulerRoutes.GetAllSchedulers))
        {
            HttpStatusCode status = await Send(route, "nobody");
            if (status != HttpStatusCode.Forbidden)
            {
                answered.Add($"{route} answered {(int) status}");
            }
        }

        answered.Should().BeEmpty("a principal in none of the three roles holds no level at all");
        (await ListedFor("nobody")).Should().BeEmpty();
        (await ListedFor(RoleAuthenticationExtensions.Viewer)).Should().Contain(TestData.SchedulerName);
    }

    /// <summary>
    /// Triggering a job now is Operate without job data and Administer with it, asked once the body is read:
    /// a stored <c>NativeJob</c> runs the command its job data names.
    /// </summary>
    [Test]
    public async Task TriggeringWithJobDataIsAnAdministratorsAlone()
    {
        Start(RoleAuthenticationExtensions.OperatorRolesPolicy);
        const string Trigger = Scheduler + "/jobs/reports/nightly/trigger";
        const string WithData = """{"jobData":{"command":"rm -rf /"}}""";

        using (new AssertionScope())
        {
            (await Post(Trigger, RoleAuthenticationExtensions.Operator, body: null)).Should().Be(HttpStatusCode.OK, "no body is no job data");
            (await Post(Trigger, RoleAuthenticationExtensions.Operator, """{"jobData":{}}""")).Should().Be(HttpStatusCode.OK, "an empty map is no job data");
            (await Post(Trigger, RoleAuthenticationExtensions.Operator, WithData)).Should().Be(HttpStatusCode.Forbidden);
            (await Post(Trigger, RoleAuthenticationExtensions.Administrator, WithData)).Should().Be(HttpStatusCode.OK);
        }

        A.CallTo(() => scheduler.TriggerJob(A<JobKey>._, A<JobDataMap?>.That.Matches(map => map != null && map.Count > 0), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        logs.WithEventId(RefusedEventId).Should().ContainSingle()
            .Which.Properties["Operation"].Should().Be("TriggerJob with job data");
    }

    /// <summary>
    /// The refusal on the wire: problem details naming the operation, the scheduler and the level, without
    /// <c>Quartz-ExceptionType</c> — a rule said no — and logged once as <c>9009</c> with the user.
    /// </summary>
    [Test]
    public async Task TheRefusalNamesTheOperationAndIsLoggedWithTheUser()
    {
        Start(RoleAuthenticationExtensions.OperatorRolesPolicy);

        using HttpClient client = ClientFor(RoleAuthenticationExtensions.Operator);
        using HttpResponseMessage response = await client.DeleteAsync(Scheduler + "/jobs/reports/nightly");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("detail").GetString().Should().Be("Not authorized for DeleteJob on scheduler " + TestData.SchedulerName + ": it needs Administer");
        problem.RootElement.TryGetProperty(HttpApiConstants.ProblemDetailsExceptionType, out _).Should().BeFalse();

        RecordedLogEntry entry = logs.WithEventId(RefusedEventId).Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Properties["User"].Should().Be(RoleAuthenticationExtensions.Operator + "-user");
        entry.Properties["Operation"].Should().Be("DeleteJob");
        entry.Properties["Access"].Should().Be("Administer");
        logs.WithEventId(9005).Should().BeEmpty("the refusal is logged once, as the caller's");
        A.CallTo(() => scheduler.DeleteJob(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The refusal comes before the scheduler is looked up, so a viewer cannot tell which schedulers exist
    /// by which change they are refused.
    /// </summary>
    [Test]
    public async Task TheRefusalComesBeforeTheSchedulerIsLookedUp()
    {
        Start(RoleAuthenticationExtensions.OperatorRolesPolicy);

        (await Post("schedulers/no-such-scheduler/pause-all", RoleAuthenticationExtensions.Viewer, body: null))
            .Should().Be(HttpStatusCode.Forbidden, "a 404 here would say the scheduler does not exist");
    }

    /// <summary>
    /// <see cref="SchedulerRoles.Operations" /> moves an operation to the other level, for both surfaces.
    /// </summary>
    [Test]
    public async Task AnOverrideMovesAnOperation()
    {
        Start(RoleAuthenticationExtensions.OperatorRolesPolicy, configureRoles: roles =>
        {
            roles.Operations["DeleteCalendar"] = SchedulerAccess.Operate;
            roles.Operations["interruptjob"] = SchedulerAccess.Administer;
        });

        using (new AssertionScope())
        {
            (await Send(SchedulerRoutes.DeleteCalendar, RoleAuthenticationExtensions.Operator)).Should().NotBe(HttpStatusCode.Forbidden,
                "the override lets operators maintain calendars");
            (await Send(SchedulerRoutes.InterruptJob, RoleAuthenticationExtensions.Operator)).Should().Be(HttpStatusCode.Forbidden,
                "the override keeps interrupting to administrators, and its name is read ignoring case");
            (await Send(SchedulerRoutes.InterruptJob, RoleAuthenticationExtensions.Administrator)).Should().NotBe(HttpStatusCode.Forbidden);
        }
    }

    /// <summary>
    /// <see cref="QuartzHttpApiOptions.ReadOnly" /> stays the ceiling over every role.
    /// </summary>
    [Test]
    public async Task ReadOnlyIsTheCeilingOverEveryRole()
    {
        Start(RoleAuthenticationExtensions.OperatorRolesPolicy, readOnly: true);

        using HttpClient client = ClientFor(RoleAuthenticationExtensions.Administrator);
        using HttpResponseMessage response = await client.PostAsync(Scheduler + "/pause-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ForbiddenException.ReadOnlyDetail);
    }

    /// <summary>
    /// The routes swept: every route of the table but the event stream, which answers a caller it lets
    /// through with a stream that does not end.
    /// </summary>
    private static IEnumerable<WireRoute> SweptRoutes() => SchedulerRoutes.All.Where(route => route != SchedulerRoutes.StreamEvents);

    private async Task<HttpStatusCode> Send(WireRoute route, string? role)
    {
        string[] values = route.Parameters.Select(ValueOf).ToArray();
        using HttpRequestMessage request = new(new HttpMethod(route.Method), route.For(values).Path);
        if (route.Method != "GET")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using HttpClient client = ClientFor(role);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;

        static string ValueOf(string parameter) => parameter switch
        {
            "schedulerName" => TestData.SchedulerName,
            "jobGroup" or "triggerGroup" => "reports",
            "jobName" or "triggerName" => "nightly",
            "calendarName" => "holidays",
            _ => "1",
        };
    }

    private async Task<HttpStatusCode> Post(string url, string role, string? body)
    {
        using HttpClient client = ClientFor(role);
        using StringContent? content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(url, content);
        return response.StatusCode;
    }

    private async Task<List<string>> ListedFor(string role)
    {
        using HttpClient client = ClientFor(role);
        string body = await client.GetStringAsync("schedulers");
        SchedulerHeaderDto[] listed = JsonSerializer.Deserialize<SchedulerHeaderDto[]>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(new SystemTextJsonSerializerRegistry()))!;
        return listed.Select(header => header.Name).ToList();
    }

    private HttpClient ClientFor(string? role)
    {
        HttpClient client = application.CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(RoleAuthenticationHandler.RoleHeaderName, role);
        }

        return client;
    }

    private void Start(string? policy, bool readOnly = false, Action<SchedulerRoles>? configureRoles = null)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        application = root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.AddRoleAuthorization(configureRoles);
                services.AddQuartzHttpApi(options =>
                {
                    options.OperationAuthorizationPolicy = policy;
                    options.ReadOnly = readOnly;
                });
            });
        });
        factories.Add(application);

        scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(TestData.SchedulerName);

        ISchedulerRepository repository = application.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(scheduler);
    }
}
