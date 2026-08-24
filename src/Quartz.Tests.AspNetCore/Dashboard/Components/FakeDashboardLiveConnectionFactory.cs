using Microsoft.AspNetCore.SignalR.Client;

using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// A live-events connection that never leaves the process, so the Live Logs page can be rendered and
/// pushed events without a SignalR server behind it.
/// </summary>
internal sealed class FakeDashboardLiveConnectionFactory : IDashboardLiveConnectionFactory
{
    private readonly List<FakeDashboardLiveConnection> connections = [];

    /// <summary>
    /// Every connection the page has built, oldest first. The page rebuilds one whenever its monitor
    /// finds the previous disconnected, so a test asserting on reconnection reads more than one.
    /// </summary>
    public IReadOnlyList<FakeDashboardLiveConnection> Connections => connections;

    public FakeDashboardLiveConnection Current => connections[^1];

    public Uri? LastHubUri { get; private set; }

    public string? LastCookieHeader { get; private set; }

    /// <summary>
    /// What <see cref="IDashboardLiveConnection.Start" /> should throw, when a test is about the page's
    /// handling of a hub it cannot reach.
    /// </summary>
    public Exception? StartFailure { get; set; }

    public IDashboardLiveConnection Create(
        Uri hubUri,
        string? cookieHeader,
        Func<Task> stateChanged,
        Func<Task> reconnected)
    {
        LastHubUri = hubUri;
        LastCookieHeader = cookieHeader;

        FakeDashboardLiveConnection connection = new(stateChanged, reconnected, StartFailure);
        connections.Add(connection);
        return connection;
    }
}

internal sealed class FakeDashboardLiveConnection : IDashboardLiveConnection
{
    private readonly Dictionary<string, Func<object?, Task>> handlers = new(StringComparer.Ordinal);
    private readonly Func<Task> stateChanged;
    private readonly Func<Task> reconnected;
    private readonly Exception? startFailure;

    public FakeDashboardLiveConnection(Func<Task> stateChanged, Func<Task> reconnected, Exception? startFailure)
    {
        this.stateChanged = stateChanged;
        this.reconnected = reconnected;
        this.startFailure = startFailure;
    }

    public HubConnectionState State { get; private set; } = HubConnectionState.Disconnected;

    /// <summary>
    /// The hub methods the page called, in order — <c>JoinScheduler</c> and <c>LeaveScheduler</c> with
    /// the scheduler each one named.
    /// </summary>
    public List<(string Method, string Argument)> Invocations { get; } = [];

    public bool Disposed { get; private set; }

    public void On(string methodName, Func<object?, Task> handler)
    {
        handlers[methodName] = handler;
    }

    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        if (startFailure is not null)
        {
            return ValueTask.FromException(startFailure);
        }

        State = HubConnectionState.Connected;
        return ValueTask.CompletedTask;
    }

    public ValueTask Invoke(string methodName, string argument, CancellationToken cancellationToken = default)
    {
        Invocations.Add((methodName, argument));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Delivers one event to the page, the way the hub would.
    /// </summary>
    public Task Push(string eventType, object? payload)
    {
        return handlers.TryGetValue(eventType, out Func<object?, Task>? handler)
            ? handler(payload)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Drops the connection, the way a lost socket would.
    /// </summary>
    public Task Drop()
    {
        State = HubConnectionState.Disconnected;
        return stateChanged();
    }

    public Task Reconnect()
    {
        State = HubConnectionState.Connected;
        return reconnected();
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        State = HubConnectionState.Disconnected;
        return ValueTask.CompletedTask;
    }
}
