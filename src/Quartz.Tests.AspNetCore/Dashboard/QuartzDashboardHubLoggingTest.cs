using System.Security.Claims;

using FakeItEasy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Dashboard.Support;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The Debug lines the live-events hub writes when a connection opens and closes, with the user the
/// connection belongs to.
/// </summary>
/// <remarks>
/// Each is guarded by <c>ILogger.IsEnabled</c>, because the user's name is read by a method call, so each is
/// asserted with the level on.
/// </remarks>
public class QuartzDashboardHubLoggingTest
{
    private const int HubConnectedEventId = 9102;
    private const int HubDisconnectedEventId = 9103;

    [TestCase("ops@example.com", "ops@example.com")]
    [TestCase(null, "(anonymous)")]
    public async Task AConnectionOpeningAndClosingNamesItsUser(string? identityName, string expectedUser)
    {
        RecordingLoggerProvider logs = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));

        ServiceCollection services = new();
        await using ServiceProvider provider = services.BuildServiceProvider();

        SchedulerAuthorization authorization = new(
            Options.Create(new QuartzDashboardOptions()),
            new TestSchedulerAuthorizationService(),
            new TestAuthenticationStateProvider());

        HubCallerContext callerContext = A.Fake<HubCallerContext>();
        A.CallTo(() => callerContext.ConnectionId).Returns("connection-1");
        A.CallTo(() => callerContext.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            identityName is null ? [] : [new Claim(ClaimTypes.Name, identityName)],
            authenticationType: identityName is null ? null : "test")));

        using QuartzDashboardHub hub = new(
            authorization,
            new DashboardHubForwarder(provider, NullLogger<DashboardHubForwarder>.Instance),
            factory.CreateLogger<QuartzDashboardHub>())
        {
            Context = callerContext
        };

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(exception: null);

        RecordedLogEntry connected = logs.WithEventId(HubConnectedEventId).Should().ContainSingle().Which;
        connected.Level.Should().Be(LogLevel.Debug);
        connected.Properties["ConnectionId"].Should().Be("connection-1");
        connected.Properties["User"].Should().Be(expectedUser);

        RecordedLogEntry disconnected = logs.WithEventId(HubDisconnectedEventId).Should().ContainSingle().Which;
        disconnected.Level.Should().Be(LogLevel.Debug);
        disconnected.Properties["User"].Should().Be(expectedUser);
    }
}
