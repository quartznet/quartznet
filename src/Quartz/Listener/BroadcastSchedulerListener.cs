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

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Listener;

/// <summary>
/// Holds a List of references to SchedulerListener instances and broadcasts all
///  events to them (in order).
///</summary>
/// <remarks>
/// This may be more convenient than registering all of the listeners
/// directly with the Scheduler, and provides the flexibility of easily changing
/// which listeners get notified.
/// </remarks>
/// <see cref="AddListener" />
/// <see cref="RemoveListener" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class BroadcastSchedulerListener : ISchedulerListener
{
    private readonly List<ISchedulerListener> listeners;
    private readonly ILogger<BroadcastSchedulerListener> logger;

    public BroadcastSchedulerListener()
    {
        listeners = new List<ISchedulerListener>();
        logger = LogProvider.CreateLogger<BroadcastSchedulerListener>();
    }

    /// <summary>
    /// Construct an instance with the given List of listeners.
    /// </summary>
    /// <param name="listeners">The initial List of SchedulerListeners to broadcast to.</param>
    public BroadcastSchedulerListener(IEnumerable<ISchedulerListener> listeners) : this()
    {
        this.listeners.AddRange(listeners);
    }

    public void AddListener(ISchedulerListener listener)
    {
        listeners.Add(listener);
    }

    public bool RemoveListener(ISchedulerListener listener)
    {
        return listeners.Remove(listener);
    }

    public IReadOnlyList<ISchedulerListener> GetListeners()
    {
        return listeners;
    }

    public ValueTask JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobAdded(jobDetail, cancellationToken), nameof(JobAdded));
    }

    public ValueTask JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobDeleted(jobKey, cancellationToken), nameof(JobDeleted));
    }

    public ValueTask JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobScheduled(trigger, cancellationToken), nameof(JobScheduled));
    }

    public ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobUnscheduled(triggerKey, cancellationToken), nameof(JobUnscheduled));
    }

    public ValueTask TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerFinalized(trigger, cancellationToken), nameof(TriggerFinalized));
    }

    public ValueTask TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggersPaused(triggerGroup, cancellationToken), nameof(TriggersPaused));
    }

    public ValueTask TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerPaused(triggerKey, cancellationToken), nameof(TriggerPaused));
    }

    public ValueTask TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggersResumed(triggerGroup, cancellationToken), nameof(TriggerResumed));
    }

    public ValueTask SchedulingDataCleared(CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulingDataCleared(cancellationToken), nameof(SchedulingDataCleared));
    }

    public ValueTask TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerResumed(triggerKey, cancellationToken), nameof(TriggerResumed));
    }

    public ValueTask JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = new CancellationToken())
    {
        return IterateListenersInGuard(l => l.JobInterrupted(jobKey, cancellationToken), nameof(JobInterrupted));
    }

    public ValueTask JobsPaused(string jobGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobsPaused(jobGroup, cancellationToken), nameof(JobsPaused));
    }

    public ValueTask JobPaused(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobPaused(jobKey, cancellationToken), nameof(JobPaused));
    }

    public ValueTask JobsResumed(string jobGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobsResumed(jobGroup, cancellationToken), nameof(JobsResumed));
    }

    public ValueTask JobResumed(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobResumed(jobKey, cancellationToken), nameof(JobResumed));
    }

    public ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerError(msg, cause, cancellationToken), nameof(SchedulerError));
    }

    public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerStarted(cancellationToken), nameof(SchedulerStarted));
    }

    public ValueTask SchedulerStarting(CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerStarting(cancellationToken), nameof(SchedulerStarting));
    }

    public ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerInStandbyMode(cancellationToken), nameof(SchedulerInStandbyMode));
    }

    public ValueTask SchedulerShutdown(CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerShutdown(cancellationToken), nameof(SchedulerShutdown));
    }

    public ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerShuttingdown(cancellationToken), nameof(SchedulerShuttingdown));
    }

    private async ValueTask IterateListenersInGuard(Func<ISchedulerListener, ValueTask> action, string methodName)
    {
        foreach (var listener in listeners)
        {
            try
            {
                await action(listener).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(e, "Listener method {MethodName} raised an exception: {ExceptionMessage}", methodName, e.Message);
                }
            }
        }
    }
}