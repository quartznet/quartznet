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

using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.HttpApiContract;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// The agent's one connection to the dashboard: dialled, registered, kept alive, answered on, and dialled
/// again whenever it drops.
/// </summary>
/// <remarks>
/// <para>
/// Three loops share the connection. The main one dials, registers and waits for the connection to end;
/// the heartbeat one tells the dashboard the scheduler is alive every interval the dashboard asked for;
/// and <see cref="DashboardAgentOptions.MaxConcurrentOperations" /> workers take the dashboard's requests
/// off a queue and answer them, never on the receive loop, so a slow operation holds nothing else up.
/// </para>
/// <para>
/// The SignalR client reconnects a dropped connection on <see cref="AgentRetryPolicy" />, which never
/// gives up, and every reconnection re-registers: the hub keys an agent by its connection, and a new
/// connection is a new registration. A dashboard that was down when the worker started is dialled again
/// on <see cref="Reconnection" />'s backoff, which the client's policy does not cover because there was
/// never a connection to reconnect.
/// </para>
/// </remarks>
internal sealed class AgentConnection : IAsyncDisposable
{
    /// <summary>
    /// How long to wait before registering again after the dashboard refused: the holder of the target
    /// may be an instance that has gone, which the dashboard forgets on its own clock.
    /// </summary>
    internal static readonly TimeSpan RegistrationRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many requests may wait behind the ones running, per worker: a queue this deep absorbs a page
    /// render's burst of reads, and one deeper would hold requests the dashboard has long given up on.
    /// </summary>
    internal const int QueueFactor = 4;

    /// <summary>
    /// What a request answered the moment it arrives, because the queue is full, says.
    /// </summary>
    internal const string QueueFullDetail = "The agent's request queue is full; try again.";

    /// <summary>
    /// What the dashboard says when it drops a connection in favour of a newer one from the same instance.
    /// </summary>
    internal const string SupersededReason = "superseded";

    private readonly DashboardAgentOptions options;
    private readonly IScheduler scheduler;
    private readonly AgentCarrier carrier;
    private readonly SchedulerEventBroker broker;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly Uri endpoint;

    private readonly Channel<AgentRequest> requests;
    private readonly ConcurrentDictionary<string, PendingRequest> inFlight = new(StringComparer.Ordinal);
    private readonly Lock watchLock = new();

    private HubConnection? connection;
    private CancellationToken stopping;
    private TaskCompletionSource<string>? closeRequested;
    private TaskCompletionSource? closed;
    private CancellationTokenSource? session;
    private Task? heartbeats;
    private Task? registering;
    private CancellationTokenSource? watching;
    private Task? streaming;
    private string? refusedFor;
    private volatile bool registered;

    public AgentConnection(
        DashboardAgentOptions options,
        IScheduler scheduler,
        AgentCarrier carrier,
        SchedulerEventBroker broker,
        TimeProvider timeProvider,
        ILogger logger)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
        this.broker = broker ?? throw new ArgumentNullException(nameof(broker));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        endpoint = options.Endpoint ?? throw new ArgumentException("The agent's endpoint is required.", nameof(options));

        // Bounded, and never waited on: the receive loop writes with TryWrite, which under the Wait full
        // mode answers false to a full queue rather than blocking - the drop modes would answer true and
        // lose the request without a word. A write that finds the queue full is answered at once rather
        // than parked, so a dashboard that floods the agent is told so and every other message on the
        // connection - a cancel, a close - still gets through.
        requests = Channel.CreateBounded<AgentRequest>(new BoundedChannelOptions(Math.Max(1, options.MaxConcurrentOperations) * QueueFactor)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
    }

    /// <summary>
    /// Whether the dashboard has accepted this agent's registration on the current connection.
    /// </summary>
    internal bool IsRegistered => registered;

    /// <summary>
    /// Dials, registers and keeps the connection until <paramref name="stopping" /> says otherwise.
    /// </summary>
    public async Task RunAsync(CancellationToken stopping)
    {
        this.stopping = stopping;
        HubConnection hub = Build();
        connection = hub;

        List<Task> workers = [];
        for (int i = 0; i < options.MaxConcurrentOperations; i++)
        {
            workers.Add(Task.Run(() => Work(hub, stopping), stopping));
        }

        try
        {
            while (!stopping.IsCancellationRequested)
            {
                closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                closeRequested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                await Dial(hub, stopping).ConfigureAwait(false);

                using CancellationTokenSource connected = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                session = connected;
                registering = RegisterUntilAccepted(hub, connected.Token);

                Task ended = await Task.WhenAny(closed.Task, closeRequested.Task).ConfigureAwait(false);
                string? closeReason = null;
                if (ReferenceEquals(ended, closeRequested.Task))
                {
                    closeReason = await closeRequested.Task.ConfigureAwait(false);
                    await StopQuietly(hub).ConfigureAwait(false);
                }

                await connected.CancelAsync().ConfigureAwait(false);
                await EndSession().ConfigureAwait(false);

                if (closeReason is not null)
                {
                    // The dashboard closed this connection on purpose. Dialling straight back would have
                    // two instances misconfigured with one target supersede each other as fast as the
                    // network allows; a pause before the redial is what keeps that a log line a minute
                    // rather than a storm.
                    TimeSpan pause = string.Equals(closeReason, SupersededReason, StringComparison.Ordinal)
                        ? RegistrationRetryDelay
                        : Reconnection.FirstRetryDelay;
                    await Task.Delay(pause, timeProvider, stopping).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            requests.Writer.TryComplete();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells the dashboard this scheduler is shutting down, so it is listed as such rather than as
    /// unreachable, and waits at most a few seconds for the word to get through.
    /// </summary>
    public async ValueTask SayGoodbye(CancellationToken cancellationToken = default)
    {
        HubConnection? hub = connection;
        if (hub is null || hub.State != HubConnectionState.Connected || !registered)
        {
            return;
        }

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await hub.SendAsync(AgentProtocol.Unregister, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A goodbye that did not get through is a disconnect, which the dashboard handles as one.
        }
    }

    /// <summary>
    /// Stops the connection without a word, so the loop — once told to stop — ends.
    /// </summary>
    public async ValueTask Drop()
    {
        HubConnection? hub = connection;
        if (hub is not null)
        {
            await StopQuietly(hub).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        HubConnection? hub = connection;
        connection = null;
        if (hub is not null)
        {
            await hub.DisposeAsync().ConfigureAwait(false);
        }

        foreach (PendingRequest pending in inFlight.Values)
        {
            pending.Dispose();
        }
    }

    // --- Dialling ------------------------------------------------------------------------------------

    private HubConnection Build()
    {
        HubConnection hub = new HubConnectionBuilder()
            .WithUrl(endpoint, Configure)
            .WithAutomaticReconnect(AgentRetryPolicy.Instance)
            .AddJsonProtocol(json => AgentProtocol.ConfigureHubPayloads(json.PayloadSerializerOptions))
            .Build();

        hub.Reconnecting += OnReconnecting;
        hub.Reconnected += OnReconnected;
        hub.Closed += OnClosed;
        hub.On<AgentRequest>(AgentProtocol.Execute, OnExecute);
        hub.On<string>(AgentProtocol.Cancel, OnCancel);
        hub.On<bool>(AgentProtocol.Watch, OnWatch);
        hub.On<string>(AgentProtocol.Close, OnClose);

        return hub;
    }

    /// <summary>
    /// The token goes as an <c>Authorization: Bearer</c> header on every transport the .NET client has,
    /// never in the URL, which is where a query string ends up in access logs.
    /// </summary>
    private void Configure(HttpConnectionOptions http)
    {
        http.AccessTokenProvider = async () => options.AccessTokenProvider is { } provider
            ? await provider(CancellationToken.None).ConfigureAwait(false)
            : options.Token;

        options.ConfigureConnection?.Invoke(http);
    }

    /// <summary>
    /// Starts the connection, trying again on <see cref="Reconnection" />'s backoff for as long as it
    /// cannot be started, and saying so once per outage.
    /// </summary>
    private async Task Dial(HubConnection hub, CancellationToken stopping)
    {
        TimeSpan retryIn = Reconnection.FirstRetryDelay;
        DateTimeOffset? since = null;

        while (true)
        {
            stopping.ThrowIfCancellationRequested();

            try
            {
                await hub.StartAsync(stopping).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (since is null)
                {
                    since = timeProvider.GetUtcNow();
                    logger.Unreachable(endpoint, since.Value, exception);
                }
            }

            await Task.Delay(retryIn, timeProvider, stopping).ConfigureAwait(false);
            retryIn = Reconnection.Next(retryIn);
        }
    }

    private static async Task StopQuietly(HubConnection hub)
    {
        try
        {
            await hub.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Stopping a connection that has already gone is not a failure of anything.
        }
    }

    /// <summary>
    /// Ends what a connection carried: the heartbeats, the registration loop, the event stream and
    /// every request in flight, each of which is cancelled rather than answered.
    /// </summary>
    private async Task EndSession()
    {
        registered = false;
        StopWatching();

        // The session's token is what the heartbeat and registration loops run on; cancelled first, so
        // that observing them below is a wait for them to notice rather than for the next interval.
        if (session is { } current)
        {
            session = null;
            await current.CancelAsync().ConfigureAwait(false);
        }

        foreach (PendingRequest pending in inFlight.Values)
        {
            pending.Cancel();
        }

        await Observe(heartbeats).ConfigureAwait(false);
        await Observe(registering).ConfigureAwait(false);
        await Observe(streaming).ConfigureAwait(false);
        heartbeats = null;
        registering = null;
        streaming = null;
    }

    private static async Task Observe(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Ended by the session ending, which is what was asked for.
        }
    }

    // --- Registration and heartbeats ------------------------------------------------------------------

    /// <summary>
    /// Registers, and keeps registering every <see cref="RegistrationRetryDelay" /> while the dashboard
    /// refuses: the refusal may be a target held by an instance that has gone, and the dashboard forgets
    /// one on its own clock.
    /// </summary>
    private async Task RegisterUntilAccepted(HubConnection hub, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            AgentRegistered? answer = await TryRegister(hub, token).ConfigureAwait(false);
            if (answer is null)
            {
                return;
            }

            if (answer.Accepted)
            {
                refusedFor = null;
                registered = true;
                carrier.MaxMessageBytes = answer.MaxMessageBytes;
                logger.Registered(endpoint, options.Target ?? Environment.MachineName, scheduler.SchedulerName);

                // Nothing to do about a watch here: the one the old session carried ended with it, and the
                // dashboard says Watch again on this connection - before this answer arrives - when a
                // page is still looking.
                TimeSpan interval = answer.HeartbeatInterval > TimeSpan.Zero ? answer.HeartbeatInterval : options.HeartbeatInterval;
                heartbeats = Heartbeat(hub, interval, token);
                return;
            }

            string reason = answer.Reason ?? "no reason given";
            if (!string.Equals(reason, refusedFor, StringComparison.Ordinal))
            {
                refusedFor = reason;
                logger.RegistrationRefused(endpoint, reason);
            }

            try
            {
                await Task.Delay(RegistrationRetryDelay, timeProvider, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One registration, or <see langword="null" /> when the connection did not carry it — which the
    /// client's reconnection follows with a fresh one.
    /// </summary>
    private async Task<AgentRegistered?> TryRegister(HubConnection hub, CancellationToken token)
    {
        try
        {
            SchedulerMetadata metadata = await scheduler.GetMetadata(token).ConfigureAwait(false);
            AgentRegistration registration = new()
            {
                Target = options.Target ?? Environment.MachineName,
                SchedulerName = scheduler.SchedulerName,
                SchedulerInstanceId = metadata.SchedulerInstanceId,
                QuartzVersion = metadata.Version,
                Routes = AgentCarrier.RouteNames,
                ReadOnly = options.ReadOnly,
                HeartbeatInterval = options.HeartbeatInterval,
            };

            return await hub.InvokeAsync<AgentRegistered>(AgentProtocol.Register, registration, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The connection went while registering. The reconnection registers again.
            return null;
        }
    }

    private async Task Heartbeat(HubConnection hub, TimeSpan interval, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, timeProvider, token).ConfigureAwait(false);
                await hub.SendAsync(
                    AgentProtocol.Heartbeat,
                    new AgentHeartbeat { Status = scheduler.Status, SentAtUtc = timeProvider.GetUtcNow() },
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A heartbeat the connection did not carry: the dashboard notices the silence, and the
                // reconnection starts a new session with new heartbeats.
            }
        }
    }

    /// <summary>
    /// The transport dropped under the connection and the client is dialling again on its own: the
    /// registration is gone with the connection id, so the session it carried ends here, and
    /// <see cref="OnReconnected" /> starts a new one on the new id.
    /// </summary>
    private Task OnReconnecting(Exception? exception)
    {
        logger.ConnectionClosed(endpoint, exception?.Message ?? "the transport dropped");
        return EndSession();
    }

    private async Task OnReconnected(string? connectionId)
    {
        await EndSession().ConfigureAwait(false);

        if (closed is { Task.IsCompleted: false } && !closeRequested!.Task.IsCompleted)
        {
            CancellationTokenSource connected = new();
            session = connected;
            registering = RegisterUntilAccepted(connection!, connected.Token);
        }
    }

    private Task OnClosed(Exception? exception)
    {
        if (exception is not null)
        {
            logger.ConnectionClosed(endpoint, exception.Message);
        }

        closed?.TrySetResult();
        return Task.CompletedTask;
    }

    // --- What the dashboard sends ----------------------------------------------------------------------

    /// <summary>
    /// A request is in flight from the moment it arrives: its cancellation exists and its timeout runs
    /// before it is queued, so a <see cref="AgentProtocol.Cancel" /> for one still waiting behind the
    /// others lands, and a request the dashboard has given up on is never run late and unreported.
    /// </summary>
    private Task OnExecute(AgentRequest request)
    {
        // A timeout longer than a timer can wait would throw out of the CancellationTokenSource, on the
        // receive loop, with the request never answered: refused instead, in the words of a 400.
        if (request.Timeout > TimerLimits.MaxDelay)
        {
            return Refuse(request, HttpStatusCode.BadRequest, $"The request's timeout {request.Timeout} is longer than a timer can wait ({TimerLimits.MaxDelay}).");
        }

        PendingRequest pending = new(request, timeProvider, stopping);
        if (!inFlight.TryAdd(request.Id, pending))
        {
            // The id names a request still in flight; a second under it would orphan the first and run
            // both against the second's cancellation. The first stays as it is.
            pending.Dispose();
            return Refuse(request, HttpStatusCode.BadRequest, $"A request with id {request.Id} is already in flight.");
        }

        if (requests.Writer.TryWrite(request))
        {
            return Task.CompletedTask;
        }

        inFlight.TryRemove(request.Id, out _);
        pending.Dispose();
        return Refuse(request, HttpStatusCode.ServiceUnavailable, QueueFullDetail);
    }

    /// <summary>
    /// Answers a request the agent will not queue, from the receive loop, with problem details and a log
    /// line; the send waits for nothing but the transport's buffer.
    /// </summary>
    private Task Refuse(AgentRequest request, HttpStatusCode status, string detail)
    {
        logger.OperationRefused(request.Method + " " + request.Path, detail);

        HubConnection? hub = connection;
        return hub is null
            ? Task.CompletedTask
            : hub.SendAsync(AgentProtocol.Answer, AgentCarrier.Problem(request.Id, status, detail), stopping);
    }

    private void OnCancel(string id)
    {
        if (inFlight.TryGetValue(id, out PendingRequest? pending))
        {
            pending.Cancel();
        }
    }

    /// <summary>
    /// For a test: whether the request <paramref name="id" /> has been cancelled while the agent still
    /// holds it.
    /// </summary>
    internal bool IsCancelled(string id)
    {
        return inFlight.TryGetValue(id, out PendingRequest? pending) && pending.Token.IsCancellationRequested;
    }

    /// <summary>
    /// For a test: whether the agent still holds the request <paramref name="id" />, queued or running.
    /// </summary>
    internal bool Holds(string id) => inFlight.ContainsKey(id);

    /// <summary>
    /// For a test: how many requests the agent holds, queued and running.
    /// </summary>
    internal int PendingCount => inFlight.Count;

    private void OnWatch(bool watch)
    {
        if (watch)
        {
            StartWatching();
        }
        else
        {
            StopWatching();
        }
    }

    private void OnClose(string reason)
    {
        logger.ConnectionClosed(endpoint, reason);
        closeRequested?.TrySetResult(reason);
    }

    /// <summary>
    /// Takes requests off the queue and answers them, one at a time per worker, each under a cancellation
    /// of its own that <see cref="AgentProtocol.Cancel" /> and the request's own timeout can pull.
    /// </summary>
    private async Task Work(HubConnection hub, CancellationToken stopping)
    {
        await foreach (AgentRequest request in requests.Reader.ReadAllAsync(stopping).ConfigureAwait(false))
        {
            if (!inFlight.TryGetValue(request.Id, out PendingRequest? pending))
            {
                continue;
            }

            try
            {
                // Cancelled or timed out while it waited: the dashboard has already reported it, and
                // running it now would be a mutation nobody asked for any more.
                if (pending.Token.IsCancellationRequested)
                {
                    continue;
                }

                AgentAnswer answer = await carrier.Handle(request, pending.Token).ConfigureAwait(false);
                await hub.SendAsync(AgentProtocol.Answer, answer, pending.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Cancelled, timed out, or the connection went: there is nobody to answer. A fault inside
                // an operation is answered as problem details by the carrier and never reaches here.
            }
            finally
            {
                inFlight.TryRemove(request.Id, out _);
                pending.Dispose();
            }
        }
    }

    /// <summary>
    /// A request the agent holds, from receipt to answer: the cancellation the dashboard's
    /// <see cref="AgentProtocol.Cancel" /> pulls, linked to the request's own timeout on the scheduler's
    /// clock and to the agent stopping.
    /// </summary>
    private sealed class PendingRequest : IDisposable
    {
        private readonly CancellationTokenSource? timeout;
        private readonly CancellationTokenSource cancellation;
        private readonly Lock gate = new();
        private bool disposed;

        public PendingRequest(AgentRequest request, TimeProvider timeProvider, CancellationToken stopping)
        {
            timeout = request.Timeout > TimeSpan.Zero ? new CancellationTokenSource(request.Timeout, timeProvider) : null;
            cancellation = timeout is null
                ? CancellationTokenSource.CreateLinkedTokenSource(stopping)
                : CancellationTokenSource.CreateLinkedTokenSource(stopping, timeout.Token);
            Token = cancellation.Token;
        }

        public CancellationToken Token { get; }

        public void Cancel()
        {
            lock (gate)
            {
                if (!disposed)
                {
                    cancellation.Cancel();
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                cancellation.Dispose();
                timeout?.Dispose();
            }
        }
    }

    // --- Events -----------------------------------------------------------------------------------------

    /// <summary>
    /// Streams the scheduler's events up the connection until told to stop: the broker's own bounded
    /// channel is the backpressure, and a scheduler nobody watches streams nothing.
    /// </summary>
    private void StartWatching()
    {
        HubConnection? hub = connection;
        if (hub is null)
        {
            return;
        }

        lock (watchLock)
        {
            if (watching is not null)
            {
                return;
            }

            CancellationTokenSource stop = new();
            watching = stop;
            streaming = Stream(hub, stop.Token);
        }
    }

    private async Task Stream(HubConnection hub, CancellationToken token)
    {
        try
        {
            await hub.SendAsync(AgentProtocol.Events, broker.Subscribe(scheduler.SchedulerName, token), token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The watch was stopped, or the connection went; the dashboard asks again when it reconnects.
        }
    }

    private void StopWatching()
    {
        CancellationTokenSource? stop;
        lock (watchLock)
        {
            stop = watching;
            watching = null;
        }

        if (stop is not null)
        {
            stop.Cancel();
            stop.Dispose();
        }
    }

    /// <summary>
    /// For a test: the container's own view of whether anything is watching.
    /// </summary>
    internal bool IsWatching
    {
        get
        {
            lock (watchLock)
            {
                return watching is not null;
            }
        }
    }

    /// <summary>
    /// For the plugin's constructor dependency on the service provider, which the carrier reads.
    /// </summary>
    internal static IServiceProvider Services(IServiceProvider services) => services.GetRequiredService<IServiceProvider>();
}
