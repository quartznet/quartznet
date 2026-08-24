using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Hubs;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// SignalR reaches a typed client by emitting a proxy into a dynamic assembly of its own, so
/// <see cref="IQuartzDashboardHubClient" /> has to be public even though nothing outside
/// Quartz.Dashboard calls it: the proxy cannot implement an interface it cannot see, and a
/// strong-named assembly cannot grant <c>InternalsVisibleTo</c> to an assembly with no public key.
/// Making it internal fails at run time, not at compile time, which is what this test is here to
/// catch.
/// </summary>
public class QuartzDashboardHubClientProxyTest
{
    [Test]
    public void SignalRCanProxyTheTypedClientInterface()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartzDashboard();

        using WebApplication app = builder.Build();

        IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient> hubContext =
            app.Services.GetRequiredService<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>();

        // Touching Clients is what makes SignalR emit the proxy; sending is what invokes it.
        IQuartzDashboardHubClient client = hubContext.Clients.All;

        var act = async () => await client.SchedulerStateChanged(new SchedulerStateDto("scheduler", SchedulerStatus.Running));

        act.Should().NotThrowAsync(
            "the typed-client proxy lives in a dynamic assembly that can only implement a public interface");
    }
}
