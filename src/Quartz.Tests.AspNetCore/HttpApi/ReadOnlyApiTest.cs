using System.Net;
using System.Text;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What <see cref="QuartzHttpApiOptions.ReadOnly" /> does: every route that changes something answers
/// <c>403</c>, and every read answers as it always did.
/// </summary>
/// <remarks>
/// The distinction is per route rather than per verb, which is the whole reason the marker exists: the
/// two bulk fetches are reads that take a body of keys, and a rule written as "refuse everything that is
/// not a GET" would have taken them away from a dashboard fronting the API.
/// </remarks>
public sealed class ReadOnlyApiTest
{
    private readonly List<WebApplicationFactory<Program>> factories = [];

    [TearDown]
    public async Task TearDown()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    [Test]
    public async Task EveryMutatingRouteIsRefused()
    {
        using HttpClient client = ApiWith(readOnly: true);

        using (new AssertionScope())
        {
            await Row(HttpMethod.Post, $"{SchedulerUrl}/start", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/shutdown", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/pause-all", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/clear", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/pause", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/group/existing/interrupt", HttpStatusCode.Forbidden,
                "interrupting a job changes what the scheduler is doing, which is what read-only is about");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/triggers/group/existing/reset-from-error-state", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/jobs/group/existing", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/calendars/holidays", HttpStatusCode.Forbidden);
            await Row(HttpMethod.Delete, $"{SchedulerUrl}/execution-limits", HttpStatusCode.Forbidden);
        }
    }

    [Test]
    public async Task EveryReadIsServed()
    {
        using HttpClient client = ApiWith(readOnly: true);

        using (new AssertionScope())
        {
            await Row(HttpMethod.Get, "schedulers", HttpStatusCode.OK);
            await Row(HttpMethod.Get, $"{SchedulerUrl}/jobs", HttpStatusCode.OK);
            await Row(HttpMethod.Get, $"{SchedulerUrl}/triggers", HttpStatusCode.OK);
            await Row(HttpMethod.Post, $"{SchedulerUrl}/jobs/fetch", HttpStatusCode.OK,
                requestJson: """[{"name":"existing","group":"group"}]""",
                because: "the bulk fetch is a read that takes a body of keys, so a rule written per verb would have refused it");
            await Row(HttpMethod.Post, $"{SchedulerUrl}/triggers/fetch", HttpStatusCode.OK,
                requestJson: """[{"name":"existing","group":"group"}]""");
        }
    }

    [Test]
    public async Task NothingIsRefusedWhileTheApiIsWritable()
    {
        using HttpClient client = ApiWith(readOnly: false);

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the default is what every earlier release did, and a mutation is served");
    }

    /// <summary>
    /// The refusal is decided by the route and the options alone, so it is answered before the handler
    /// reads a body or looks a scheduler up.
    /// </summary>
    /// <remarks>
    /// A scheduler nobody has bound would otherwise be a <c>404</c>, and a body a mutating route requires
    /// would otherwise be a <c>400</c>. Both would leak: a caller could map the schedulers of a process
    /// by which refusal came back.
    /// </remarks>
    [Test]
    public async Task TheRefusalComesBeforeTheSchedulerIsLookedUp()
    {
        using HttpClient client = ApiWith(readOnly: true);

        using HttpResponseMessage response = await client.PostAsync("schedulers/no-such-scheduler/pause-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "nothing about the request decides this, so there is no reason to look a scheduler up first");
    }

    /// <summary>
    /// The refusal carries no <c>Quartz-ExceptionType</c>, as the API's other refusals do not: nothing
    /// failed, a rule the operator configured said no.
    /// </summary>
    [Test]
    public async Task TheRefusalNamesNoExceptionType()
    {
        using HttpClient client = ApiWith(readOnly: true);

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("Quartz-ExceptionType").Should().BeFalse(
            "a client must not be invited to catch a refusal as though it were a scheduler fault");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ForbiddenException.ReadOnlyDetail);
    }

    /// <summary>
    /// Every route that is not a <c>GET</c> is marked as a mutation, or is one of the two reads that
    /// take a body.
    /// </summary>
    /// <remarks>
    /// The marker is written at the <c>Map*</c> call, so this is what makes forgetting it a failing
    /// build rather than a route that stays writable while the API is configured read-only.
    /// </remarks>
    [Test]
    public async Task EveryNonGetRouteIsMarkedAsAMutationOrIsOneOfTheTwoFetches()
    {
        await using WebApplication app = ReadOnlyApp();
        app.MapQuartzHttpApi("/quartz-api");

        List<string> unmarked = [];
        foreach (RouteEndpoint endpoint in Endpoints(app))
        {
            IReadOnlyList<string> methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (methods.Count == 0 || methods.All(method => method == HttpMethods.Get))
            {
                continue;
            }

            if (endpoint.Metadata.GetMetadata<QuartzMutationMetadata>() is not null)
            {
                continue;
            }

            unmarked.Add($"{string.Join(",", methods)} {endpoint.RoutePattern.RawText}");
        }

        unmarked.Should().BeEquivalentTo(
            ["POST /quartz-api/schedulers/{schedulerName}/jobs/fetch", "POST /quartz-api/schedulers/{schedulerName}/triggers/fetch"],
            "the two bulk fetches are the only routes that take a body without changing anything - every other "
            + "non-GET route has to carry the marker, or QuartzHttpApiOptions.ReadOnly leaves it writable");
    }

    /// <summary>
    /// The <c>403</c> is described on the mutating routes only while the API is actually read-only.
    /// </summary>
    /// <remarks>
    /// The rule <c>ProducesJobTypeRefusal</c> follows: a described response that cannot occur is a
    /// described response a client writes a branch for.
    /// </remarks>
    [Test]
    public async Task TheRefusalIsDescribedOnlyWhenItCanHappen()
    {
        await using WebApplication readOnly = ReadOnlyApp();
        readOnly.MapQuartzHttpApi("/quartz-api");

        await using WebApplication writable = WritableApp();
        writable.MapQuartzHttpApi("/quartz-api");

        DescribesForbidden(readOnly, "/quartz-api/schedulers/{schedulerName}/pause-all").Should().BeTrue();
        DescribesForbidden(writable, "/quartz-api/schedulers/{schedulerName}/pause-all").Should().BeFalse(
            "an API that refuses nothing must not describe a refusal");
    }

    private const string SchedulerUrl = "schedulers/" + TestData.SchedulerName;

    private HttpClient client = null!;

    private HttpClient ApiWith(bool readOnly)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddQuartzHttpApi(options => options.ReadOnly = readOnly)));
        factories.Add(configured);

        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => fake.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([], HasMore: false));
        A.CallTo(() => fake.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));

        client = configured.CreateClient();

        ISchedulerRepository repository = configured.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
        return client;
    }

    private async Task Row(
        HttpMethod method,
        string url,
        HttpStatusCode expected,
        string? because = null,
        string? requestJson = null)
    {
        using HttpRequestMessage request = new(method, url);
        if (requestJson is not null)
        {
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(expected,
            because ?? $"{method} {url} answers {expected:D} while the API is read-only, body was {body}");
    }

    private static WebApplication ReadOnlyApp() => App(options => options.ReadOnly = true);

    private static WebApplication WritableApp() => App(configure: null);

    private static WebApplication App(Action<QuartzHttpApiOptions>? configure)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartz();
        builder.Services.AddQuartzHttpApi(configure);
        return builder.Build();
    }

    private static List<RouteEndpoint> Endpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder) app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static bool DescribesForbidden(WebApplication app, string pattern)
    {
        RouteEndpoint endpoint = Endpoints(app).Single(x => x.RoutePattern.RawText == pattern);
        return endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>()
            .Any(metadata => metadata.StatusCode == StatusCodes.Status403Forbidden);
    }
}
