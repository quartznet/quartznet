using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Dashboard.Support;
using Quartz.Tests.AspNetCore.HttpApi;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The same refusal for the second HTTP surface: the dashboard authenticates nothing of its own, its
/// pages start, stand by, shut down and delete, and <c>ReadOnly</c> is <see langword="false" /> by
/// default — so a mapping that says nothing about authorization is refused before the listener is bound.
/// </summary>
/// <remarks>
/// The deliberately anonymous endpoints — the static assets, and the Blazor circuit when no policy is
/// set — already say what they mean and are not what the guard is about. Its subject is the pages and
/// the live-events hub, which are what carry scheduler data.
/// </remarks>
public sealed class QuartzDashboardAuthorizationGuardTest
{
    [Test]
    public async Task ADashboardMappedWithNothingSaidRefusesToStart()
    {
        await using WebApplication app = CreateDashboard();
        app.MapQuartzDashboard();

        Func<Task> act = async () => await app.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*MapQuartzDashboard().RequireAuthorization()*")
            .WithMessage("*AuthorizationPolicy*")
            .WithMessage("*MapQuartzDashboard().AllowAnonymous()*")
            .WithMessage("*/quartz*", "the pages and the hub are what it is refusing to serve");
    }

    [Test]
    public async Task ADashboardTheCallerAuthorizedStarts()
    {
        await using WebApplication app = CreateDashboard();
        app.MapQuartzDashboard().RequireAuthorization();

        await app.StartAsync();
        await app.StopAsync();
    }

    /// <summary>
    /// The hub streams the same scheduler data the pages render and is reachable on its own, so a
    /// <c>RequireAuthorization()</c> that covered only the pages would leave the interesting half open.
    /// </summary>
    [Test]
    public async Task AuthorizingTheDashboardCoversItsHubAsWellAsItsPages()
    {
        await using WebApplication app = CreateDashboard();
        app.MapQuartzDashboard().RequireAuthorization("dashboard-policy");

        List<RouteEndpoint> endpoints = RouteEndpoints(app);

        endpoints.First(e => e.RoutePattern.RawText == "/quartz/hub")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
        endpoints.First(e => e.RoutePattern.RawText == "/quartz")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
    }

    [Test]
    public async Task ADashboardTheCallerMadeAnonymousOnPurposeStarts()
    {
        await using WebApplication app = CreateDashboard();
        app.MapQuartzDashboard().AllowAnonymous();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task ADashboardUnderAFallbackPolicyStarts()
    {
        await using WebApplication app = CreateDashboard(QuartzHttpApiAuthorizationGuardTest.FailClosed);
        app.MapQuartzDashboard();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task ADashboardWithItsOwnPolicyStarts()
    {
        await using WebApplication app = CreateDashboard(
            configureDashboard: options => options.AuthorizationPolicy = "dashboard-policy");
        app.MapQuartzDashboard();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task ADashboardRegisteredAndNeverMappedStarts()
    {
        await using WebApplication app = CreateDashboard();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task ADashboardIntegratedIntoAHostsBlazorRootIsCoveredWithoutCoveringTheHostsPages()
    {
        await using WebApplication app = CreateDashboard();
        RazorComponentsEndpointConventionBuilder blazor = app.MapRazorComponents<TestHostApp>()
            .AddInteractiveServerRenderMode();
        app.MapQuartzDashboard(blazor).RequireAuthorization("dashboard-policy");

        List<RouteEndpoint> endpoints = RouteEndpoints(app);

        endpoints.First(e => e.RoutePattern.RawText == "/quartz")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
        endpoints.First(e => e.RoutePattern.RawText == "/quartz/hub")
            .Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("dashboard-policy");
        endpoints.First(e => e.RoutePattern.RawText == "/host-page")
            .Metadata.GetMetadata<IAuthorizeData>().Should().BeNull(
                "what MapQuartzDashboard hands back is the dashboard, not the builder it was given (#3066)");
    }

    /// <summary>
    /// One process, both surfaces, nothing said about either: the refusal names them both rather than
    /// stopping at whichever the guard reached first.
    /// </summary>
    [Test]
    public async Task ARefusalNamesEverySurfaceThatSaidNothing()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartz();
        builder.Services.AddQuartzHttpApi();
        builder.Services.AddQuartzDashboard();

        await using WebApplication app = builder.Build();
        app.MapQuartzHttpApi();
        app.MapQuartzDashboard();

        Func<Task> act = async () => await app.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Quartz HTTP API*")
            .WithMessage("*Quartz dashboard*");
    }

    private static WebApplication CreateDashboard(
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<QuartzDashboardOptions>? configureDashboard = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddQuartz();
        builder.Services.AddQuartzDashboard(configureDashboard);
        configureBuilder?.Invoke(builder);
        return builder.Build();
    }

    private static List<RouteEndpoint> RouteEndpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder) app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }
}
