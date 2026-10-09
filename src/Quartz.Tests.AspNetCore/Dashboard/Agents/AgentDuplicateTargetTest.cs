using Quartz.Dashboard.Services;
using Quartz.Impl;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// One target is held by one scheduler instance at a time: a second instance claiming a live target is
/// refused and keeps asking, and takes it over once the holder has gone quiet.
/// </summary>
public sealed class AgentDuplicateTargetTest
{
    [Test]
    public async Task ASecondInstanceOnALiveTargetIsRefusedUntilTheHolderIsOverdue()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker holder = await dashboard.StartAgent("w1", instanceId: "node-1");

        AgentWorker claimant = await dashboard.StartAgent("w1", instanceId: "node-2", waitUntilRegistered: false);
        await Eventually.Until(() => claimant.Logs.WithEventId(9302).Count > 0);

        claimant.Logs.WithEventId(9302)[0].Message.Should().Contain("held by instance 'node-1'");
        dashboard.Logs.WithEventId(9113).Should().NotBeEmpty("the hub logs the refusal too");
        dashboard.Registry.Find("w1")!.SchedulerInstanceId.Should().Be("node-1", "the holder keeps its target");
        (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == holder.Key);

        // The holder goes quiet, and the claimant asks again on its own clock.
        dashboard.Clock.Advance(TimeSpan.FromSeconds(46));
        claimant.Clock.Advance(AgentConnection.RegistrationRetryDelay + TimeSpan.FromSeconds(1));

        await claimant.WaitUntilRegistered();

        dashboard.Registry.Find("w1")!.SchedulerInstanceId.Should().Be("node-2", "an overdue holder is one the operator redeployed over");
        claimant.Logs.WithEventId(9300).Should().ContainSingle();

        SchedulerHeaderDto row = (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == claimant.Key).Subject;
        row.SchedulerInstanceId.Should().Be("node-2");
        row.Status.Should().Be(SchedulerStatus.Running);
    }
}
