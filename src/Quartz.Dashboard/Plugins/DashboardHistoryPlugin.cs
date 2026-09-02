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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Services;
using Quartz.Extensibility;

namespace Quartz.Dashboard.Plugins;

/// <summary>
/// Records what a scheduler has run and what it has missed, so the history and misfire pages have
/// something to show.
/// </summary>
/// <remarks>
/// The rows go to the <see cref="IDashboardHistoryStore" /> in the container, which by default keeps
/// them in memory for <c>HistoryRetention</c> — a dashboard's history is an operator's recent view
/// rather than an audit log. Registered by <c>AddQuartzDashboard</c> against every scheduler in the
/// container, and told its own scheduler's name when it is initialized, which is what its rows are
/// keyed by.
/// </remarks>
public sealed class DashboardHistoryPlugin : ISchedulerPlugin, IJobListener, ITriggerListener
{
    private readonly IServiceProvider serviceProvider;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Takes the container the history store is resolved from, and the clock a misfire is stamped with.
    /// </summary>
    /// <remarks>
    /// A plugin is constructed by the container — this one is registered for every scheduler by
    /// <c>AddQuartzDashboard</c> — so it asks for what it needs the way any other component does. It used
    /// to read the container back out of <c>scheduler.Context["Quartz.ServiceProvider"]</c>, which put
    /// Quartz's plumbing into the application's own map, and left the scheduler-context endpoint of the
    /// HTTP API answering <c>500</c> for every scheduler a container had built.
    /// <para>
    /// The clock is this scheduler's: a named scheduler is built through a provider that resolves its own
    /// parts, so a scheduler given a <see cref="TimeProvider" /> of its own stamps its misfires with it.
    /// An execution needs no clock — it carries the fire time the scheduler already recorded.
    /// </para>
    /// </remarks>
    public DashboardHistoryPlugin(IServiceProvider serviceProvider, TimeProvider timeProvider)
    {
        this.serviceProvider = serviceProvider;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name { get; private set; } = "QuartzDashboardHistory";

    /// <inheritdoc />
    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        scheduler.ListenerManager.AddJobListener(this, Matchers.AllJobs());
        scheduler.ListenerManager.AddTriggerListener(this, Matchers.AllTriggers());
        return default;
    }

    /// <inheritdoc />
    public ValueTask Start(CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        try
        {
            IDashboardHistoryStore? store = Store();
            if (store is null)
            {
                return default;
            }

            DashboardHistoryEntry entry = new(
                SchedulerName: context.Scheduler.SchedulerName,
                SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
                JobGroup: context.JobDetail.Key.Group,
                JobName: context.JobDetail.Key.Name,
                TriggerGroup: context.Trigger.Key.Group,
                TriggerName: context.Trigger.Key.Name,
                FiredAtUtc: context.FireTimeUtc,
                Duration: context.JobRunTime,
                Succeeded: jobException is null,
                ExceptionMessage: jobException?.Message);

            return store.AddExecution(entry, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

    /// <inheritdoc />
    public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// Records a firing the scheduler missed.
    /// </summary>
    /// <remarks>
    /// A misfire never becomes an execution, so it is invisible in the execution history however long a
    /// reader stares at it. The scheduler notifies before it applies the trigger's misfire instruction,
    /// so <see cref="ITrigger.NextFireTimeUtc" /> is still the firing that was missed rather than the one
    /// it was rescheduled to.
    /// </remarks>
    public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        try
        {
            IDashboardHistoryStore? store = Store();
            if (store is null)
            {
                return default;
            }

            DashboardMisfireEntry entry = new(
                SchedulerName: scheduler.SchedulerName,
                SchedulerInstanceId: scheduler.SchedulerInstanceId,
                TriggerGroup: trigger.Key.Group,
                TriggerName: trigger.Key.Name,
                JobKey: trigger.JobKey is null ? null : new JobKeyDto(trigger.JobKey.Group, trigger.JobKey.Name),
                MisfiredAtUtc: timeProvider.GetUtcNow(),
                ScheduledFireTimeUtc: trigger.NextFireTimeUtc);

            return store.AddMisfire(entry, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return default;
        }
    }

    /// <inheritdoc />
    public ValueTask TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// The store to record into, or <see langword="null" /> when there is none.
    /// </summary>
    /// <remarks>
    /// Resolved per event and allowed to be absent, as when this went through the scheduler context: a
    /// dashboard that was never registered is a reason to record nothing, not a reason to fail the
    /// execution that just finished. Nor is a container the host has begun disposing, which is what the
    /// callers' <see cref="ObjectDisposedException" /> handlers are for.
    /// </remarks>
    private IDashboardHistoryStore? Store() => serviceProvider.GetService<IDashboardHistoryStore>();
}
