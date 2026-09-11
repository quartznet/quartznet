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
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Records what a scheduler has run and what it has missed into the container's
/// <see cref="IExecutionHistoryStore" />.
/// </summary>
/// <remarks>
/// Registered against every scheduler in the container by <c>AddQuartzExecutionHistory()</c>, and told
/// its own scheduler's name when it is initialized, which is what its rows are keyed by. It is the one
/// recorder: two of them writing the same events would double every row.
/// </remarks>
internal sealed class ExecutionHistoryPlugin : ISchedulerPlugin, IJobListener, ITriggerListener
{
    /// <summary>
    /// The name <c>AddQuartzExecutionHistory()</c> registers this under, and the marker that makes the
    /// registration idempotent.
    /// </summary>
    internal const string PluginName = "quartzExecutionHistory";

    private readonly IServiceProvider serviceProvider;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Takes the container the history store is resolved from, and the clock a misfire is stamped with.
    /// </summary>
    /// <remarks>
    /// The clock is this scheduler's: a named scheduler is built through a provider that resolves its own
    /// parts, so a scheduler given a <see cref="System.TimeProvider" /> of its own stamps its misfires
    /// with it. An execution needs no clock — it carries the fire time the scheduler already recorded.
    /// </remarks>
    public ExecutionHistoryPlugin(IServiceProvider serviceProvider, TimeProvider timeProvider)
    {
        this.serviceProvider = serviceProvider;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name { get; private set; } = PluginName;

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
            IExecutionHistoryStore? store = Store();
            if (store is null)
            {
                return default;
            }

            ExecutionHistoryEntry entry = new(
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
        return new ValueTask<bool>(false);
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
            IExecutionHistoryStore? store = Store();
            if (store is null)
            {
                return default;
            }

            MisfireHistoryEntry entry = new(
                SchedulerName: scheduler.SchedulerName,
                SchedulerInstanceId: scheduler.SchedulerInstanceId,
                TriggerGroup: trigger.Key.Group,
                TriggerName: trigger.Key.Name,
                JobKey: trigger.JobKey,
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
    /// The store to record into, or <see langword="null" /> when there is none to record into and when
    /// the bounds say to record nothing.
    /// </summary>
    /// <remarks>
    /// Resolved per event and allowed to be absent: a container with no history store is a reason to
    /// record nothing, not a reason to fail the execution that just finished. Nor is a container the
    /// host has begun disposing, which is what the callers' <see cref="ObjectDisposedException" />
    /// handlers are for.
    /// <para>
    /// <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" /> of <c>0</c> is the opt-out, and it
    /// is honoured here rather than in the store: a process that does not want a history should not
    /// build the rows a store would immediately discard. It is read per event so that it can be turned
    /// off and on again while the process runs.
    /// </para>
    /// </remarks>
    private IExecutionHistoryStore? Store()
    {
        IOptions<ExecutionHistoryOptions>? options = serviceProvider.GetService<IOptions<ExecutionHistoryOptions>>();
        if (options is not null && options.Value.MaxEntriesPerScheduler <= 0)
        {
            return null;
        }

        return serviceProvider.GetService<IExecutionHistoryStore>();
    }
}
