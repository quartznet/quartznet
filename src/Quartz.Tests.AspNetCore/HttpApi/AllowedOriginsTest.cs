using System.Net;

using FakeItEasy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// <see cref="QuartzHttpApiOptions.AllowedOrigins" /> (task xvg6, #4218): a request a browser sent from a
/// page of another origin is refused, which is what stops a cross-site form from shutting a scheduler down
/// under cookie authentication. Off unless origins are listed.
/// </summary>
/// <remarks>
/// <c>POST …/shutdown</c> takes no body, so a plain <c>&lt;form method="post"&gt;</c> on any page reaches it
/// without a CORS preflight, carrying whatever cookie the browser holds for the API's site.
/// </remarks>
public sealed class AllowedOriginsTest
{
    private const string ShutdownUrl = "schedulers/" + TestData.SchedulerName + "/shutdown";

    private readonly RecordingLoggerProvider logs = new();
    private WebApplication? app;
    private IScheduler scheduler = null!;

    [TearDown]
    public async Task TearDown()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
            app = null;
        }

        logs.Clear();
    }

    [Test]
    public async Task ACrossSiteFormPostToShutdownIsRefusedWhenOriginsAreListed()
    {
        using HttpClient client = await Start("https://ops.example.com");

        using HttpResponseMessage response = await client.SendAsync(FormPost("https://evil.example"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("https://evil.example").And.Contain("not allowed");
        A.CallTo(() => scheduler.Shutdown(A<bool>._, A<CancellationToken>._)).MustNotHaveHappened();
        logs.WithEventId(9005).Should().ContainSingle("a refusal by a configured rule is the API's 9005, at Warning");
    }

    [Test]
    public async Task AListedOriginAndARequestWithNoOriginGoOn()
    {
        using HttpClient client = await Start("HTTPS://Ops.Example.com:443/");

        using HttpResponseMessage listed = await client.SendAsync(FormPost("https://ops.example.com"));
        using HttpResponseMessage script = await client.PostAsync(ShutdownUrl, content: null);

        listed.StatusCode.Should().Be(HttpStatusCode.OK, "an origin is compared as a browser writes it: no default port, no case");
        script.StatusCode.Should().Be(HttpStatusCode.OK, "a script, a pipeline or HttpScheduler sends no Origin, and is never affected");
    }

    [TestCase("null")]
    [TestCase("https://ops.example.com.evil.example")]
    [TestCase("http://ops.example.com")]
    [TestCase("https://ops.example.com:8443")]
    public async Task AnythingButAListedOriginIsRefused(string origin)
    {
        using HttpClient client = await Start("https://ops.example.com");

        using HttpResponseMessage response = await client.SendAsync(FormPost(origin));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "an opaque origin, another host, scheme or port is not the one listed");
    }

    /// <summary>
    /// The origin is refused before either policy is asked: the caller the page borrowed is not logged as
    /// having reached for an operation their role does not allow.
    /// </summary>
    [Test]
    public async Task ARefusedOriginIsAnsweredBeforeThePoliciesRun()
    {
        using HttpClient client = await Start(
            services => services.AddRoleAuthorization(),
            options => options.OperationAuthorizationPolicy = RoleAuthenticationExtensions.OperatorRolesPolicy,
            "https://ops.example.com");

        using HttpRequestMessage request = FormPost("https://evil.example");
        request.Headers.Add(RoleAuthenticationHandler.RoleHeaderName, RoleAuthenticationExtensions.Viewer);
        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        logs.WithEventId(9005).Should().ContainSingle("the origin refusal");
        logs.WithEventId(9009).Should().BeEmpty("a viewer may not shut down, but the operation policy was never asked");
    }

    [Test]
    public async Task WithoutTheOptionNothingChanges()
    {
        using HttpClient client = await Start();

        using HttpResponseMessage response = await client.SendAsync(FormPost("https://evil.example"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the check is opt-in: refusing Origin by default would break the single-page applications and CORS callers of the API");
        A.CallTo(() => scheduler.Shutdown(A<bool>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [TestCase("ops.example.com")]
    [TestCase("https://ops.example.com/quartz")]
    [TestCase("https://ops.example.com/?x=1")]
    [TestCase("ftp://ops.example.com")]
    [TestCase(" ")]
    public void AnEntryThatIsNotAnOriginIsRefusedAtStartup(string origin)
    {
        Func<Task> start = () => Start(origin);

        start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*AllowedOrigins*").GetAwaiter().GetResult();
    }

    private Task<HttpClient> Start(params string[] allowedOrigins) => Start(null, null, allowedOrigins);

    private async Task<HttpClient> Start(Action<IServiceCollection>? services, Action<QuartzHttpApiOptions>? configure, params string[] allowedOrigins)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddProvider(logs);
        builder.Services.AddQuartz();
        services?.Invoke(builder.Services);
        builder.Services.AddQuartzHttpApi(options =>
        {
            options.AllowedOrigins.AddRange(allowedOrigins);
            configure?.Invoke(options);
        });

        app = builder.Build();

        scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(TestData.SchedulerName);
        app.Services.GetRequiredService<ISchedulerRepository>().Bind(scheduler);

        app.MapQuartzHttpApi("/").AllowAnonymous();
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static HttpRequestMessage FormPost(string origin)
    {
        HttpRequestMessage request = new(HttpMethod.Post, ShutdownUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["waitForJobsToComplete"] = "false" })
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }
}
