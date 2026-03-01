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
    public static IServiceProvider? ServiceProvider { get; set; }

    private IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>? hubContext;
    private string schedulerName = string.Empty;

    public string Name { get; private set; } = "QuartzDashboardLiveEvents";

    public Task Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        schedulerName = scheduler.SchedulerName;

        scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
        scheduler.ListenerManager.AddTriggerListener(this, EverythingMatcher<TriggerKey>.AllTriggers());
        scheduler.ListenerManager.AddSchedulerListener(this);

        return Task.CompletedTask;
    }

    public Task Start(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task Shutdown(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobEventDto payload = new(
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            TriggerKey: new TriggerKeyDto(context.Trigger.Key.Group, context.Trigger.Key.Name),
            FireTimeUtc: context.FireTimeUtc,
            FireInstanceId: context.FireInstanceId);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.JobExecuting(payload));
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
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

    public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
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

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: new JobKeyDto(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
            FireTimeUtc: context.FireTimeUtc);

        return BroadcastToScheduler(context.Scheduler.SchedulerName, client => client.TriggerFired(payload));
    }

    public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        TriggerEventDto payload = new(
            TriggerKey: new TriggerKeyDto(trigger.Key.Group, trigger.Key.Name),
            JobKey: trigger.JobKey is null ? null : new JobKeyDto(trigger.JobKey.Group, trigger.JobKey.Name),
            FireTimeUtc: null);

        return BroadcastToScheduler(schedulerName, client => client.TriggerMisfired(payload));
    }

    public Task TriggerComplete(
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

    public Task JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        TriggerKeyDto payload = new(triggerKey.Group, triggerKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.TriggerPaused(payload));
    }

    public Task TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        TriggerKeyDto payload = new(triggerKey.Group, triggerKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.TriggerResumed(payload));
    }

    public Task TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobPaused(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        JobKeyDto payload = new(jobKey.Group, jobKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.JobPaused(payload));
    }

    public Task JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobsPaused(string jobGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task JobResumed(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        JobKeyDto payload = new(jobKey.Group, jobKey.Name);
        return BroadcastToScheduler(schedulerName, client => client.JobResumed(payload));
    }

    public Task JobsResumed(string jobGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
    {
        SchedulerErrorDto payload = new(schedulerName, msg, cause.Message);
        return BroadcastToScheduler(schedulerName, client => client.SchedulerError(payload));
    }

    public Task SchedulerInStandbyMode(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Standby");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public Task SchedulerStarted(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Started");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public Task SchedulerStarting(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Starting");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public Task SchedulerShutdown(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "Shutdown");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public Task SchedulerShuttingdown(CancellationToken cancellationToken = default)
    {
        SchedulerStateDto payload = new(schedulerName, "ShuttingDown");
        return BroadcastToScheduler(schedulerName, client => client.SchedulerStateChanged(payload));
    }

    public Task SchedulingDataCleared(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private Task BroadcastToScheduler(string scheduler, Func<IQuartzDashboardHubClient, Task> send)
    {
        if (string.IsNullOrWhiteSpace(scheduler))
        {
            return Task.CompletedTask;
        }

        // Lazily resolve hub context — ServiceProvider is set by MapQuartzDashboard()
        // which runs after the scheduler (and this plugin) is initialized
        hubContext ??= ServiceProvider?.GetService<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>();

        if (hubContext is null)
        {
            return Task.CompletedTask;
        }

        Task task = send(hubContext.Clients.Group(scheduler));
        return task;
    }
}
