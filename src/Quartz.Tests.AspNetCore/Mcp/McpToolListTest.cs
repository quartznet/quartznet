using ModelContextProtocol.Client;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

using Microsoft.Extensions.DependencyInjection;

using FakeItEasy;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M2: <c>tools/list</c> offers each caller the tools they can call on some scheduler they may see.
/// </summary>
public sealed class McpToolListTest
{
    private static readonly string[] ViewTools =
    [
        "get_job", "get_job_run_status", "get_scheduler", "get_trigger", "list_cluster_nodes", "list_executing", "list_jobs",
        "list_schedulers", "list_triggers", "query_history",
    ];

    private static readonly string[] OperateTools =
    [
        .. ViewTools, "interrupt", "pause_job", "pause_trigger", "reset_trigger_from_error", "resume_job", "resume_trigger", "trigger_job",
    ];

    private static readonly string[] AdministerTools = [.. OperateTools, "reschedule", "unschedule"];

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

    [TestCase(RoleAuthenticationExtensions.Viewer, 10)]
    [TestCase(RoleAuthenticationExtensions.Operator, 17)]
    [TestCase(RoleAuthenticationExtensions.Administrator, 19)]
    public async Task EachRoleIsOfferedTheToolsOfItsLevel(string role, int count)
    {
        host = await McpTestHost.Start();

        List<string> tools = await McpTestHost.ToolNames(await host.Connect(role));

        tools.Should().HaveCount(count);
        tools.Should().BeEquivalentTo(count switch
        {
            10 => ViewTools,
            17 => OperateTools,
            _ => AdministerTools,
        });
    }

    [Test]
    public async Task AReadOnlyEndpointOffersAnAdministratorTheReadsAlone()
    {
        host = await McpTestHost.Start(mcp: options => options.ReadOnly = true);

        (await McpTestHost.ToolNames(await host.Connect(RoleAuthenticationExtensions.Administrator))).Should().BeEquivalentTo(ViewTools);
    }

    [Test]
    public async Task TheDefaultEndpointIsReadOnly()
    {
        host = await McpTestHost.Start(writable: false);

        (await McpTestHost.ToolNames(await host.Connect(role: null))).Should().BeEquivalentTo(ViewTools,
            "with no policy every caller sees every scheduler, and the endpoint is read-only until it is told otherwise");
    }

    [Test]
    public async Task ACallerWithNoStableIdentityIsNeverOfferedTheAdministerTools()
    {
        // No policy and writable is refused by the validator, so the operation policy allows everyone instead.
        host = await McpTestHost.Start(mcp: options => options.OperationAuthorizationPolicy = AllowEveryone.Policy, services: AllowEveryone.Add);

        List<string> anonymous = await McpTestHost.ToolNames(await host.Connect(role: null));

        anonymous.Should().BeEquivalentTo(OperateTools,
            "a confirmation is bound to the caller, and an anonymous caller is nobody in particular");
    }

    [Test]
    public async Task AnOperationsOverrideMovesAToolWhereItMovesTheRoute()
    {
        host = await McpTestHost.Start(roles: roles => roles.Operations["UnscheduleJob"] = SchedulerAccess.Operate);

        List<string> tools = await McpTestHost.ToolNames(await host.Connect(RoleAuthenticationExtensions.Operator));

        tools.Should().Contain("unschedule", "the override names the route the tool calls").And.NotContain("reschedule");
    }

    [Test]
    public async Task AnAgentsMaxAccessHidesTheChangesOnItsSchedulersAlone()
    {
        // Only the agent's scheduler in sight: the scheduler policy lets an administrator see target a1 and nothing else.
        host = await McpTestHost.Start(
            mcp: options => options.SchedulerAuthorizationPolicy = OnlyTheAgent,
            services: services => services.AddAuthorizationBuilder().AddPolicy(OnlyTheAgent, policy => policy.RequireAssertion(static context =>
                context.Resource is SchedulerResource { Target: "a1" })));
        BindAgent(host, SchedulerAccess.View);

        (await McpTestHost.ToolNames(await host.Connect(RoleAuthenticationExtensions.Administrator))).Should().BeEquivalentTo(ViewTools,
            "the agent said MaxAccess = View, and no role here raises a target's own ceiling");
    }

    [Test]
    public async Task AnAgentsCeilingBindsItsOwnSchedulersAndNoOthers()
    {
        host = await McpTestHost.Start();
        BindAgent(host, SchedulerAccess.View);

        (await McpTestHost.ToolNames(await host.Connect(RoleAuthenticationExtensions.Administrator))).Should().HaveCount(19,
            "the application's own scheduler takes changes, so they are still offered");
    }

    private const string OnlyTheAgent = "OnlyTheAgent";

    private static void BindAgent(McpTestHost host, SchedulerAccess maxAccess)
    {
        IScheduler agentScheduler = A.Fake<IScheduler>();
        A.CallTo(() => agentScheduler.SchedulerName).Returns("AgentScheduler");
        host.Services.GetRequiredService<SchedulerTargets>().Add(new SchedulerTarget
        {
            Name = "a1",
            Origin = SchedulerOrigin.Agent,
            MaxAccess = () => maxAccess,
        });
        host.Services.GetRequiredService<ISchedulerRepository>().Bind(new FakeProxyScheduler(agentScheduler, "a1", SchedulerOrigin.Agent), "a1");
    }
}

/// <summary>An operation policy that allows every caller, signed in or not.</summary>
internal static class AllowEveryone
{
    public const string Policy = "Everyone";

    public static void Add(IServiceCollection services)
    {
        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy.RequireAssertion(static _ => true));
    }
}
