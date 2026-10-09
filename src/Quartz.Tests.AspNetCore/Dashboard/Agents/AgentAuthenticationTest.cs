using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Tests.AspNetCore.Dashboard.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// How the agent hub decides who may dial in: the bearer token, read from the <c>Authorization</c> header
/// and from nowhere else.
/// </summary>
/// <remarks>
/// A token in a URL ends up in access logs, so an <c>access_token</c> query parameter — which SignalR's
/// browser clients send and its server reads for them — is not read here. The .NET client sends the token
/// as a header on every transport, which is what makes the rule costless for an agent and what makes the
/// case with the token in the query the one that proves it.
/// </remarks>
public sealed class AgentAuthenticationTest
{
    [Test]
    public async Task AConnectionWithTheWrongTokenIsAbortedAndNeverRegisters()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();

        AgentWorker worker = await dashboard.StartAgent("w1", options => options.Token = "not-the-token", waitUntilRegistered: false);

        await Eventually.Until(() => dashboard.Logs.WithEventId(9111).Count > 0);

        dashboard.Logs.WithEventId(9111).Should().NotBeEmpty("a refused connection is logged, naming the connection and the reason");
        dashboard.Logs.WithEventId(9111)[0].Level.Should().Be(LogLevel.Warning);
        dashboard.Registry.Find("w1").Should().BeNull("an aborted connection runs no hub method, so nothing was registered");
        worker.Plugin.Connection!.IsRegistered.Should().BeFalse();
    }

    /// <summary>
    /// The rule R4 states: a token that arrives only as <c>access_token</c> in the URL is not a token at
    /// all to this hub.
    /// </summary>
    [Test]
    public async Task ATokenInTheQueryStringIsNotReadAndTheConnectionIsAborted()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();

        AgentWorker worker = await dashboard.StartAgent("w1", options =>
        {
            options.Endpoint = new Uri(dashboard.HubUri + "?access_token=" + AgentDashboard.Token);
            options.ConfigureConnection = connection =>
            {
                connection.HttpMessageHandlerFactory = _ => dashboard.App.GetTestServer().CreateHandler();
                connection.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;

                // The .NET client would put the same token in the header; this case is about a client that
                // does not, which is what a token in the URL would otherwise let through.
                connection.AccessTokenProvider = null;
            };
        }, waitUntilRegistered: false);

        await Eventually.Until(() => dashboard.Logs.WithEventId(9111).Count > 0);

        dashboard.Logs.WithEventId(9111)[0].Message.Should().Contain("no Authorization header",
            "the hub reads the bearer header only; a token in the URL ends up in access logs");
        dashboard.Registry.Find("w1").Should().BeNull();
        worker.Plugin.Connection!.IsRegistered.Should().BeFalse();
    }

    [Test]
    public async Task TheSecondaryTokenIsAcceptedWhileARotationIsUnderWay()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start(agents => agents.Tokens.Secondary = "the-next-token");

        AgentWorker worker = await dashboard.StartAgent("w1", options => options.Token = "the-next-token");

        dashboard.Registry.Find("w1").Should().NotBeNull("the secondary slot is what lets agents roll to a new token one at a time");
        worker.Plugin.Connection!.IsRegistered.Should().BeTrue();
        dashboard.Logs.WithEventId(9111).Should().BeEmpty();
    }

    /// <summary>
    /// The hardening the dashboard recommends is a fail-closed <c>FallbackPolicy</c>. A hub authenticated
    /// by token says so to the host, so that policy does not turn every agent away with <c>401</c> — and
    /// the token check still turns away the agents it should.
    /// </summary>
    [Test]
    public async Task ATokenAuthenticatedHubIsReachableUnderAFailClosedFallbackPolicy()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start(configureServices: services =>
        {
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        });

        AgentWorker worker = await dashboard.StartAgent("w1");
        worker.Plugin.Connection!.IsRegistered.Should().BeTrue("the hub stated AllowAnonymous for the host and checks the token itself");
        worker.Logs.WithEventId(9301).Should().BeEmpty("nothing answered 401");

        AgentWorker wrong = await dashboard.StartAgent("w2", options => options.Token = "not-the-token", waitUntilRegistered: false);
        await Eventually.Until(() => dashboard.Logs.WithEventId(9111).Count > 0);
        dashboard.Registry.Find("w2").Should().BeNull("a wrong token is still a wrong token");
        wrong.Plugin.Connection!.IsRegistered.Should().BeFalse();
    }

    /// <summary>
    /// A token that is only whitespace would match an agent that sends a bearer header with nothing in
    /// it; the options refuse it before the hub is mapped.
    /// </summary>
    [Test]
    public async Task ABlankTokenIsRefusedAtStartup()
    {
        Func<Task> start = () => AgentDashboard.Start(agents => agents.Tokens.Secondary = "   ");

        (await start.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*AcceptAgents*token*");
    }

    /// <summary>
    /// With no token configured the hub is held to the host's own authentication, and a scheme that
    /// authenticates nobody lets no agent in.
    /// </summary>
    [Test]
    public async Task AHostAuthorizationPolicyIsAppliedWhenNoTokenIsConfigured()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start(
            agents =>
            {
                agents.Tokens.Primary = null;
                agents.AuthorizationPolicy = "agents";
            },
            services =>
            {
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.AddAuthorizationBuilder().AddPolicy("agents", policy => policy.RequireAuthenticatedUser());
            });

        AgentWorker worker = await dashboard.StartAgent("w1", waitUntilRegistered: false);

        await Eventually.Until(() => worker.Logs.WithEventId(9301).Count > 0);

        worker.Logs.WithEventId(9301).Should().NotBeEmpty("the hub refused the connection with 401, which the agent reports as the dashboard being unreachable");
        dashboard.Registry.Find("w1").Should().BeNull();
    }
}
