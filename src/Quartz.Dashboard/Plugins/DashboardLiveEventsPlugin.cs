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
using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.Dashboard.Plugins;

public sealed class DashboardLiveEventsPlugin : ISchedulerPlugin, IJobListener, ITriggerListener, ISchedulerListener
{
    private IScheduler? scheduler;
    private IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>? hubContext;
    private string schedulerName = string.Empty;

    public string Name { get; private set; } = "QuartzDashboardLiveEvents";

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        this.scheduler = scheduler;
        schedulerName = scheduler.SchedulerName;

        scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
        scheduler.ListenerManager.AddTriggerListener(this, EverythingMatcher<TriggerKey>.AllTriggers());
        scheduler.ListenerManager.AddSchedulerListener(this);

        return default;
    }

    public ValueTask Start(CancellationToken cancellationToken = default) => default;

    public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;

    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobEventDto payload = new(
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            TriggerKey: new TriggerKeyDto(context.Trigger.Key.Group, context.Trigger.Key.Name),
            FireTimeUtc: context.FireTimeUtc,
            FireInstanceId: context.FireInstanceId);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.JobExecuting(payload));
    }

    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobExecutionResultDto payload = new(
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            TriggerKey: new TriggerKeyDto(context.Trigger.Key.Group, context.Trigger.Key.Name),
            FireTimeUtc: context.FireTimeUtc,
            RunTimeMs: (long) context.JobRunTime.TotalMilliseconds,
            Vetoed: true,
            ExceptionMessage: null);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.JobExecuted(payload));
    }

    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        JobExecutionResultDto payload = new(
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            TriggerKey: new TriggerKeyDto(context.Trigger.Key.Group, context.Trigger.Key.Name),
            FireTimeUtc: context.FireTimeUtc,
            RunTimeMs: (long) context.JobRunTime.TotalMilliseconds,
            Vetoed: false,
            ExceptionMessage: jobException?.Message);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.JobExecuted(payload));
    }

    public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            FireTimeUtc: context.FireTimeUtc);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.TriggerFired(payload));
    }

    public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(false);
    }

    public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: trigger.JobKey is null ? null : new JobKeyDto(trigger.JobKey.Group, trigger.JobKey.Name),
            FireTimeUtc: null);

        return BroadcastToScheduler(schedulerName, client => client.TriggerMisfired(payload));
    }

    public ValueTask TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            FireTimeUtc: context.FireTimeUtc);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.TriggerCompleted(payload));
    }

    public ValueTask JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    public ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;

    public ValueTask TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    public ValueTask TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        TriggerKeyDto payload = new(triggerKey.Group, triggerKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.TriggerPaused(payload));
    }

    public ValueTask TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default) => default;

    public ValueTask TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        TriggerKeyDto payload = new(triggerKey.Group, triggerKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.TriggerResumed(payload));
    }

    public ValueTask TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default) => default;

    public ValueTask JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default) => default;

    public ValueTask JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    public ValueTask JobPaused(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        JobKeyDto payload = new(jobKey.Group, jobKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.JobPaused(payload));
    }

    public ValueTask JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    public ValueTask JobsPaused(string jobGroup, CancellationToken cancellationToken = default) => default;

    public ValueTask JobResumed(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        JobKeyDto payload = new(jobKey.Group, jobKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.JobResumed(payload));
    }

    public ValueTask JobsResumed(string jobGroup, CancellationToken cancellationToken = default) => default;

    public ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
    {
        SchedulerErrorDto payload = new(schedulerName, msg, cause.Message);
        return BroadcastToScheduler(schedulerName, client => client.SchedulerError(payload));
    }

    public ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Standby");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Started");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public ValueTask SchedulerStarting(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Starting");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public ValueTask SchedulerShutdown(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Shutdown");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "ShuttingDown");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public ValueTask SchedulingDataCleared(CancellationToken cancellationToken = default) => default;

    private async ValueTask BroadcastToScheduler(string schedulerName, Func<IQuartzDashboardHubClient, Task> send)
    {
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            return;
        }

        try
        {
            if (hubContext is null
                && scheduler?.Context is { } ctx
                && ctx.TryGetValue(DashboardPluginKeys.ServiceProvider, out var value)
                && value is IServiceProvider sp)
            {
                hubContext = sp.GetService<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>();
            }

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
