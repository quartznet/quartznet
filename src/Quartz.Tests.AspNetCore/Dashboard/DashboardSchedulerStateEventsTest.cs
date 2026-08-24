using FakeItEasy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Plugins;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// What the dashboard's live feed says a scheduler's state is.
/// </summary>
/// <remarks>
/// The payload used to be a phrase chosen at each call site, and a running scheduler was announced here
/// as <c>"Started"</c> while the same state was called <c>"Running"</c> by the HTTP API and by the
/// in-process client. It is a <see cref="SchedulerStatus" /> now, so there is one spelling and one
/// vocabulary — and an event that is not a state has nothing to push.
/// </remarks>
public class DashboardSchedulerStateEventsTest
{
    [Test]
    public async Task EachListenerEventPushesTheStateTheSchedulerIsNowIn()
    {
        (DashboardLiveEventsPlugin plugin, List<SchedulerStateDto> pushed) = await Plugin();

        await plugin.SchedulerStarted();
        await plugin.SchedulerInStandbyMode();
        await plugin.SchedulerShuttingDown();
        await plugin.SchedulerShutdown();

        pushed.Select(state => state.Status).Should().Equal(
            [
                SchedulerStatus.Running,
                SchedulerStatus.Standby,
                SchedulerStatus.ShuttingDown,
                SchedulerStatus.Shutdown
            ],
            "a listener event names the state the scheduler has arrived in, and the dashboard shows that state");

        pushed.Should().OnlyContain(state => state.SchedulerName == "TestScheduler");
    }

    [Test]
    public async Task ASchedulerThatIsStartingPushesNothing()
    {
        (DashboardLiveEventsPlugin plugin, List<SchedulerStateDto> pushed) = await Plugin();

        await plugin.SchedulerStarting();

        pushed.Should().BeEmpty(
            "starting is an event rather than a state, and the state it leads to arrives a moment later as "
            + "SchedulerStarted - pushing 'Starting' only gave a browser a value that is not a status to render");
    }

    private static async Task<(DashboardLiveEventsPlugin Plugin, List<SchedulerStateDto> Pushed)> Plugin()
    {
        List<SchedulerStateDto> pushed = [];

        IQuartzDashboardHubClient client = A.Fake<IQuartzDashboardHubClient>();
        A.CallTo(() => client.SchedulerStateChanged(A<SchedulerStateDto>._))
            .Invokes((SchedulerStateDto state) => pushed.Add(state))
            .Returns(Task.CompletedTask);

        IHubClients<IQuartzDashboardHubClient> clients = A.Fake<IHubClients<IQuartzDashboardHubClient>>();
        A.CallTo(() => clients.Group(A<string>._)).Returns(client);

        // Written out rather than faked: the hub is internal, so a dynamic proxy over
        // IHubContext<QuartzDashboardHub, …> cannot be emitted.
        IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient> hubContext = new CapturingHubContext(clients);

        ServiceCollection services = new();
        services.AddSingleton(hubContext);
        ServiceProvider provider = services.BuildServiceProvider();

        SchedulerContext context = new() { ["Quartz.ServiceProvider"] = provider };

        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("TestScheduler");
        A.CallTo(() => scheduler.Context).Returns(context);
        A.CallTo(() => scheduler.ListenerManager).Returns(A.Fake<IListenerManager>());

        DashboardLiveEventsPlugin plugin = new();
        await plugin.Initialize("live", scheduler);

        return (plugin, pushed);
    }

    private sealed class CapturingHubContext : IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>
    {
        public CapturingHubContext(IHubClients<IQuartzDashboardHubClient> clients)
        {
            Clients = clients;
        }

        public IHubClients<IQuartzDashboardHubClient> Clients { get; }

        public IGroupManager Groups => throw new NotSupportedException("the plugin sends to groups, it does not manage them");
    }
}
