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

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;

namespace Quartz.Dashboard.Plugins;

/// <summary>
/// Pushes a scheduler's events to the browsers watching it, which is what makes the dashboard's live
/// view live.
/// </summary>
/// <remarks>
/// It is all three listener kinds at once because the live view draws all three: a job starting and
/// finishing, a trigger firing and misfiring, and the scheduler's own lifecycle. Registered by
/// <c>AddQuartzDashboard</c> against every scheduler in the container rather than named by a
/// <c>quartz.plugin.*.type</c> key, and told its own scheduler's name when it is initialized — which is
/// the SignalR group it broadcasts to.
/// </remarks>
public sealed class DashboardLiveEventsPlugin : ISchedulerPlugin, IJobListener, ITriggerListener, ISchedulerListener
{
    private readonly IServiceProvider serviceProvider;
    private IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>? hubContext;

    /// <summary>
    /// Takes the container the dashboard's SignalR hub is resolved from.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="DashboardHistoryPlugin(IServiceProvider, TimeProvider)" path="/remarks" />
    /// </remarks>
    public DashboardLiveEventsPlugin(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name { get; private set; } = "QuartzDashboardLiveEvents";

    /// <inheritdoc />
    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;

        scheduler.ListenerManager.AddJobListener(this, Matchers.AllJobs());
        scheduler.ListenerManager.AddTriggerListener(this, Matchers.AllTriggers());
        scheduler.ListenerManager.AddSchedulerListener(this);

        return default;
    }

    /// <inheritdoc />
    public ValueTask Start(CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobEventDto payload = new(
            SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            TriggerKey: new TriggerKeyDto(context.Trigger.Key.Group, context.Trigger.Key.Name),
            FireTimeUtc: context.FireTimeUtc,
            FireInstanceId: context.FireInstanceId);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.JobExecuting(payload));
    }

    /// <inheritdoc />
    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobExecutionResultDto payload = new(
            SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            TriggerKey: new TriggerKeyDto(context.Trigger.Key.Group, context.Trigger.Key.Name),
            FireTimeUtc: context.FireTimeUtc,
            RunTime: context.JobRunTime,
            Vetoed: true,
            ExceptionMessage: null);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.JobExecuted(payload));
    }

    /// <inheritdoc />
    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        JobExecutionResultDto payload = new(
            SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            TriggerKey: new TriggerKeyDto(context.Trigger.Key.Group, context.Trigger.Key.Name),
            FireTimeUtc: context.FireTimeUtc,
            RunTime: context.JobRunTime,
            Vetoed: false,
            ExceptionMessage: jobException?.Message);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.JobExecuted(payload));
    }

    /// <inheritdoc />
    public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            FireTimeUtc: context.FireTimeUtc);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.TriggerFired(payload));
    }

    /// <inheritdoc />
    public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(false);
    }

    /// <inheritdoc />
    public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            SchedulerInstanceId: scheduler.SchedulerInstanceId,
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: trigger.JobKey is null ? null : new JobKeyDto(trigger.JobKey.Group, trigger.JobKey.Name),
            FireTimeUtc: null);

        return BroadcastToScheduler(scheduler.SchedulerName, client => client.TriggerMisfired(payload));
    }

    /// <inheritdoc />
    public ValueTask TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            FireTimeUtc: context.FireTimeUtc);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.TriggerCompleted(payload));
    }

    /// <inheritdoc />
    public ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobUnscheduled(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask TriggerFinalized(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        TriggerLifecycleDto payload = new(scheduler.SchedulerInstanceId, new TriggerKeyDto(triggerKey.Group, triggerKey.Name));
        return BroadcastToScheduler(scheduler.SchedulerName, client => client.TriggerPaused(payload));
    }

    /// <inheritdoc />
    public ValueTask TriggersPaused(IScheduler scheduler, string? triggerGroup, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask TriggerResumed(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        TriggerLifecycleDto payload = new(scheduler.SchedulerInstanceId, new TriggerKeyDto(triggerKey.Group, triggerKey.Name));
        return BroadcastToScheduler(scheduler.SchedulerName, client => client.TriggerResumed(payload));
    }

    /// <inheritdoc />
    public ValueTask TriggersResumed(IScheduler scheduler, string? triggerGroup, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobAdded(IScheduler scheduler, IJobDetail jobDetail, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobDeleted(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobPaused(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        JobLifecycleDto payload = new(scheduler.SchedulerInstanceId, new JobKeyDto(jobKey.Group, jobKey.Name));
        return BroadcastToScheduler(scheduler.SchedulerName, client => client.JobPaused(payload));
    }

    /// <inheritdoc />
    public ValueTask JobInterrupted(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobsPaused(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobResumed(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        JobLifecycleDto payload = new(scheduler.SchedulerInstanceId, new JobKeyDto(jobKey.Group, jobKey.Name));
        return BroadcastToScheduler(scheduler.SchedulerName, client => client.JobResumed(payload));
    }

    /// <inheritdoc />
    public ValueTask JobsResumed(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
    {
        SchedulerErrorDto payload = new(
            SchedulerName: scheduler.SchedulerName,
            SchedulerInstanceId: scheduler.SchedulerInstanceId,
            Message: errorContext.Message,
            Cause: errorContext.Exception.Message,
            TriggerKey: errorContext.TriggerKey is null ? null : new TriggerKeyDto(errorContext.TriggerKey.Group, errorContext.TriggerKey.Name),
            JobKey: errorContext.JobKey is null ? null : new JobKeyDto(errorContext.JobKey.Group, errorContext.JobKey.Name));

        return BroadcastToScheduler(scheduler.SchedulerName, client => client.SchedulerError(payload));
    }

    /// <inheritdoc />
    public ValueTask SchedulerInStandbyMode(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return BroadcastState(scheduler, SchedulerStatus.Standby);
    }

    /// <inheritdoc />
    public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return BroadcastState(scheduler, SchedulerStatus.Running);
    }

    /// <summary>
    /// Nothing is pushed: a scheduler that is starting is an event, not a state it is in.
    /// </summary>
    /// <remarks>
    /// The state it will be in arrives a moment later as <see cref="SchedulerStarted" />, and pushing
    /// "starting" first only gave a browser a value that is not a <see cref="SchedulerStatus" /> to
    /// render in the meantime.
    /// </remarks>
    public ValueTask SchedulerStarting(IScheduler scheduler, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return BroadcastState(scheduler, SchedulerStatus.Shutdown);
    }

    /// <inheritdoc />
    public ValueTask SchedulerShuttingDown(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return BroadcastState(scheduler, SchedulerStatus.ShuttingDown);
    }

    /// <summary>
    /// Pushes the state the scheduler is now in, which is what a listener event means.
    /// </summary>
    private ValueTask BroadcastState(IScheduler scheduler, SchedulerStatus status)
    {
        SchedulerStateDto payload = new(scheduler.SchedulerName, scheduler.SchedulerInstanceId, status);
        return BroadcastToScheduler(scheduler.SchedulerName, client => client.SchedulerStateChanged(payload));
    }

    /// <inheritdoc />
    public ValueTask SchedulingDataCleared(IScheduler scheduler, CancellationToken cancellationToken = default) => default;

    private async ValueTask BroadcastToScheduler(string schedulerName, Func<IQuartzDashboardHubClient, Task> send)
    {
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            return;
        }

        try
        {
            // Resolved on the first event and kept, as when this went through the scheduler context. An
            // application with no hub registered has none to broadcast to, so the event is dropped
            // rather than the execution failed.
            hubContext ??= serviceProvider.GetService<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>();

            if (hubContext is null)
            {
                return;
            }

            await send(hubContext.Clients.Group(schedulerName)).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Host is disposing — silently ignore, dashboard events are non-critical
        }
    }
}
