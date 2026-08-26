using System.Net;
using System.Text;
using System.Text.Json;

using AwesomeAssertions.Execution;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What <see cref="QuartzHttpApiOptions.SchedulerAuthorizationPolicy" /> does to the API: every route
/// that names a scheduler is authorized against that scheduler, and the listing is filtered to the
/// schedulers the caller may act on.
/// </summary>
/// <remarks>
/// Two tenants in one process, reached over one set of endpoints — the arrangement the option exists for.
/// The policy is a real one and the handler is a real
/// <c>AuthorizationHandler&lt;SchedulerOwnerRequirement, SchedulerResource&gt;</c>, because the promise
/// being tested is that an application writes one of those and Quartz does the rest.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerAuthorizationEndpointTest
{
    private const string JobsRoute = "schedulers/globex/jobs";

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly List<IScheduler> schedulers = [];

    private WebApplicationFactory<Program> application = null!;

    [SetUp]
    public async Task SetUp()
    {
        application = CreateApplication(policyName: TenantAuthenticationExtensions.SchedulerOwnerPolicy);
        await CreateSchedulers("acme", "globex");
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown();
        }

        schedulers.Clear();

        // Named as well as enumerated: it is in the list below, and disposing twice is a no-op, but the
        // analyzer reads the field rather than the list.
        await application.DisposeAsync();

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    /// <summary>
    /// One route from each endpoint group, read and written, for a scheduler that is not the caller's.
    /// </summary>
    /// <remarks>
    /// Every one of them resolves its scheduler from the same <c>{schedulerName}</c> segment, so the check
    /// belongs to the route rather than to any handler — and that is what this asserts by sampling all
    /// four groups rather than testing one endpoint.
    /// </remarks>
    [Test]
    public async Task EveryRouteOfAForeignSchedulerAnswersForbidden()
    {
        using HttpClient client = ClientFor("acme");

        using (new AssertionScope())
        {
            await Row(client, HttpMethod.Get, "schedulers/globex", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Get, "schedulers/globex/context", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Post, "schedulers/globex/pause-all", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Get, JobsRoute, HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Post, "schedulers/globex/jobs/group/name/pause", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Get, "schedulers/globex/triggers", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Post, "schedulers/globex/triggers/group/name/pause", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Get, "schedulers/globex/calendars", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Delete, "schedulers/globex/calendars/holidays", HttpStatusCode.Forbidden);
        }
    }

    /// <summary>
    /// The same routes on the caller's own scheduler answer as they always did.
    /// </summary>
    [Test]
    public async Task TheCallersOwnSchedulerAnswersAsItAlwaysDid()
    {
        using HttpClient client = ClientFor("acme");

        using (new AssertionScope())
        {
            await Row(client, HttpMethod.Get, "schedulers/acme", HttpStatusCode.OK);
            await Row(client, HttpMethod.Get, "schedulers/acme/context", HttpStatusCode.OK);
            await Row(client, HttpMethod.Post, "schedulers/acme/pause-all", HttpStatusCode.OK);
            await Row(client, HttpMethod.Get, "schedulers/acme/jobs", HttpStatusCode.OK);
            await Row(client, HttpMethod.Post, "schedulers/acme/jobs/group/name/pause", HttpStatusCode.OK);
            await Row(client, HttpMethod.Get, "schedulers/acme/triggers", HttpStatusCode.OK);
            await Row(client, HttpMethod.Get, "schedulers/acme/calendars", HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// A refusal comes before the scheduler is looked up, so a caller cannot use the difference between
    /// <c>403</c> and <c>404</c> to discover which tenants exist. A <c>404</c> is only ever the answer to
    /// a name the caller was allowed to ask about.
    /// </summary>
    [Test]
    public async Task AnUnknownSchedulerIsForbiddenRatherThanNotFound()
    {
        using HttpClient client = ClientFor("acme");

        using (new AssertionScope())
        {
            await Row(client, HttpMethod.Get, "schedulers/no-such-scheduler", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Get, "schedulers/globex", HttpStatusCode.Forbidden);
            await Row(client, HttpMethod.Get, "schedulers/acme/jobs/group/no-such-job", HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// The listing names no scheduler, so it filters its own answer: a tenant is told about its own
    /// schedulers and learns nothing about the others, not even that they are there.
    /// </summary>
    [Test]
    public async Task TheSchedulerListingCarriesOnlyTheCallersSchedulers()
    {
        using HttpClient acme = ClientFor("acme");
        using HttpClient globex = ClientFor("globex");

        SchedulerHeaderDto[] acmeSees = await ReadSchedulers(acme);
        SchedulerHeaderDto[] globexSees = await ReadSchedulers(globex);

        acmeSees.Select(x => x.Name).Should().Equal(["acme"],
            "the default scheduler the test application also registers is not this tenant's either");
        globexSees.Select(x => x.Name).Should().Equal(["globex"]);
    }

    /// <summary>
    /// The refusal on the wire: RFC 7807 problem details, like every other error the API produces, and
    /// without <c>Quartz-ExceptionType</c> — no exception was raised, a policy said no.
    /// </summary>
    [Test]
    public async Task ForbiddenProblemDetailsBody()
    {
        using HttpClient client = ClientFor("acme");

        using HttpResponseMessage response = await client.GetAsync(JobsRoute);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string body = await response.Content.ReadAsStringAsync();

        await VerifyJson(body)
            .UseStrictJson()
            .UseDirectory("../Verify")
            .UseFileName("SchedulerAuthorizationEndpointTest_ForbiddenProblemDetailsBody")
            .DisableRequireUniquePrefix();
    }

    /// <summary>
    /// Without the option the API is what it was: one policy for the whole surface, or none, and every
    /// scheduler reachable by whoever got through it.
    /// </summary>
    [Test]
    public async Task WithoutTheOptionEverySchedulerIsReachable()
    {
        WebApplicationFactory<Program> unauthorized = CreateApplication(policyName: null);
        await CreateSchedulers(unauthorized, "zeta");

        using HttpClient client = ClientFor(unauthorized, "acme");

        using (new AssertionScope())
        {
            await Row(client, HttpMethod.Get, "schedulers/zeta", HttpStatusCode.OK);
            await Row(client, HttpMethod.Get, "schedulers/zeta/jobs", HttpStatusCode.OK);
        }

        SchedulerHeaderDto[] listed = await ReadSchedulers(client);
        listed.Select(x => x.Name).Should().Contain("zeta",
            "with no per-scheduler policy the listing is unfiltered, whoever is asking");
    }

    private static async Task Row(HttpClient client, HttpMethod method, string url, HttpStatusCode expected)
    {
        using HttpRequestMessage request = new(method, url);
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("", Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(expected, $"{method} {url} answers {expected:D}, body was {body}");
    }

    private HttpClient ClientFor(string tenant) => ClientFor(application, tenant);

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string tenant)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TenantAuthenticationHandler.TenantHeaderName, tenant);
        return client;
    }

    private static async Task<SchedulerHeaderDto[]> ReadSchedulers(HttpClient client)
    {
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        using HttpResponseMessage response = await client.GetAsync("schedulers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SchedulerHeaderDto[]>(body, serializerOptions)!;
    }

    private Task CreateSchedulers(params string[] names) => CreateSchedulers(application, names);

    /// <summary>
    /// Builds each tenant's scheduler, because a registration nothing has built answers <c>404</c> on
    /// every route and the status codes are the whole subject here.
    /// </summary>
    private async Task CreateSchedulers(WebApplicationFactory<Program> factory, params string[] names)
    {
        foreach (string name in names)
        {
            IScheduler scheduler = await factory.Services
                .GetRequiredKeyedService<ISchedulerFactory>(name)
                .GetScheduler();

            schedulers.Add(scheduler);
        }
    }

    private WebApplicationFactory<Program> CreateApplication(string? policyName)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddTenantAuthorization();
            services.AddQuartz("acme", q => q.UseInMemoryStore());
            services.AddQuartz("globex", q => q.UseInMemoryStore());
            services.AddQuartz("zeta", q => q.UseInMemoryStore());
            services.AddQuartzHttpApi(options => options.SchedulerAuthorizationPolicy = policyName);
        }));

        factories.Add(configured);

        return configured;
    }
}
