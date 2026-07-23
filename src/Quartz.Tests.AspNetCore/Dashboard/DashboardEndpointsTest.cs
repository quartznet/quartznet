using System.Net;


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

        // the Blazor circuit endpoints move under the dashboard path so a reverse proxy that
        // forwards only the dashboard prefix can reach them (#3134)
        patterns.Where(p => p?.StartsWith("/_blazor", StringComparison.Ordinal) == true).Should().BeEmpty();
        patterns.Should().Contain(p => p != null && p.StartsWith("/ops/scheduler/_blazor", StringComparison.Ordinal));
        patterns.Should().Contain(p => p != null && p.StartsWith("/ops/scheduler/_blazor", StringComparison.Ordinal) && p.EndsWith("negotiate", StringComparison.Ordinal));

        // dashboard-owned framework script, opaque-redirect and static asset endpoints exist under
        // the dashboard path; the root-level asset endpoint stays for backwards compatibility
        patterns.Should().Contain("/ops/scheduler/_framework/blazor.web.js");
        patterns.Should().Contain("/ops/scheduler/_framework/opaque-redirect");
        patterns.Should().Contain("/ops/scheduler/_content/Quartz.Dashboard/{**path}");
        patterns.Should().Contain("_content/Quartz.Dashboard/{**path}");
    }

    [Test]
    public async Task CustomDashboardPathPolicyShouldCoverRerootedEndpoints()
    {
        await using WebApplication app = CreateApp(options =>
        {
            options.DashboardPath = "/ops/scheduler";
            options.AuthorizationPolicy = "dashboard-policy";
        });
        app.MapQuartzDashboard();

        List<RouteEndpoint> endpoints = GetRouteEndpoints(app);

        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/hub")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");

        List<RouteEndpoint> circuitEndpoints = endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith("/ops/scheduler/_blazor", StringComparison.Ordinal) == true)
            .ToList();
        circuitEndpoints.Should().NotBeEmpty();
        circuitEndpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<IAuthorizeData>() != null);

        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/_framework/blazor.web.js")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/_framework/opaque-redirect")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/_content/Quartz.Dashboard/{**path}")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
    }

    [Test]
    public async Task CustomDashboardPathWithoutPolicyShouldAllowAnonymousPlumbingButProtectPages()
    {
        // #3117 semantics must hold for the re-rooted endpoints too: the Blazor plumbing opts out of
        // authorization so it stays reachable under a fail-closed FallbackPolicy, while the pages and
        // the live-events hub remain governed by the host's policies
        await using WebApplication app = CreateApp(options => options.DashboardPath = "/ops/scheduler");
        app.MapQuartzDashboard();

        List<RouteEndpoint> endpoints = GetRouteEndpoints(app);

        List<RouteEndpoint> circuitEndpoints = endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith("/ops/scheduler/_blazor", StringComparison.Ordinal) == true)
            .ToList();
        circuitEndpoints.Should().NotBeEmpty();
        circuitEndpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<IAllowAnonymous>() != null);

        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/_framework/blazor.web.js")
            .Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/_framework/opaque-redirect")
            .Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/_content/Quartz.Dashboard/{**path}")
            .Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();

        RouteEndpoint dashboardPage = endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler");
        dashboardPage.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        dashboardPage.Metadata.GetMetadata<IAuthorizeData>().Should().BeNull();

        RouteEndpoint hub = endpoints.First(e => e.RoutePattern.RawText == "/ops/scheduler/hub");
        hub.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        hub.Metadata.GetMetadata<IAuthorizeData>().Should().BeNull();
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

    [Test]
    public async Task CustomDashboardPathShouldServeShellWithDashboardRootedBaseHref()
    {
        // end-to-end repro of #3134: with a custom path the shell must root the document at the
        // dashboard itself so the framework script, stylesheet and circuit resolve under the prefix
        await using WebApplication app = CreateApp(
            options => options.DashboardPath = "/my-api/quartz",
            builder => builder.Services.AddQuartz());

        app.UseAntiforgery();
        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            using HttpResponseMessage response = await client.GetAsync("/my-api/quartz");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            string body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("<base href=\"/my-api/quartz/\"");
            body.Should().Contain("_framework/blazor.web.js");

            // route matching tolerates the trailing-slash form of the dashboard root
            using HttpResponseMessage slashResponse = await client.GetAsync("/my-api/quartz/");
            slashResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Test]
    public async Task DefaultDashboardPathShouldKeepPathBaseRootedBaseHref()
    {
        // regression guard: default-path behavior must stay unchanged by the #3134 fix
        await using WebApplication app = CreateApp(
            configureBuilder: builder => builder.Services.AddQuartz());

        app.UseAntiforgery();
        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            using HttpResponseMessage response = await client.GetAsync("/quartz");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            string body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("<base href=\"/\"");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Test]
    public async Task CustomDashboardPathShouldHonorApplicationPathBase()
    {
        await using WebApplication app = CreateApp(
            options => options.DashboardPath = "/my-api/quartz",
            builder => builder.Services.AddQuartz());

        // UsePathBase only affects routing when UseRouting is (re-)registered after it
        app.UsePathBase("/app");
        app.UseRouting();
        app.UseAntiforgery();
        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            using HttpResponseMessage response = await client.GetAsync("/app/my-api/quartz");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            string body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("<base href=\"/app/my-api/quartz/\"");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Test]
    public async Task CustomDashboardPathShouldServeFrameworkScriptAndStaticAssets()
    {
        await using WebApplication app = CreateApp(
            options => options.DashboardPath = "/my-api/quartz",
            builder => builder.Environment.WebRootFileProvider = new TestFileProvider(new Dictionary<string, byte[]>
            {
                // On .NET 10+ the framework script is a static web asset exposed through the web root
                // rather than an embedded manifest, so a host serves it from the WebRootFileProvider
                // (see the RequiresAspNetWebAssets note in the dashboard docs).
                ["_framework/blazor.web.js"] = "// blazor.web.js"u8.ToArray(),
                ["_content/Quartz.Dashboard/css/quartz-dashboard.css"] = "body { }"u8.ToArray(),
            }));

        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            // the framework script is mirrored under the dashboard path and served from the web root
            // (a static web asset on .NET 10+, the embedded manifest on .NET 8/9)
            using HttpResponseMessage scriptResponse = await client.GetAsync("/my-api/quartz/_framework/blazor.web.js");
            scriptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            scriptResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
            (await scriptResponse.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();

            using HttpResponseMessage cssResponse = await client.GetAsync("/my-api/quartz/_content/Quartz.Dashboard/css/quartz-dashboard.css");
            cssResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            cssResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/css");

            // root-level endpoints stay reachable for backwards compatibility
            using HttpResponseMessage rootCssResponse = await client.GetAsync("/_content/Quartz.Dashboard/css/quartz-dashboard.css");
            rootCssResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Test]
    public async Task CustomDashboardPathScriptShouldSupportHeadAndConditionalRequests()
    {
        await using WebApplication app = CreateApp(
            options => options.DashboardPath = "/my-api/quartz",
            builder => builder.Environment.WebRootFileProvider = new TestFileProvider(new Dictionary<string, byte[]>
            {
                // the framework script is a static web asset served from the web root on .NET 10+
                ["_framework/blazor.web.js"] = "// blazor.web.js"u8.ToArray(),
            }));
        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();
            const string scriptUrl = "/my-api/quartz/_framework/blazor.web.js";

            // HEAD returns the headers without a body (probes, link checkers)
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, scriptUrl);
            using HttpResponseMessage headResponse = await client.SendAsync(headRequest);
            headResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            (await headResponse.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

            using HttpResponseMessage getResponse = await client.GetAsync(scriptUrl);
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            string etag = getResponse.Headers.ETag!.ToString();

            // revalidation with the served validator returns 304 without a body
            using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, scriptUrl);
            conditionalRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using HttpResponseMessage notModified = await client.SendAsync(conditionalRequest);
            notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);

            // RFC 9110: "*" matches any current representation
            using var anyRequest = new HttpRequestMessage(HttpMethod.Get, scriptUrl);
            anyRequest.Headers.TryAddWithoutValidation("If-None-Match", "*");
            using HttpResponseMessage anyNotModified = await client.SendAsync(anyRequest);
            anyNotModified.StatusCode.Should().Be(HttpStatusCode.NotModified);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [TestCase("/tenant{env}")]
    [TestCase("/ops/../quartz")]
    [TestCase("/ops?x=1")]
    [TestCase("/ops#frag")]
    [TestCase("/ops//scheduler")]
    public async Task InvalidDashboardPathShouldFailValidation(string dashboardPath)
    {
        await using WebApplication app = CreateApp(options => options.DashboardPath = dashboardPath);

        Action act = () => app.MapQuartzDashboard();
        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().WithMessage("*DashboardPath*");
    }

    [Test]
    public async Task CustomDashboardPathShouldForwardOpaqueRedirectToRootEndpoint()
    {
        // the framework emits enhanced-navigation redirects as URLs relative to the document base,
        // so with a dashboard-rooted <base href> the browser requests them under the dashboard path
        await using WebApplication app = CreateApp(options => options.DashboardPath = "/my-api/quartz");
        app.MapQuartzDashboard();

        GetRouteEndpoints(app).Select(e => e.RoutePattern.RawText)
            .Should().Contain("/my-api/quartz/_framework/opaque-redirect");

        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            // the framework endpoint rejects the missing protected payload (400); a 404 would mean
            // the mirror found nothing to forward to, and a throw from the mirror itself would
            // surface as a test error rather than passing vacuously
            using HttpResponseMessage response = await client.GetAsync("/my-api/quartz/_framework/opaque-redirect");
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Test]
    public async Task CustomDashboardPathShouldServeBlazorNegotiate()
    {
        await using WebApplication app = CreateApp(options => options.DashboardPath = "/my-api/quartz");
        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            // proves the re-rooted SignalR circuit endpoints still dispatch correctly
            using HttpResponseMessage response = await client.PostAsync("/my-api/quartz/_blazor/negotiate?negotiateVersion=1", content: null);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            string body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("availableTransports");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Test]
    public async Task CustomDashboardPathAssetsShouldBeServedUnderFailClosedFallbackPolicy()
    {
        // the #3097/#3117 guarantees must hold for the endpoints mirrored under a custom path
        await using WebApplication app = CreateApp(
            options => options.DashboardPath = "/my-api/quartz",
            builder =>
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
                    // served from the web root as static web assets on .NET 10+
                    ["_framework/blazor.web.js"] = "// blazor.web.js"u8.ToArray(),
                    ["_content/Quartz.Dashboard/css/quartz-dashboard.css"] = "body { }"u8.ToArray(),
                });
            });

        app.MapQuartzDashboard();
        await app.StartAsync();
        try
        {
            using System.Net.Http.HttpClient client = app.GetTestClient();

            using HttpResponseMessage cssResponse = await client.GetAsync("/my-api/quartz/_content/Quartz.Dashboard/css/quartz-dashboard.css");
            cssResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using HttpResponseMessage scriptResponse = await client.GetAsync("/my-api/quartz/_framework/blazor.web.js");
            scriptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // dashboard pages remain subject to the fail-closed fallback policy
            using HttpResponseMessage pageResponse = await client.GetAsync("/my-api/quartz");
            pageResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
