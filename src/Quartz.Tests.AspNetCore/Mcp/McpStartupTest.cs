using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Quartz.Mcp.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M10: what refuses to start — a writable endpoint with no operation policy, an endpoint nothing authorizes, and a
/// container whose MCP server holds tools that are not Quartz's — and the option bounds.
/// </summary>
public sealed class McpStartupTest
{
    private WebApplication? app;

    [TearDown]
    public async Task TearDown()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
            app = null;
        }
    }

    [Test]
    public async Task AWritableEndpointWithoutAnOperationPolicyIsRefused()
    {
        Func<Task> start = () => Start(options => options.ReadOnly = false, map: x => x.MapQuartzMcp().AllowAnonymous());

        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*ReadOnly is off*OperationAuthorizationPolicy*");
    }

    [TestCase(29)]
    [TestCase(1801)]
    public async Task AConfirmationLifetimeOutsideItsBoundsIsRefused(int seconds)
    {
        Func<Task> start = () => Start(options => options.ConfirmationLifetime = TimeSpan.FromSeconds(seconds), map: x => x.MapQuartzMcp().AllowAnonymous());

        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*ConfirmationLifetime*");
    }

    [Test]
    public async Task ANegativeRateLimitAndAMalformedOriginAreRefused()
    {
        Func<Task> negative = () => Start(options => options.MaxChangesPerMinute = -1, map: x => x.MapQuartzMcp().AllowAnonymous());
        await negative.Should().ThrowAsync<OptionsValidationException>().WithMessage("*MaxChangesPerMinute*");

        await TearDown();

        Func<Task> origin = () => Start(options => options.AllowedOrigins.Add("https://ops.example.com/path"), map: x => x.MapQuartzMcp().AllowAnonymous());
        await origin.Should().ThrowAsync<OptionsValidationException>().WithMessage("*AllowedOrigins*");
    }

    [Test]
    public async Task AnEndpointNothingAuthorizesRefusesToStart()
    {
        Func<Task> start = () => Start(configure: null, map: x => x.MapQuartzMcp());

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("The Quartz MCP endpoint is mapped with no authorization*");
    }

    [Test]
    public async Task AnOperationPolicyAuthorizesTheEndpoint()
    {
        await Start(
            options => options.OperationAuthorizationPolicy = RoleAuthenticationExtensions.OperatorRolesPolicy,
            map: x => x.MapQuartzMcp(),
            services: services => services.AddRoleAuthorization());
    }

    [Test]
    public async Task AnApplicationToolInTheContainersServerIsRefusedNamingIt()
    {
        Func<Task> start = () => Start(
            configure: null,
            map: x => x.MapQuartzMcp().AllowAnonymous(),
            services: services => services.AddSingleton<McpServerTool>(new ApplicationTool()));

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tools that are not Quartz's: deploy_app*");
    }

    [Test]
    public void APatternThatIsNotRootedIsRefused()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartz();
        builder.Services.AddQuartzMcp();
        app = builder.Build();

        Action map = () => app.MapQuartzMcp("quartz-mcp");

        map.Should().Throw<ArgumentException>().WithMessage("*must start with '/'*");
    }

    private async Task Start(Action<QuartzMcpOptions>? configure, Action<WebApplication> map, Action<IServiceCollection>? services = null)
    {
        TestContentRoot.Apply();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddQuartz();
        services?.Invoke(builder.Services);
        builder.Services.AddQuartzMcp(configure);
        app = builder.Build();
        map(app);
        await app.StartAsync();
    }

    private sealed class ApplicationTool : McpServerTool
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = "deploy_app",
            InputSchema = System.Text.Json.JsonElement.Parse("{\"type\":\"object\"}"),
        };

        public override IReadOnlyList<object> Metadata => [];

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CallToolResult());
        }
    }

    /// <summary>The validator alone, for the one rule a host has no other way to see.</summary>
    [Test]
    public void TheValidatorNamesAPolicyWithNothingToEvaluateIt()
    {
        QuartzMcpOptionsValidator validator = new(new NoServices());

        ValidateOptionsResult result = validator.Validate(null, new QuartzMcpOptions { OperationAuthorizationPolicy = "Roles" });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AddAuthorization");
    }

    private sealed class NoServices : IServiceProviderIsService
    {
        public bool IsService(Type serviceType) => false;
    }
}
