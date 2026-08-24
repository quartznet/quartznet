#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace Quartz.Dashboard.Services;

/// <summary>
/// The live-events hub, as the Live Logs page uses it.
/// </summary>
/// <remarks>
/// The page used to build a <see cref="HubConnection" /> itself, which made rendering it the same thing
/// as opening a socket: nothing could exercise the page without a server on the other end. This names
/// the four members it actually calls, so the transport is something the page is handed rather than
/// something it constructs.
/// </remarks>
internal interface IDashboardLiveConnection : IAsyncDisposable
{
    HubConnectionState State { get; }

    /// <summary>
    /// Registers a handler for one server-sent event. The payload arrives as the hub serialized it.
    /// </summary>
    void On(string methodName, Func<object?, Task> handler);

    ValueTask Start(CancellationToken cancellationToken = default);

    ValueTask Invoke(string methodName, string argument, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds a connection to the live-events hub at a given address.
/// </summary>
/// <remarks>
/// A factory rather than a registered connection: the page rebuilds its connection whenever the monitor
/// finds it disconnected, and the address is derived from the request it is rendering under.
/// </remarks>
internal interface IDashboardLiveConnectionFactory
{
    /// <param name="hubUri">The hub's address, as the browser would reach it.</param>
    /// <param name="cookieHeader">The cookies to forward, so the hub authenticates the same visitor
    /// the page was rendered for.</param>
    /// <param name="stateChanged">Called when the connection dropped or started reconnecting.</param>
    /// <param name="reconnected">Called when a dropped connection came back, which is when the page has
    /// to re-join its scheduler.</param>
    IDashboardLiveConnection Create(
        Uri hubUri,
        string? cookieHeader,
        Func<Task> stateChanged,
        Func<Task> reconnected);
}

internal sealed class SignalRDashboardLiveConnectionFactory : IDashboardLiveConnectionFactory
{
    public IDashboardLiveConnection Create(
        Uri hubUri,
        string? cookieHeader,
        Func<Task> stateChanged,
        Func<Task> reconnected)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(hubUri, connectionOptions => ForwardCookies(connectionOptions, cookieHeader))
            .WithAutomaticReconnect()
            .Build();

        return new SignalRDashboardLiveConnection(connection, stateChanged, reconnected);
    }

    /// <summary>
    /// Forwards the visitor's cookies to the hub, so it authenticates the same visitor the page was
    /// rendered for. A separate member because it is the factory's only decision, and the options it
    /// shapes are otherwise reachable only from inside a built <see cref="HubConnection" />.
    /// </summary>
    internal static void ForwardCookies(HttpConnectionOptions connectionOptions, string? cookieHeader)
    {
        ArgumentNullException.ThrowIfNull(connectionOptions);

        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            connectionOptions.Headers["Cookie"] = cookieHeader;
        }
    }
}

internal sealed class SignalRDashboardLiveConnection : IDashboardLiveConnection
{
    private readonly HubConnection connection;

    public SignalRDashboardLiveConnection(HubConnection connection, Func<Task> stateChanged, Func<Task> reconnected)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(stateChanged);
        ArgumentNullException.ThrowIfNull(reconnected);

        this.connection = connection;
        connection.Reconnecting += _ => stateChanged();
        connection.Closed += _ => stateChanged();
        connection.Reconnected += _ => reconnected();
    }

    public HubConnectionState State => connection.State;

    public void On(string methodName, Func<object?, Task> handler)
    {
        connection.On<object>(methodName, payload => handler(payload));
    }

    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return new ValueTask(connection.StartAsync(cancellationToken));
    }

    public ValueTask Invoke(string methodName, string argument, CancellationToken cancellationToken = default)
    {
        return new ValueTask(connection.InvokeAsync(methodName, argument, cancellationToken));
    }

    public ValueTask DisposeAsync()
    {
        return connection.DisposeAsync();
    }
}
