using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;

using Quartz.AspNetCore;
using Quartz.Dashboard.Services;
using Quartz.Mcp.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M11: a change made through MCP is entered in the dashboard's Action Log with Via MCP, the user and the client as it
/// stated itself; the pause is in the caller's name, via MCP; and the change is logged as <c>9500</c>.
/// </summary>
public sealed class McpActionLogTest
{
    private McpTestHost? host;

    [TearDown]
    public async Task TearDown()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }
    }

    [Test]
    public async Task APauseThroughMcpIsAnActionLogEntryViaMcpWithTheUserAndTheClient()
    {
        // The dashboard registers the Action Log's recorder; nothing of its pages is mapped.
        host = await McpTestHost.Start(services: services => services.AddQuartzDashboard());
        (JobKey _, TriggerKey trigger) = await host.ScheduleJob();
        McpClient @operator = await host.Connect(RoleAuthenticationExtensions.Operator, clientName: "claude-code");

        McpTestHost.Answer(await McpTestHost.Call(@operator, "pause_trigger", new
        {
            scheduler = host.Scheduler.SchedulerName,
            group = trigger.Group,
            name = trigger.Name,
            reason = "fails on a database timeout",
        }));

        DashboardActionLogEntry entry = host.Services.GetRequiredService<DashboardActionLogService>().GetLatest().Should().ContainSingle().Subject;
        entry.Via.Should().Be(OperatorChannel.Mcp);
        entry.User.Should().Be("quartz-operator-user");
        entry.Client.Should().Be("claude-code/1.0", "MCP's clientInfo, as the client stated it");
        entry.Action.Should().Be("PauseTrigger", "the word the dashboard's button and an HTTP API entry use");
        entry.Target.Should().Be(trigger.ToString());
        entry.Succeeded.Should().BeTrue();
        entry.Message.Should().Be("reason: fails on a database timeout");

        PauseInfo? pause = await host.Scheduler.GetTriggerPause(trigger);
        pause!.RequestedBy.Should().Be("quartz-operator-user via MCP");
        pause.Reason.Should().Be("fails on a database timeout");

        RecordedLogEntry line = host.Logs.WithEventId(9500).Should().ContainSingle().Subject;
        line.Properties["User"].Should().Be("quartz-operator-user");
        line.Properties["Client"].Should().Be("claude-code/1.0");
        line.Properties["Tool"].Should().Be("pause_trigger");
        line.Message.Should().NotContain("fails on a database timeout", "the log line carries no argument");
    }

    [Test]
    public async Task ARefusedChangeIsEnteredAsRefusedAndAReadIsNotEnteredAtAll()
    {
        host = await McpTestHost.Start(services: services => services.AddQuartzDashboard());
        (JobKey job, TriggerKey _) = await host.ScheduleJob();
        McpClient viewer = await host.Connect(RoleAuthenticationExtensions.Viewer);

        await McpTestHost.Call(viewer, "trigger_job", new { scheduler = host.Scheduler.SchedulerName, group = job.Group, name = job.Name });
        await McpTestHost.Call(viewer, "list_jobs", new { scheduler = host.Scheduler.SchedulerName });

        DashboardActionLogEntry entry = host.Services.GetRequiredService<DashboardActionLogService>().GetLatest().Should().ContainSingle().Subject;
        entry.Refused.Should().BeTrue();
        entry.Via.Should().Be(OperatorChannel.Mcp);
        entry.Action.Should().Be("TriggerJob");
        entry.Message.Should().Contain("it needs Operate");
    }

    [Test]
    public void WithoutClientInfoTheClientIsTheUserAgentsProductToken()
    {
        McpCaller caller = McpCaller.From(new System.Security.Claims.ClaimsPrincipal(), clientInfo: null, userAgent: "curl/8.4.0 (x86_64-pc-linux-gnu)\u0007");

        caller.Client.Should().Be("curl/8.4.0");
        McpCaller.From(new System.Security.Claims.ClaimsPrincipal(), null, new string('x', 100)).Client.Should().HaveLength(64);
        McpCaller.From(new System.Security.Claims.ClaimsPrincipal(), null, "ab\u0001cd").Client.Should().Be("abcd", "control characters are taken out");
        caller.Name.Should().Be("(anonymous)");
        caller.HasStableIdentity.Should().BeFalse();
    }
}
