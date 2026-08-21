using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The path the API is served under can be named where the endpoints are mapped, the way the rest of
/// ASP.NET Core reads — <c>MapHealthChecks("/health")</c> — and that beats the Add-time option.
/// </summary>
public class HttpApiRoutePatternTest
{
    [Test]
    public async Task TheDefaultPathIsUsedWhenNoPatternIsGiven()
    {
        await using WebApplication app = CreateApp();
        app.MapQuartzHttpApi();

        Patterns(app).Should().Contain(p => p!.StartsWith("/quartz-api/", StringComparison.Ordinal));
    }

    [Test]
    public async Task APatternGivenAtTheMapSiteServesTheApi()
    {
        await using WebApplication app = CreateApp();
        app.MapQuartzHttpApi("/ops/api");

        Patterns(app).Should().Contain("/ops/api/schedulers");
        Patterns(app).Should().NotContain(p => p!.StartsWith("/quartz-api", StringComparison.Ordinal));
    }

    [Test]
    public async Task APatternGivenAtTheMapSiteBeatsTheConfiguredPath()
    {
        await using WebApplication app = CreateApp(options => options.ApiPath = "/configured");
        app.MapQuartzHttpApi("/mapped");

        Patterns(app).Should().Contain("/mapped/schedulers");
        Patterns(app).Should().NotContain(p => p!.StartsWith("/configured", StringComparison.Ordinal),
            "the pattern at the map site is the more specific of the two");
    }

    [Test]
    public async Task AConfiguredPathIsStillUsedByTheParameterlessOverload()
    {
        await using WebApplication app = CreateApp(options => options.ApiPath = "/configured");
        app.MapQuartzHttpApi();

        Patterns(app).Should().Contain("/configured/schedulers");
    }

    [TestCase("")]
    [TestCase("  ")]
    [TestCase("quartz-api")]
    public async Task AnInvalidPatternAtTheMapSiteIsRejected(string pattern)
    {
        await using WebApplication app = CreateApp();

        Action act = () => app.MapQuartzHttpApi(pattern);

        act.Should().Throw<ArgumentException>().WithParameterName("pattern",
            "a pattern given at the map site is held to the same rule as the configured path, and the "
            + "options validator has already run by then");
    }

    private static WebApplication CreateApp(Action<QuartzHttpApiOptions>? configure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartz(quartz => quartz.AddQuartzHttpApi(configure));
        return builder.Build();
    }

    private static List<string?> Patterns(WebApplication app)
    {
        return ((IEndpointRouteBuilder) app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();
    }
}
