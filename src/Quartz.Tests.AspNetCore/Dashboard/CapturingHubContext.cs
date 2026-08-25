using Microsoft.AspNetCore.SignalR;

using Quartz.Dashboard.Hubs;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// A hub context that hands out the clients it was given, so a test can read what the live-events
/// plugin pushed.
/// </summary>
/// <remarks>
/// Written out rather than faked: the hub is internal, so a dynamic proxy over
/// <see cref="IHubContext{THub, T}" /> closed over it cannot be emitted.
/// </remarks>
internal sealed class CapturingHubContext : IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>
{
    public CapturingHubContext(IHubClients<IQuartzDashboardHubClient> clients)
    {
        Clients = clients;
    }

    public IHubClients<IQuartzDashboardHubClient> Clients { get; }

    public IGroupManager Groups => throw new NotSupportedException("the plugin sends to groups, it does not manage them");
}
