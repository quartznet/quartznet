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

using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.HttpApiContract;

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
/// Three kinds are not forwarded: <see cref="SchedulerEventKind.JobInterrupted" /> and
/// <see cref="SchedulerEventKind.TriggerInError" />, which the hub has no method for and which a 4.0
/// client would not know what to do with, and <see cref="SchedulerEventKind.Heartbeat" />, which is the
/// transport talking about itself.
/// </para>
/// </remarks>
internal sealed class DashboardHubForwarder : IAsyncDisposable
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<DashboardHubForwarder> logger;
    private readonly ConcurrentDictionary<string, Task> forwarding = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource stopping = new();

    private IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>? hubContext;

    /// <summary>
    /// Takes the container the event sources and the hub are resolved from.
    /// </summary>
    /// <remarks>
    /// The provider rather than either of them: the hub context exists only once SignalR has been mapped,
    /// and which source answers for a scheduler depends on whether that scheduler is in this process.
    /// </remarks>
    public DashboardHubForwarder(IServiceProvider serviceProvider, ILogger<DashboardHubForwarder> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Starts forwarding <paramref name="schedulerName" />'s events into the hub group of that name, once.
    /// </summary>
    /// <remarks>
    /// Called by the hub when a connection joins a scheduler, which it does after authorizing the join —
    /// so nothing is subscribed to on behalf of a caller who may not see it. Calling it again for the same
    /// scheduler does nothing: one subscription feeds every connection in the group.
    /// </remarks>
    public void Forward(string schedulerName)
    {
        if (string.IsNullOrWhiteSpace(schedulerName) || stopping.IsCancellationRequested)
        {
            return;
        }

        // Discarded rather than awaited: the pump runs for the life of the process, and what it ends with
        // is either an ending it handles itself or the line Pump logs. The token is given to Task.Run as
        // well as to the pump, so a join that raced disposal gets a cancelled task rather than a pump
        // reading a source the container is tearing down — and its TaskCanceledException goes nowhere,
        // because nothing awaits what is recorded here except DisposeAsync, which expects it.
        _ = forwarding.GetOrAdd(schedulerName, name => Task.Run(() => Pump(name, stopping.Token), stopping.Token));
    }

    /// <summary>
    /// Reads one scheduler's events for as long as the process runs, sending each to the hub.
    /// </summary>
    /// <remarks>
    /// Every ending is an ending rather than a failure. A stream that completes is a scheduler that went
    /// away; a target that serves no event stream says so with <see cref="NotSupportedException" />, and
    /// there is nothing to forward from it; a container being disposed is a host shutting down. None of
    /// them is worth a log line here, because the page a visitor is looking at reads the same source and
    /// reports what it finds.
    /// </remarks>
    private async Task Pump(string schedulerName, CancellationToken cancellationToken)
    {
        try
        {
            ISchedulerEventSource? source = SchedulerEventSources.For(serviceProvider, schedulerName);
            if (source is null)
            {
                return;
            }

            await foreach (SchedulerEvent schedulerEvent in source.Subscribe(schedulerName, cancellationToken).ConfigureAwait(false))
            {
                await Send(schedulerName, schedulerEvent).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping, or this forwarder is being disposed. Both are endings.
        }
        catch (ObjectDisposedException)
        {
            // The container is being torn down underneath the subscription, which is the same ending.
        }
        catch (NotSupportedException)
        {
            // The target serves no event stream, so there is nothing here to forward from it. The page a
            // visitor is looking at reads the same source and says so.
        }
        catch (Exception exception)
        {
            // Anything else leaves the hub serving connections that will receive nothing, which is the one
            // failure a live view cannot show for itself.
            logger.HubForwardingStopped(schedulerName, exception);
        }
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

        foreach (KeyValuePair<string, Task> pump in forwarding)
        {
            try
            {
                await pump.Value.ConfigureAwait(false);
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
