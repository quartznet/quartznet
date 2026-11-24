using System.Text.Json;

using FakeItEasy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Quartz.AspNetCore;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M3: <c>tools/call</c> asks every rule again against the scheduler it names — the list is a convenience, the call
/// is the enforcement — with the resource carrying the target, and a refused change is recorded.
/// </summary>
public sealed class McpAuthorizationTest
{
    private McpTestHost? host;
    private readonly List<SchedulerResource> asked = [];

    [TearDown]
    public async Task TearDown()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }

        asked.Clear();
    }

    [Test]
    public async Task AViewerIsRefusedAPauseWithTheLevelItNeedsAndNothingReachesTheScheduler()
    {
        RecordingRecorder recorder = new();
        host = await McpTestHost.Start(services: services => services.AddSingleton<IOperatorActionRecorder>(recorder));
        (JobKey _, TriggerKey trigger) = await host.ScheduleJob();
        McpClient viewer = await host.Connect(RoleAuthenticationExtensions.Viewer);

        CallToolResult result = await McpTestHost.Call(viewer, "pause_trigger", new
        {
            scheduler = host.Scheduler.SchedulerName,
            group = trigger.Group,
            name = trigger.Name,
            reason = "it fails every night",
        });

        JsonElement error = McpTestHost.Error(result);
        error.GetProperty("code").GetString().Should().Be("forbidden");
        error.GetProperty("message").GetString().Should().Contain("PauseTrigger").And.Contain("it needs Operate");
        (await host.Scheduler.GetTriggerState(trigger)).Should().Be(TriggerState.Normal, "a refused call reaches no scheduler");

        host.Logs.WithEventId(9501).Should().ContainSingle().Which.Properties["User"].Should().Be("quartz-viewer-user");
        recorder.Actions.Should().ContainSingle().Which.Should().Match<OperatorAction>(action =>
            action.Via == OperatorChannel.Mcp && action.Refused && !action.Succeeded && action.User == "quartz-viewer-user"
            && action.Operation == "PauseTrigger" && action.Client == "test-agent/1.0");
    }

    [Test]
    public async Task TheCallIsAskedAgainstTheSchedulerItNamesNotTheOneTheListWasFor()
    {
        // An administrator on this process's scheduler and a viewer on the agent's: the list offers pause, because one
        // scheduler allows it, and the call on the other is refused.
        host = await McpTestHost.Start(
            mcp: options => options.OperationAuthorizationPolicy = PerTarget,
            services: services =>
            {
                services.AddSingleton<IAuthorizationHandler>(new PerTargetHandler(asked));
                services.AddAuthorizationBuilder().AddPolicy(PerTarget, policy => policy.AddRequirements(new PerTargetRequirement()));
            });
        BindAgent(host);
        McpClient caller = await host.Connect(RoleAuthenticationExtensions.Administrator);

        (await McpTestHost.ToolNames(caller)).Should().Contain("pause_trigger");

        CallToolResult onAgent = await McpTestHost.Call(caller, "pause_trigger", new
        {
            scheduler = "a1/AgentScheduler",
            group = "g",
            name = "t",
            reason = "not on the agent",
        });

        McpTestHost.Error(onAgent).GetProperty("message").GetString().Should().Contain("it needs Operate");
        asked.Should().Contain(resource => resource.Target == "a1" && resource.SchedulerName == "AgentScheduler" && resource.Operation == "PauseTrigger",
            "the resource carries the key's target, as the HTTP API's does");
    }

    [Test]
    public async Task ATargetIsAskedAboutInItsOwnSpellingWhateverTheCallerWrote()
    {
        host = await McpTestHost.Start(
            mcp: options => options.OperationAuthorizationPolicy = PerTarget,
            services: services =>
            {
                services.AddSingleton<IAuthorizationHandler>(new PerTargetHandler(asked));
                services.AddAuthorizationBuilder().AddPolicy(PerTarget, policy => policy.AddRequirements(new PerTargetRequirement()));
            });
        BindAgent(host);
        McpClient caller = await host.Connect(RoleAuthenticationExtensions.Administrator);

        await McpTestHost.Call(caller, "get_scheduler", new { scheduler = "A1/AgentScheduler" });

        asked.Should().Contain(resource => resource.Target == "a1",
            "a policy written against a1 cannot be stepped round by spelling the target A1");
        asked.Should().NotContain(resource => resource.Target == "A1");
    }

    [Test]
    public async Task ARefusedCallerCannotTellAMissingSchedulerFromAForbiddenOne()
    {
        host = await McpTestHost.Start();
        McpClient anonymous = await host.Connect(role: null);

        JsonElement missing = McpTestHost.Error(await McpTestHost.Call(anonymous, "get_scheduler", new { scheduler = "nowhere/Nothing" }));
        JsonElement present = McpTestHost.Error(await McpTestHost.Call(anonymous, "get_scheduler", new { scheduler = host.Scheduler.SchedulerName }));

        missing.GetProperty("code").GetString().Should().Be("forbidden");
        present.GetProperty("code").GetString().Should().Be("forbidden");
    }

    [Test]
    public async Task AReadOnlyEndpointRefusesEveryChangeBeforeAPolicyIsAsked()
    {
        host = await McpTestHost.Start(mcp: options => options.ReadOnly = true);
        (JobKey job, TriggerKey _) = await host.ScheduleJob();
        McpClient admin = await host.Connect(RoleAuthenticationExtensions.Administrator);

        JsonElement error = McpTestHost.Error(await McpTestHost.Call(admin, "trigger_job", new { scheduler = host.Scheduler.SchedulerName, group = job.Group, name = job.Name }));

        error.GetProperty("code").GetString().Should().Be("read_only");
    }

    [Test]
    public async Task AnUnknownArgumentIsRefusedNamingIt()
    {
        host = await McpTestHost.Start();
        McpClient admin = await host.Connect(RoleAuthenticationExtensions.Administrator);

        JsonElement error = McpTestHost.Error(await McpTestHost.Call(admin, "trigger_job", new
        {
            scheduler = host.Scheduler.SchedulerName,
            group = "g",
            name = "n",
            jobData = new { command = "rm -rf /" },
        }));

        error.GetProperty("code").GetString().Should().Be("invalid_arguments");
        error.GetProperty("message").GetString().Should().Contain("'jobData'");
    }

    [TestCase(201)]
    [TestCase(0)]
    public async Task APageOutsideItsBoundsIsRefusedNotCut(int take)
    {
        host = await McpTestHost.Start();
        McpClient admin = await host.Connect(RoleAuthenticationExtensions.Administrator);

        JsonElement error = McpTestHost.Error(await McpTestHost.Call(admin, "list_jobs", new { scheduler = host.Scheduler.SchedulerName, take }));

        error.GetProperty("message").GetString().Should().Contain("from 1 to 200");
    }

    [Test]
    public async Task APauseWithoutAReasonIsRefused()
    {
        host = await McpTestHost.Start();
        McpClient admin = await host.Connect(RoleAuthenticationExtensions.Administrator);

        JsonElement error = McpTestHost.Error(await McpTestHost.Call(admin, "pause_job", new { scheduler = host.Scheduler.SchedulerName, group = "g", name = "n", reason = "  " }));

        error.GetProperty("message").GetString().Should().Contain("'reason' is required");
    }

    private const string PerTarget = "PerTarget";

    private static void BindAgent(McpTestHost host)
    {
        IScheduler agent = A.Fake<IScheduler>();
        A.CallTo(() => agent.SchedulerName).Returns("AgentScheduler");
        host.Services.GetRequiredService<Quartz.Configuration.SchedulerTargets>().Add(new Quartz.Configuration.SchedulerTarget { Name = "a1", Origin = SchedulerOrigin.Agent });
        host.Services.GetRequiredService<Quartz.Extensibility.ISchedulerRepository>().Bind(new FakeProxyScheduler(agent, "a1", SchedulerOrigin.Agent), "a1");
    }

    private sealed class PerTargetRequirement : IAuthorizationRequirement;

    /// <summary>
    /// Administer on this process's schedulers, View on any target's, recording what it was asked.
    /// </summary>
    private sealed class PerTargetHandler(List<SchedulerResource> asked) : AuthorizationHandler<PerTargetRequirement, SchedulerResource>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PerTargetRequirement requirement, SchedulerResource resource)
        {
            lock (asked)
            {
                asked.Add(resource);
            }

            SchedulerAccess granted = resource.Target is null ? SchedulerAccess.Administer : SchedulerAccess.View;
            if ((resource.Access ?? SchedulerAccess.View) <= granted)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>Keeps every action an <see cref="IOperatorActionRecorder" /> is handed.</summary>
internal sealed class RecordingRecorder : IOperatorActionRecorder
{
    private readonly List<OperatorAction> actions = [];

    public List<OperatorAction> Actions
    {
        get
        {
            lock (actions)
            {
                return [.. actions];
            }
        }
    }

    public void Record(OperatorAction action)
    {
        lock (actions)
        {
            actions.Add(action);
        }
    }
}
