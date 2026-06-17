using System.Net;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Dashboard.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class DashboardEndpointsTest
{
    private static WebApplication CreateApp(
        Action<QuartzDashboardOptions>? configureDashboard = null,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartzDashboard(configureDashboard);
        configureBuilder?.Invoke(builder);
        return builder.Build();
    }

    private static List<RouteEndpoint> GetRouteEndpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder) app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static RouteEndpoint GetStaticAssetEndpoint(WebApplication app)
    {
        return GetRouteEndpoints(app).Single(e => e.RoutePattern.RawText == "_content/Quartz.Dashboard/{**path}");
    }

    [Test]
    public async Task StaticAssetEndpointShouldAllowAnonymousWithoutPolicy()
    {
        await using WebApplication app = CreateApp();
        app.MapQuartzDashboard();

        RouteEndpoint assets = GetStaticAssetEndpoint(app);
        assets.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        assets.Metadata.GetMetadata<IAuthorizeData>().Should().BeNull();
    }

    [Test]
    public async Task StaticAssetEndpointShouldRequireConfiguredPolicy()
    {
        await using WebApplication app = CreateApp(options => options.AuthorizationPolicy = "dashboard-policy");
        app.MapQuartzDashboard();

        RouteEndpoint assets = GetStaticAssetEndpoint(app);
        assets.Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
        assets.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
    }

    [Test]
    public async Task StandalonePolicyShouldCoverPagesHubAndFrameworkEndpoints()
    {
        await using WebApplication app = CreateApp(options => options.AuthorizationPolicy = "dashboard-policy");
        app.MapQuartzDashboard();

        List<RouteEndpoint> endpoints = GetRouteEndpoints(app);

        RouteEndpoint dashboardPage = endpoints.First(e => e.RoutePattern.RawText == "/quartz");
        dashboardPage.Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");

        RouteEndpoint hub = endpoints.First(e => e.RoutePattern.RawText == "/quartz/hub");
        hub.Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");

        // Endpoints not backed by a page component (the /_blazor circuit endpoints) must carry
        // the policy too so that fail-closed FallbackPolicy configurations keep working (#3097)
        List<RouteEndpoint> frameworkEndpoints = endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith("/_blazor", StringComparison.Ordinal) == true)
            .ToList();
        frameworkEndpoints.Should().NotBeEmpty();
        frameworkEndpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<IAuthorizeData>() != null);
    }

    [Test]
    public async Task StandaloneWithoutPolicyShouldAllowAnonymousCircuitButProtectPages()
    {
        // #3117: without a dashboard policy the Blazor circuit (/_blazor) opts out of authorization
        // so it stays reachable under a fail-closed FallbackPolicy, while the dashboard pages and the
        // live-events hub carry no metadata of their own and remain governed by the host's policies
        // (so scheduler data is never silently exposed to anonymous users).
        await using WebApplication app = CreateApp();
        app.MapQuartzDashboard();

        List<RouteEndpoint> endpoints = GetRouteEndpoints(app);

        // the /_blazor circuit endpoints are marked anonymous
        List<RouteEndpoint> frameworkEndpoints = endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith("/_blazor", StringComparison.Ordinal) == true)
            .ToList();
        frameworkEndpoints.Should().NotBeEmpty();
        frameworkEndpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<IAllowAnonymous>() != null);

        // dashboard pages stay subject to the host fallback (neither anonymous nor explicitly authorized)
        RouteEndpoint dashboardPage = endpoints.First(e => e.RoutePattern.RawText == "/quartz");
        dashboardPage.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        dashboardPage.Metadata.GetMetadata<IAuthorizeData>().Should().BeNull();

        // the live-events hub also stays behind the host fallback (it carries scheduler data)
        RouteEndpoint hub = endpoints.First(e => e.RoutePattern.RawText == "/quartz/hub");
        hub.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        hub.Metadata.GetMetadata<IAuthorizeData>().Should().BeNull();
    }

    [Test]
    public async Task IntegratedPolicyShouldNotLeakToHostEndpoints()
    {
        // regression test for #3066
        await using WebApplication app = CreateApp(options => options.AuthorizationPolicy = "dashboard-policy");
        var blazor = app.MapRazorComponents<TestHostApp>().AddInteractiveServerRenderMode();
        app.MapQuartzDashboard(blazor);

        List<RouteEndpoint> endpoints = GetRouteEndpoints(app);

        RouteEndpoint hostPage = endpoints.First(e => e.RoutePattern.RawText == "/host-page");
        hostPage.Metadata.GetMetadata<IAuthorizeData>().Should().BeNull();

        RouteEndpoint dashboardPage = endpoints.First(e => e.RoutePattern.RawText == "/quartz");
        dashboardPage.Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
    }

    [Test]
    public async Task CustomDashboardPathShouldRewriteDashboardEndpoints()
    {
        await using WebApplication app = CreateApp(options => options.DashboardPath = "/ops/scheduler");
        app.MapQuartzDashboard();

        List<RouteEndpoint> endpoints = GetRouteEndpoints(app);
        List<string?> patterns = endpoints.Select(e => e.RoutePattern.RawText).ToList();

        patterns.Should().Contain("/ops/scheduler");
        patterns.Should().Contain("/ops/scheduler/jobs");
        patterns.Should().Contain("/ops/scheduler/jobs/{Group}/{Name}");
        patterns.Should().Contain("/ops/scheduler/hub");

        // no dashboard page endpoints are left under the default path
        patterns.Should().NotContain("/quartz");
        patterns.Where(p => p?.StartsWith("/quartz/", StringComparison.Ordinal) == true).Should().BeEmpty();

        // framework endpoints are not re-rooted
        patterns.Should().Contain(p => p != null && p.StartsWith("/_blazor", StringComparison.Ordinal));
    }

    [Test]
    public async Task CustomDashboardPathShouldThrowWhenIntegratingWithExistingBlazorApp()
    {
        await using WebApplication app = CreateApp(options => options.DashboardPath = "/ops/scheduler");
        var blazor = app.MapRazorComponents<TestHostApp>().AddInteractiveServerRenderMode();

        Action act = () => app.MapQuartzDashboard(blazor);
        act.Should().Throw<InvalidOperationException>().WithMessage("*DashboardPath*");
    }

    [Test]
    public async Task StaticAssetsShouldBeServedUnderFailClosedFallbackPolicy()
    {
        // end-to-end repro of #3097: a FallbackPolicy that denies endpoints without explicit
        // authorization metadata must not block dashboard static assets
        await using WebApplication app = CreateApp(
            configureBuilder: builder =>
            {
                builder.Services
                    .AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null);
                builder.Services.AddAuthorization(options =>
                {
                    options.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAssertion(_ => false)
                        .Build();
                });
                builder.Environment.WebRootFileProvider = new TestFileProvider(new Dictionary<string, byte[]>
                {
                    ["_content/Quartz.Dashboard/css/quartz-dashboard.css"] = "body { }"u8.ToArray(),
                });
            });

        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            using HttpResponseMessage cssResponse = await client.GetAsync("/_content/Quartz.Dashboard/css/quartz-dashboard.css");
            cssResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            cssResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/css");

            // dashboard pages remain subject to the fail-closed fallback policy
            using HttpResponseMessage pageResponse = await client.GetAsync("/quartz");
            pageResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
