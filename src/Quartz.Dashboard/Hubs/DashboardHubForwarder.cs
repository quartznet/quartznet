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

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Configuration;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Dashboard.Hubs;

/// <summary>
/// Feeds the dashboard's hub from Quartz's event stream, so that anyone connected to the hub goes on
/// receiving what they always did.
/// </summary>
/// <remarks>
/// <para>
/// The hub used to be written to directly, by a plugin installed into every scheduler. The events are
/// Quartz's now — one stream per scheduler, in core, reachable over HTTP as well as in process — and this
/// is the adapter between the two: one subscription per scheduler, each event sent to the hub group named
/// after that scheduler, in the payload records the hub has always carried.
/// </para>
/// <para>
/// It subscribes when the first connection joins a scheduler rather than at startup. A subscriber is what
/// makes a scheduler build its events at all, so a process whose hub nobody has connected to pays nothing
/// for this — and the dashboard's own pages read the stream directly, so they are not what keeps it open.
/// </para>
/// <para>
/// A pump lasts as long as the dashboard lists its scheduler. A stream that ends while the scheduler is
/// listed is subscribed to again after a wait, so the connections in the group, which nothing tells to
/// join again, go on receiving: that is an agent redeployed under the same target. A scheduler that is no
/// longer listed, such as an agent forgotten after <c>ForgetAfter</c>, ends its pump, and the pump leaves
/// this forwarder, so the next join starts a new one. A join for a name the dashboard does not list starts
/// nothing, which bounds the pumps by the schedulers the dashboard knows rather than by the names a client
/// sends.
/// </para>
/// <para>
/// Three kinds are not forwarded: <see cref="SchedulerEventKind.JobInterrupted" /> and
/// <see cref="SchedulerEventKind.TriggerInError" />, which the hub has no method for and which a 4.0
/// client would not know what to do with, and <see cref="SchedulerEventKind.Heartbeat" />, which is the
/// transport talking about itself.
/// </para>
/// </remarks>
internal sealed class DashboardHubForwarder : IAsyncDisposable
{
    /// <summary>
    /// How long a pump waits before it subscribes again after its stream ended, the first time.
    /// </summary>
    /// <remarks>
    /// Long enough that a target which fails at once is not asked again in a loop, and short enough that a
    /// redeployed agent's events reach the group a few seconds after it registers. The wait doubles while
    /// subscriptions end without delivering anything, up to <see cref="MaxRestartDelay" />.
    /// </remarks>
    internal static readonly TimeSpan FirstRestartDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest a pump waits between two subscriptions.
    /// </summary>
    internal static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<DashboardHubForwarder> logger;
    private readonly TimeProvider timeProvider;
    private readonly ConcurrentDictionary<string, Task> forwarding = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock starting = new();
    private readonly CancellationTokenSource stopping = new();

    /// <summary>
    /// The token <see cref="stopping" /> hands out, read once: a join that races disposal reads it after
    /// the source is disposed, which <see cref="CancellationTokenSource.Token" /> refuses.
    /// </summary>
    private readonly CancellationToken stoppingToken;

    private IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>? hubContext;

    /// <summary>
    /// Takes the container the event sources, the listing, the clock and the hub are resolved from.
    /// </summary>
    /// <remarks>
    /// The provider rather than any of them: the hub context exists only once SignalR has been mapped,
    /// and which source answers for a scheduler depends on whether that scheduler is in this process.
    /// </remarks>
    public DashboardHubForwarder(IServiceProvider serviceProvider, ILogger<DashboardHubForwarder> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
        timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        stoppingToken = stopping.Token;
    }

    /// <summary>
    /// Starts forwarding <paramref name="schedulerName" />'s events into the hub group of that name, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the hub when a connection joins a scheduler, which it does after authorizing the join —
    /// so nothing is subscribed to on behalf of a caller who may not see it. Calling it again for the same
    /// scheduler does nothing while its pump runs: one subscription feeds every connection in the group.
    /// A name the dashboard does not list starts nothing, and starts a pump on the first join after it is
    /// listed.
    /// </para>
    /// <para>
    /// The pump is started under a lock rather than in a dictionary's value factory, which may run twice
    /// for one key: the losing pump would run unrecorded and send every event to the group a second time.
    /// </para>
    /// </remarks>
    public void Forward(string schedulerName)
    {
        if (string.IsNullOrWhiteSpace(schedulerName) || stoppingToken.IsCancellationRequested || forwarding.ContainsKey(schedulerName))
        {
            return;
        }

        if (!Lists(schedulerName))
        {
            return;
        }

        lock (starting)
        {
            if (stoppingToken.IsCancellationRequested || forwarding.ContainsKey(schedulerName))
            {
                return;
            }

            // The token is given to Task.Run as well as to the pump, so a join that raced disposal gets a
            // cancelled task rather than a pump reading a source the container is tearing down.
            Task pump = Task.Run(() => Pump(schedulerName, stoppingToken), stoppingToken);
            forwarding[schedulerName] = pump;

            // Registered once the pump is recorded, so that a pump which has already ended is removed
            // rather than left behind. Removal compares the task: the entry is this pump's or nothing.
            _ = pump.ContinueWith(
                static (ended, state) =>
                {
                    (DashboardHubForwarder forwarder, string name) = ((DashboardHubForwarder, string)) state!;
                    forwarder.forwarding.TryRemove(new KeyValuePair<string, Task>(name, ended));
                },
                (this, schedulerName),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Whether this forwarder holds a pump for <paramref name="schedulerName" />.
    /// </summary>
    internal bool IsForwarding(string schedulerName) => forwarding.ContainsKey(schedulerName);

    /// <summary>
    /// Whether the dashboard lists the scheduler <paramref name="schedulerKey" /> names, which is what a
    /// pump is started for and kept running for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of the container's singletons rather than of a circuit's <see cref="SchedulerState" />, which
    /// a hub connection does not have. Every scheduler the picker lists a live row for is bound in the
    /// repository: this process's own, the proxy of each HTTP target and of each cluster, each window
    /// onto an attached store, and each agent's scheduler, which the agent registry binds under the
    /// agent's target with the target itself, and removes with it when the agent is replaced or
    /// forgotten. So the key is resolved by the rule every page resolves one by.
    /// </para>
    /// <para>
    /// A container being torn down answers <see cref="ObjectDisposedException" />, which the pump treats as
    /// the ending it is.
    /// </para>
    /// </remarks>
    private bool Lists(string schedulerKey)
    {
        return serviceProvider.GetService<ISchedulerRepository>() is { } repository
            && SchedulerLookup.Resolve(repository, serviceProvider.GetService<SchedulerWindowRegistry>(), SchedulerRef.Parse(schedulerKey)) is not null;
    }

    /// <summary>
    /// Reads one scheduler's events for as long as the dashboard lists it, sending each to the hub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stream that completes, or fails with anything <see cref="IsFinal" /> does not call final, is
    /// subscribed to again after a wait that starts at <see cref="FirstRestartDelay" /> and doubles up to
    /// <see cref="MaxRestartDelay" />. A subscription that delivered an event starts the wait over.
    /// </para>
    /// <para>
    /// Whether the scheduler is still listed is asked once the wait is over: a scheduler that went during
    /// it is not subscribed to, and one listed again by then, an agent back under its target, is.
    /// </para>
    /// <para>
    /// A failure is logged once per run of them: a target that keeps refusing is asked again at least once
    /// a minute, and logging each refusal would write the same warning that often. A subscription that
    /// delivers an event ends the run.
    /// </para>
    /// </remarks>
    private async Task Pump(string schedulerName, CancellationToken cancellationToken)
    {
        TimeSpan restartIn = FirstRestartDelay;
        bool failing = false;

        try
        {
            while (true)
            {
                Ending ending = await Read(schedulerName, cancellationToken).ConfigureAwait(false);
                if (ending.Final || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (ending.Delivered)
                {
                    restartIn = FirstRestartDelay;
                    failing = false;
                }

                if (ending.Failure is { } failure && !failing)
                {
                    failing = true;
                    logger.HubForwardingFailed(schedulerName, failure);
                }

                await Task.Delay(restartIn, timeProvider, cancellationToken).ConfigureAwait(false);
                restartIn = TimeSpan.FromTicks(Math.Min(restartIn.Ticks * 2, MaxRestartDelay.Ticks));

                if (!Lists(schedulerName))
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (IsFinal(exception, cancellationToken))
        {
            // The host stopped while the pump waited, or the container went with it.
        }
    }

    /// <summary>
    /// Reads one subscription to its end, sending each event to the hub, and says how it ended.
    /// </summary>
    private async Task<Ending> Read(string schedulerName, CancellationToken cancellationToken)
    {
        bool delivered = false;
        try
        {
            // A window onto an attached store has no source, and nothing to forward from.
            ISchedulerEventSource? source = SchedulerEventLookup.For(serviceProvider, schedulerName);
            if (source is null)
            {
                return Ending.ForGood;
            }

            await foreach (SchedulerEvent schedulerEvent in source.Subscribe(schedulerName, cancellationToken).ConfigureAwait(false))
            {
                delivered = true;
                await Send(schedulerName, schedulerEvent).ConfigureAwait(false);
            }

            return new Ending(Final: false, delivered, Failure: null);
        }
        catch (Exception exception) when (IsFinal(exception, cancellationToken))
        {
            return Ending.ForGood;
        }
        catch (Exception exception)
        {
            // Anything else leaves the connections in the group receiving nothing until the pump has
            // subscribed again, which is the one failure a live view cannot show for itself.
            return new Ending(Final: false, delivered, exception);
        }
    }

    /// <summary>
    /// Whether <paramref name="exception" /> ends a pump for good rather than being a reason to subscribe
    /// again.
    /// </summary>
    /// <remarks>
    /// A target that serves no event stream says so with <see cref="NotSupportedException" />, and asking
    /// again would be told the same; the page a visitor is looking at reads the same source and says so. A
    /// container being torn down answers <see cref="ObjectDisposedException" />, and a host stopping cancels
    /// the token. A cancellation that is not this forwarder's, such as a request that timed out, is a failure
    /// like any other. None of these is logged.
    /// </remarks>
    private static bool IsFinal(Exception exception, CancellationToken cancellationToken)
    {
        return exception is NotSupportedException or ObjectDisposedException
               || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// How one subscription ended: for good, or in a way the pump subscribes again after.
    /// </summary>
    /// <param name="Final">Whether nothing is to be subscribed to again.</param>
    /// <param name="Delivered">Whether the subscription delivered an event before it ended.</param>
    /// <param name="Failure">What it failed with, or <see langword="null" /> when it completed.</param>
    private readonly record struct Ending(bool Final, bool Delivered, Exception? Failure)
    {
        public static Ending ForGood => new(Final: true, Delivered: false, Failure: null);
    }

    /// <summary>
    /// Sends one event to the hub group watching its scheduler, in the payload the hub's client interface
    /// declares for it.
    /// </summary>
    /// <remarks>
    /// The hub context is resolved on the first event and kept, as it was when a plugin wrote to the hub:
    /// an application that mapped no hub has none to send to, so the event is dropped rather than the
    /// stream broken.
    /// </remarks>
    private ValueTask Send(string schedulerName, SchedulerEvent schedulerEvent)
    {
        hubContext ??= serviceProvider.GetService<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>();
        if (hubContext is null)
        {
            return default;
        }

        IQuartzDashboardHubClient clients = hubContext.Clients.Group(schedulerName);

        return schedulerEvent.Kind switch
        {
            SchedulerEventKind.JobExecuting when schedulerEvent.JobKey is not null && schedulerEvent.TriggerKey is not null =>
                new ValueTask(clients.JobExecuting(new JobEventDto(
                    schedulerEvent.SchedulerInstanceId,
                    Job(schedulerEvent.JobKey),
                    Trigger(schedulerEvent.TriggerKey),
                    schedulerEvent.FireTimeUtc ?? default,
                    schedulerEvent.FireInstanceId))),

            SchedulerEventKind.JobExecuted when schedulerEvent.JobKey is not null && schedulerEvent.TriggerKey is not null =>
                new ValueTask(clients.JobExecuted(new JobExecutionResultDto(
                    schedulerEvent.SchedulerInstanceId,
                    Job(schedulerEvent.JobKey),
                    Trigger(schedulerEvent.TriggerKey),
                    schedulerEvent.FireTimeUtc ?? default,
                    schedulerEvent.RunTime ?? TimeSpan.Zero,
                    schedulerEvent.Vetoed ?? false,
                    schedulerEvent.ExceptionMessage))),

            SchedulerEventKind.TriggerFired when schedulerEvent.TriggerKey is not null =>
                new ValueTask(clients.TriggerFired(TriggerEvent(schedulerEvent))),

            SchedulerEventKind.TriggerCompleted when schedulerEvent.TriggerKey is not null =>
                new ValueTask(clients.TriggerCompleted(TriggerEvent(schedulerEvent))),

            SchedulerEventKind.TriggerMisfired when schedulerEvent.TriggerKey is not null =>
                new ValueTask(clients.TriggerMisfired(TriggerEvent(schedulerEvent))),

            SchedulerEventKind.TriggerPaused when schedulerEvent.TriggerKey is not null =>
                new ValueTask(clients.TriggerPaused(new TriggerLifecycleDto(
                    schedulerEvent.SchedulerInstanceId, Trigger(schedulerEvent.TriggerKey)))),

            SchedulerEventKind.TriggerResumed when schedulerEvent.TriggerKey is not null =>
                new ValueTask(clients.TriggerResumed(new TriggerLifecycleDto(
                    schedulerEvent.SchedulerInstanceId, Trigger(schedulerEvent.TriggerKey)))),

            SchedulerEventKind.JobPaused when schedulerEvent.JobKey is not null =>
                new ValueTask(clients.JobPaused(new JobLifecycleDto(
                    schedulerEvent.SchedulerInstanceId, Job(schedulerEvent.JobKey)))),

            SchedulerEventKind.JobResumed when schedulerEvent.JobKey is not null =>
                new ValueTask(clients.JobResumed(new JobLifecycleDto(
                    schedulerEvent.SchedulerInstanceId, Job(schedulerEvent.JobKey)))),

            SchedulerEventKind.SchedulerStateChanged when schedulerEvent.Status is not null =>
                new ValueTask(clients.SchedulerStateChanged(new SchedulerStateDto(
                    schedulerEvent.SchedulerName, schedulerEvent.SchedulerInstanceId, schedulerEvent.Status.Value))),

            SchedulerEventKind.SchedulerError =>
                new ValueTask(clients.SchedulerError(new SchedulerErrorDto(
                    schedulerEvent.SchedulerName,
                    schedulerEvent.SchedulerInstanceId,
                    schedulerEvent.Message ?? "",
                    schedulerEvent.Cause,
                    schedulerEvent.TriggerKey is null ? null : Trigger(schedulerEvent.TriggerKey),
                    schedulerEvent.JobKey is null ? null : Job(schedulerEvent.JobKey)))),

            _ => default
        };
    }

    private static TriggerEventDto TriggerEvent(SchedulerEvent schedulerEvent) => new(
        schedulerEvent.SchedulerInstanceId,
        Trigger(schedulerEvent.TriggerKey!),
        schedulerEvent.JobKey is null ? null : Job(schedulerEvent.JobKey),
        schedulerEvent.FireTimeUtc);

    /// <remarks>
    /// The dashboard's keys lead with the group and the wire's lead with the name, which is why this is a
    /// method rather than a cast.
    /// </remarks>
    private static JobKeyDto Job(KeyDto key) => new(key.Group, key.Name);

    /// <inheritdoc cref="Job" />
    private static TriggerKeyDto Trigger(KeyDto key) => new(key.Group, key.Name);

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);

        // Taken under the lock a pump is started under, after cancelling: a join that started a pump before
        // this is in the snapshot, and one after it finds the token cancelled and starts nothing. The pumps
        // leave the dictionary as they end, so it is read once rather than enumerated while it changes.
        Task[] pumps;
        lock (starting)
        {
            pumps = [.. forwarding.Values];
        }

        foreach (Task pump in pumps)
        {
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Every ending is an ending; see Pump. A pump that never started because the token was
                // already cancelled ends here too, as a cancelled task.
            }
        }

        forwarding.Clear();
        stopping.Dispose();
    }
}
