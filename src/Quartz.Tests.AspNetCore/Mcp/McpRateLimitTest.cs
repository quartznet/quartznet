using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M8: <see cref="QuartzMcpOptions.MaxChangesPerMinute" /> counts each caller's changes in a sliding minute, reads and
/// previews not at all, and says when to try again.
/// </summary>
public sealed class McpRateLimitTest
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
    public async Task TheTwentyFirstChangeInAMinuteIsRefusedWithWhenToTryAgain()
    {
        host = await McpTestHost.Start();
        (JobKey _, TriggerKey trigger) = await host.ScheduleJob();
        McpClient @operator = await host.Connect(RoleAuthenticationExtensions.Operator);

        for (int i = 0; i < 20; i++)
        {
            McpTestHost.Answer(await Resume(@operator, trigger));
        }

        JsonElement error = McpTestHost.Error(await Resume(@operator, trigger));

        error.GetProperty("code").GetString().Should().Be("rate_limited");
        error.GetProperty("retryAfterSeconds").GetInt32().Should().BePositive();
        host.Logs.WithEventId(9503).Should().ContainSingle();

        McpTestHost.Answer(await McpTestHost.Call(@operator, "get_trigger", new { scheduler = host.Scheduler.SchedulerName, group = trigger.Group, name = trigger.Name }));

        McpClient other = await host.Connect(RoleAuthenticationExtensions.Administrator);
        McpTestHost.Answer(await Resume(other, trigger));
    }

    [Test]
    public async Task APreviewIsNotCounted()
    {
        host = await McpTestHost.Start(mcp: options => options.MaxChangesPerMinute = 1);
        (JobKey _, TriggerKey trigger) = await host.ScheduleJob();
        McpClient admin = await host.Connect(RoleAuthenticationExtensions.Administrator);

        for (int i = 0; i < 5; i++)
        {
            McpTestHost.Answer(await McpTestHost.Call(admin, "unschedule", new { scheduler = host.Scheduler.SchedulerName, group = trigger.Group, name = trigger.Name }));
        }

        McpTestHost.Answer(await Resume(admin, trigger));
        McpTestHost.Error(await Resume(admin, trigger)).GetProperty("code").GetString().Should().Be("rate_limited");
    }

    /// <summary>
    /// A caller looping on a change their role refuses counts against the same minute: past the limit its refusals stop
    /// filling the Action Log's 250 entries and the log, and the limit is logged once for the window (#4245 review).
    /// </summary>
    [Test]
    public async Task RefusedChangesCountAgainstTheMinuteAndStopFloodingTheRecords()
    {
        RecordingRecorder recorder = new();
        host = await McpTestHost.Start(
            mcp: options => options.MaxChangesPerMinute = 3,
            services: services => services.AddSingleton<Quartz.AspNetCore.IOperatorActionRecorder>(recorder));
        (JobKey job, TriggerKey _) = await host.ScheduleJob();
        McpClient viewer = await host.Connect(RoleAuthenticationExtensions.Viewer);

        for (int i = 0; i < 10; i++)
        {
            McpTestHost.Error(await McpTestHost.Call(viewer, "trigger_job", new { scheduler = host.Scheduler.SchedulerName, group = job.Group, name = job.Name }))
                .GetProperty("code").GetString().Should().Be("forbidden", "the refusal is still answered as one");
        }

        recorder.Actions.Should().HaveCount(3, "past the minute's limit a refused change is no longer entered");
        host.Logs.WithEventId(9501).Should().HaveCount(3);
        host.Logs.WithEventId(9503).Should().ContainSingle("the limit is logged once for the window, not per call");
    }

    [Test]
    public async Task TheLimitIsLoggedOncePerWindow()
    {
        host = await McpTestHost.Start(mcp: options => options.MaxChangesPerMinute = 1);
        (JobKey _, TriggerKey trigger) = await host.ScheduleJob();
        McpClient @operator = await host.Connect(RoleAuthenticationExtensions.Operator);

        for (int i = 0; i < 6; i++)
        {
            await Resume(@operator, trigger);
        }

        host.Logs.WithEventId(9503).Should().ContainSingle();
    }

    [Test]
    public async Task ZeroTurnsTheLimitOff()
    {
        host = await McpTestHost.Start(mcp: options => options.MaxChangesPerMinute = 0);
        (JobKey _, TriggerKey trigger) = await host.ScheduleJob();
        McpClient @operator = await host.Connect(RoleAuthenticationExtensions.Operator);

        for (int i = 0; i < 30; i++)
        {
            McpTestHost.Answer(await Resume(@operator, trigger));
        }
    }

    private ValueTask<ModelContextProtocol.Protocol.CallToolResult> Resume(McpClient client, TriggerKey trigger)
    {
        return McpTestHost.Call(client, "resume_trigger", new { scheduler = host!.Scheduler.SchedulerName, group = trigger.Group, name = trigger.Name });
    }
}
