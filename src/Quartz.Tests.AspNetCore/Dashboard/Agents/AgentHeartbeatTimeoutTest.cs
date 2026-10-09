using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Quartz.Configuration;
using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// How the dashboard judges an agent that went quiet: by its heartbeats, on the dashboard's own clock.
/// </summary>
/// <remarks>
/// The agent's clock is never advanced, so it sends no heartbeat on its own; the dashboard's clock is,
/// which is what makes the silence. SignalR's keep-alive runs on real time underneath both and keeps the
/// socket open, which is the point: a live socket whose process has stopped answering is the failure the
/// application heartbeat exists to report.
/// </remarks>
public sealed class AgentHeartbeatTimeoutTest
{
    [Test]
    public async Task AnAgentThatMissesItsHeartbeatsIsReportedUnknownWithinTheListingDeadlineAndRecoversOnTheNextOne()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1");

        SchedulerHeaderDto live = (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == worker.Key).Subject;
        live.Status.Should().Be(SchedulerStatus.Running, "a registered agent is asked over its connection, and answers");
        live.Origin.Should().Be(SchedulerOrigin.Agent);
        live.LastSeenUtc.Should().Be(dashboard.Clock.GetUtcNow(), "the registration is the first sign of life");

        // 15 s × 3 is 45 s of silence; one more second and the agent is overdue.
        dashboard.Clock.Advance(TimeSpan.FromSeconds(46));

        Stopwatch listing = Stopwatch.StartNew();
        SchedulerHeaderDto silent = (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == worker.Key).Subject;
        listing.Stop();

        silent.Status.Should().Be(SchedulerStatus.Unknown, "three missed heartbeats are the dashboard's definition of not knowing");
        silent.LastSeenUtc.Should().Be(live.LastSeenUtc, "the row says when the agent was last heard from");
        listing.Elapsed.Should().BeLessThan(ContainerSchedulerRegistry.StatusTimeout,
            "an overdue agent is not asked over a tunnel nobody is answering; the listing says Unknown without waiting");
        dashboard.Logs.WithEventId(9116).Should().ContainSingle("the sweep logs the missed heartbeats once per silence")
            .Which.Level.Should().Be(LogLevel.Warning);

        await worker.Heartbeat();

        SchedulerHeaderDto back = (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == worker.Key).Subject;
        back.Status.Should().Be(SchedulerStatus.Running, "a heartbeat is the agent saying it is alive, and the dashboard asks it again");
        back.LastSeenUtc.Should().BeAfter(live.LastSeenUtc!.Value);
    }
}
