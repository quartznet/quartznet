using Microsoft.AspNetCore.SignalR.Client;

using Quartz.Dashboard.Services;
using Quartz.HttpApiContract;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// The hub's side of the protocol, driven by a connection speaking it by hand: what the hub does with a
/// message a well-behaved agent never sends.
/// </summary>
public sealed class AgentHubProtocolTest
{
    /// <summary>
    /// One connection is one agent is one target. A connection that holds a target and registers another
    /// is refused naming the one it holds, and what it holds is untouched.
    /// </summary>
    [Test]
    public async Task AConnectionThatAlreadyHoldsATargetIsRefusedASecondOne()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        await using HubConnection hub = dashboard.RawConnection();
        List<string> closes = [];
        hub.On<string>(AgentProtocol.Close, reason => closes.Add(reason));
        await hub.StartAsync();

        AgentRegistered first = await hub.InvokeAsync<AgentRegistered>(AgentProtocol.Register, Registration("w1", "node-1"));
        first.Accepted.Should().BeTrue();

        AgentRegistered second = await hub.InvokeAsync<AgentRegistered>(AgentProtocol.Register, Registration("w2", "node-1"));

        second.Accepted.Should().BeFalse("a second target on the connection would leave the first unmapped the moment either went");
        second.Reason.Should().Contain("'w1'", "the refusal names the target the connection holds");
        dashboard.Registry.Find("w2").Should().BeNull();
        dashboard.Registry.Find("w1")!.ConnectionId.Should().NotBeNull("the registration it holds is untouched");
        dashboard.Logs.WithEventId(9113).Should().ContainSingle().Which.Message.Should().Contain("w2");

        // The connection goes on as the agent it is: a heartbeat is heard, and the same target registered
        // again is the same instance taking its own entry over, which closes nothing.
        DateTimeOffset before = dashboard.Registry.Find("w1")!.LastSeenUtc;
        dashboard.Clock.Advance(TimeSpan.FromSeconds(1));
        await hub.SendAsync(AgentProtocol.Heartbeat, new AgentHeartbeat { Status = SchedulerStatus.Running, SentAtUtc = dashboard.Clock.GetUtcNow() });
        await Eventually.Until(() => dashboard.Registry.Find("w1")!.LastSeenUtc > before);

        AgentRegistered again = await hub.InvokeAsync<AgentRegistered>(AgentProtocol.Register, Registration("w1", "node-1"));
        again.Accepted.Should().BeTrue();
        closes.Should().BeEmpty("nothing told this connection to go");
    }

    /// <summary>
    /// The same instance registering from a new connection — a redeploy that kept its instance id — takes
    /// its row over with what the new registration says: its version and the routes it now serves.
    /// </summary>
    [Test]
    public async Task TheSameInstanceOnANewConnectionCarriesItsNewVersionAndRoutes()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        await using HubConnection old = dashboard.RawConnection();
        List<string> closes = [];
        old.On<string>(AgentProtocol.Close, reason => closes.Add(reason));
        await old.StartAsync();
        (await old.InvokeAsync<AgentRegistered>(AgentProtocol.Register, Registration("w1", "node-1", "4.4.0"))).Accepted.Should().BeTrue();

        await using HubConnection redeployed = dashboard.RawConnection();
        await redeployed.StartAsync();
        AgentRegistered taken = await redeployed.InvokeAsync<AgentRegistered>(
            AgentProtocol.Register,
            Registration("w1", "node-1", "4.5.0") with { Routes = [SchedulerRoutes.GetSchedulerDetails.Name], ReadOnly = true });

        taken.Accepted.Should().BeTrue();
        AgentEntry entry = dashboard.Registry.Find("w1")!;
        entry.QuartzVersion.Should().Be("4.5.0", "the row says what the agent runs now, not what it ran before the redeploy");
        entry.ReadOnly.Should().BeTrue();
        entry.Serves(SchedulerRoutes.GetSchedulerDetails).Should().BeTrue();
        entry.Serves(SchedulerRoutes.TriggerJob).Should().BeFalse("the routes are the new registration's");
        await Eventually.Until(() => closes.Count > 0);
        closes.Should().Equal(["superseded"], "the old connection is told why it is being dropped");
    }

    private static AgentRegistration Registration(string target, string instanceId, string version = "4.5.0")
    {
        return new AgentRegistration
        {
            Target = target,
            SchedulerName = AgentDashboard.SchedulerName,
            SchedulerInstanceId = instanceId,
            QuartzVersion = version,
            Routes = SchedulerRoutes.All.Select(route => route.Name).ToArray(),
        };
    }
}
