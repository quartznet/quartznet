using Microsoft.AspNetCore.Http.Connections;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// The transport production uses: a dashboard on Kestrel, dialled over a loopback socket with WebSockets.
/// Every other case here runs over the test server's long polling; this one pins that the header-only
/// token rule and the request round trip hold on the socket an agent really opens.
/// </summary>
public sealed class AgentWebSocketsTest
{
    [Test]
    public async Task OverKestrelAndWebSocketsAHeaderTokenRegistersAnOperationRoundTripsAndATokenInTheUrlDoesNot()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start(kestrel: true);
        dashboard.HubUri.Scheme.Should().Be("http");
        dashboard.HubUri.Host.Should().Be("127.0.0.1");

        AgentWorker worker = await dashboard.StartAgent("w1");

        (await dashboard.Client.GetScheduler(worker.Key)).SchedulerInstanceId.Should().Be(worker.Scheduler.SchedulerInstanceId,
            "a request went down the socket and its answer came back up it");
        dashboard.Logs.WithEventId(9111).Should().BeEmpty();

        // A refused agent redials on its policy - at once, over a socket - so the refusal is logged
        // more than once by the time anyone looks; what is asserted is that it was, and why.
        AgentWorker wrong = await dashboard.StartAgent("w2", options => options.Token = "not-the-token", waitUntilRegistered: false);
        await Eventually.Until(() => dashboard.Logs.WithEventId(9111).Any(entry => entry.Message.Contains("matches neither", StringComparison.Ordinal)));
        wrong.Plugin.Connection!.IsRegistered.Should().BeFalse();
        dashboard.Registry.Find("w2").Should().BeNull();

        AgentWorker inTheUrl = await dashboard.StartAgent("w3", options =>
        {
            options.Endpoint = new Uri(dashboard.HubUri + "?access_token=" + AgentDashboard.Token);
            options.ConfigureConnection = connection =>
            {
                connection.Transports = HttpTransportType.WebSockets;
                connection.AccessTokenProvider = null;
            };
        }, waitUntilRegistered: false);
        await Eventually.Until(() => dashboard.Logs.WithEventId(9111).Any(entry => entry.Message.Contains("no Authorization header", StringComparison.Ordinal)));
        inTheUrl.Plugin.Connection!.IsRegistered.Should().BeFalse("a token in the URL is not read over WebSockets either");
        dashboard.Registry.Find("w3").Should().BeNull();
    }
}
