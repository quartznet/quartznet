using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The API authenticates nothing of its own, every one of its routes mutates, and a job it schedules
/// names its type with a string the request carries — so a mapping that says nothing about
/// authorization is refused before the listener is bound.
/// </summary>
/// <remarks>
/// The check runs in <c>IHostedLifecycleService.StartingAsync</c>, which every hosted service completes
/// before any of them is started, so <c>StartAsync</c> here is the whole of what a deployment would
/// experience: the process does not come up. Four things satisfy it and each is a statement someone had
/// to write — <c>RequireAuthorization()</c>, <c>SchedulerAuthorizationPolicy</c>, a fail-closed
/// <c>FallbackPolicy</c>, or <c>AllowAnonymous()</c> meaning it.
/// </remarks>
public sealed class QuartzHttpApiAuthorizationGuardTest
{
    [Test]
    public async Task AnApiMappedWithNothingSaidRefusesToStart()
    {
        await using WebApplication app = CreateApi();
        app.MapQuartzHttpApi();

        Func<Task> act = async () => await app.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*MapQuartzHttpApi().RequireAuthorization()*", "the message names the fix that authorizes it")
            .WithMessage("*SchedulerAuthorizationPolicy*", "the fix that authorizes each scheduler on its own")
            .WithMessage("*MapQuartzHttpApi().AllowAnonymous()*", "and the one that says the opposite on purpose")
            .WithMessage("*FallbackPolicy*", "with the application-wide answer named beside them")
            .WithMessage("*/quartz-api/*", "and at least one of the routes it is refusing to serve");
    }

    [Test]
    public async Task AnApiTheCallerAuthorizedStarts()
    {
        await using WebApplication app = CreateApi();
        app.MapQuartzHttpApi().RequireAuthorization();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task AnApiTheCallerMadeAnonymousOnPurposeStarts()
    {
        await using WebApplication app = CreateApi();
        app.MapQuartzHttpApi().AllowAnonymous();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task AnApiUnderAFallbackPolicyStarts()
    {
        await using WebApplication app = CreateApi(FailClosed);
        app.MapQuartzHttpApi();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task AnApiWithAPerSchedulerPolicyStarts()
    {
        // The per-scheduler policy is a filter over the route rather than IAuthorizeData metadata, so it
        // is invisible to the metadata check and the mapping has to say so itself.
        await using WebApplication app = CreateApi(
            configureBuilder: builder => builder.Services.AddQuartzHttpApi(
                options => options.SchedulerAuthorizationPolicy = "scheduler-owner"));
        app.MapQuartzHttpApi();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task AnApiAuthorizedOnTheGroupAboveItStarts()
    {
        // Metadata applied to a parent group flows into each endpoint's metadata, so a grouped map is
        // already a statement and the guard has nothing to add.
        await using WebApplication app = CreateApi();
        RouteGroupBuilder group = app.MapGroup("/ops").RequireAuthorization();
        group.MapQuartzHttpApi();

        await app.StartAsync();
        await app.StopAsync();
    }

    [Test]
    public async Task AnApiMappedOntoAGroupThatSaysNothingIsStillRefused()
    {
        // The grouped case is checked once the group has finished its endpoints rather than before the
        // listener binds, so this is the half of it that proves the deferral is not an escape hatch.
        await using WebApplication app = CreateApi();
        RouteGroupBuilder group = app.MapGroup("/ops");
        group.MapQuartzHttpApi();

        Func<Task> act = async () => await app.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Quartz HTTP API*");
    }

    [Test]
    public async Task AnApiRegisteredAndNeverMappedStarts()
    {
        // Nothing is served, so there is nothing to refuse - and refusing here would fail every
        // application that registers the services in one place and maps conditionally in another.
        await using WebApplication app = CreateApi();

        await app.StartAsync();
        await app.StopAsync();
    }

    internal static void FailClosed(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAssertion(_ => false)
            .Build());
    }

    private static WebApplication CreateApi(Action<WebApplicationBuilder>? configureBuilder = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddQuartz();
        builder.Services.AddQuartzHttpApi();
        configureBuilder?.Invoke(builder);
        return builder.Build();
    }
}
