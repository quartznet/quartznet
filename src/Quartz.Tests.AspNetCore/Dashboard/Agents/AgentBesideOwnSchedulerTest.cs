using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// A process that hosts the dashboard, accepts agents and has a scheduler of its own under the agents'
/// name gets its own scheduler from its factory, however early an agent connected (#4322).
/// </summary>
public sealed class AgentBesideOwnSchedulerTest
{
    [Test]
    public async Task AnAgentRegisteredBeforeTheFirstGetSchedulerDoesNotStandInForTheOwnScheduler()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start(configureServices: services =>
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = AgentDashboard.SchedulerName)));

        AgentWorker worker = await dashboard.StartAgent("w1");

        ISchedulerRepository repository = dashboard.App.Services.GetRequiredService<ISchedulerRepository>();
        IScheduler? agent = repository.Lookup(AgentDashboard.SchedulerName, "w1");
        agent.Should().NotBeNull("the premise is an agent bound under the name before anything asked for the scheduler");

        IScheduler own = await dashboard.App.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            own.Should().NotBeSameAs(agent);
            own.Should().NotBeAssignableTo<IProxyScheduler>("the factory builds this process's scheduler, not a tunnel to the worker");
            own.SchedulerInstanceId.Should().NotBe(worker.Scheduler.SchedulerInstanceId);

            repository.Lookup(AgentDashboard.SchedulerName, "w1").Should().BeSameAs(agent, "the agent stays bound under its target");
            repository.Lookup(AgentDashboard.SchedulerName, own.SchedulerInstanceId).Should().BeSameAs(own);
            (await dashboard.App.Services.GetRequiredService<ISchedulerFactory>().GetScheduler()).Should().BeSameAs(own);
        }
        finally
        {
            await own.Shutdown();
        }
    }
}
