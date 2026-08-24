using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The SignalR-backed side of the live-events seam — the implementation the component tests replace
/// with a fake, and therefore the one piece of the dashboard they would otherwise never run.
/// </summary>
/// <remarks>
/// Nothing here needs a server: <see cref="HubConnectionBuilder.Build" /> assembles the connection
/// without contacting anything, and the connection only reaches the network from <c>StartAsync</c>,
/// which these tests deliberately never call.
/// </remarks>
public class DashboardLiveConnectionTest
{
    private static readonly Uri HubUri = new("http://localhost/quartz/hub");

    [Test]
    public async Task AFreshConnectionHasNotConnectedToAnything()
    {
        IDashboardLiveConnection connection = CreateConnection();

        connection.State.Should().Be(HubConnectionState.Disconnected,
            "building the connection must not reach the hub — the page builds one on every render pass "
            + "where its monitor finds the previous one gone");

        await connection.DisposeAsync();
    }

    [Test]
    public async Task HandlersRegisterBeforeThereIsAnythingToReceiveThem()
    {
        IDashboardLiveConnection connection = CreateConnection();

        Action act = () => connection.On("JobExecuted", _ => Task.CompletedTask);

        act.Should().NotThrow(
            "the page registers its eleven handlers between building the connection and starting it");

        await connection.DisposeAsync();
    }

    [Test]
    public async Task DisposingAConnectionThatNeverStartedCompletes()
    {
        IDashboardLiveConnection connection = CreateConnection();

        Func<Task> act = async () => await connection.DisposeAsync();

        await act.Should().NotThrowAsync(
            "a circuit torn down before the hub answered must not hang the disposal it is part of");
    }

    [Test]
    public void TheVisitorsCookiesAreForwardedToTheHub()
    {
        HttpConnectionOptions options = new();

        SignalRDashboardLiveConnectionFactory.ForwardCookies(options, "auth=abc; tenant=acme");

        options.Headers.Should().ContainKey("Cookie").WhoseValue.Should().Be("auth=abc; tenant=acme",
            "the hub authenticates the visitor the page was rendered for, and the Blazor circuit's own "
            + "cookies do not travel with a connection the server opens on its behalf");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void NoCookiesMeansNoCookieHeader(string? cookieHeader)
    {
        HttpConnectionOptions options = new();

        SignalRDashboardLiveConnectionFactory.ForwardCookies(options, cookieHeader);

        options.Headers.Should().NotContainKey("Cookie",
            "an empty Cookie header is not the same as none, and some proxies reject it");
    }

    [Test]
    public async Task AConnectionNeedsSomethingToWrapAndSomewhereToReport()
    {
        await using HubConnection hubConnection = new HubConnectionBuilder().WithUrl(HubUri).Build();

        Action withoutConnection = () => _ = new SignalRDashboardLiveConnection(null!, () => Task.CompletedTask, () => Task.CompletedTask);
        Action withoutStateChanged = () => _ = new SignalRDashboardLiveConnection(hubConnection, null!, () => Task.CompletedTask);
        Action withoutReconnected = () => _ = new SignalRDashboardLiveConnection(hubConnection, () => Task.CompletedTask, null!);

        withoutConnection.Should().Throw<ArgumentNullException>();
        withoutStateChanged.Should().Throw<ArgumentNullException>();
        withoutReconnected.Should().Throw<ArgumentNullException>();
    }

    private static IDashboardLiveConnection CreateConnection()
    {
        return new SignalRDashboardLiveConnectionFactory().Create(
            HubUri,
            "auth=abc",
            stateChanged: () => Task.CompletedTask,
            reconnected: () => Task.CompletedTask);
    }
}
