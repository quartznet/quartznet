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
using Quartz.Util;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Listeners;

/// <summary>
/// Holds a List of references to SchedulerListener instances and broadcasts all
///  events to them (in order).
///</summary>
/// <remarks>
/// This may be more convenient than registering all of the listeners
/// directly with the Scheduler, and provides the flexibility of easily changing
/// which listeners get notified.
/// </remarks>
/// <seealso cref="AddListener(ISchedulerListener)" />
/// <seealso cref="RemoveListener(ISchedulerListener)" />
/// <seealso cref="RemoveListener(string)" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class BroadcastSchedulerListener : ISchedulerListener
{
    private readonly List<ISchedulerListener> listeners;
    private readonly ILogger<BroadcastSchedulerListener> logger;

    /// <summary>
    /// Construct an instance with the given name.
    /// </summary>
    /// <remarks>
    /// (Remember to add some delegate listeners!)
    /// </remarks>
    /// <param name="name">the name of this instance</param>
    public BroadcastSchedulerListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name), "Listener name cannot be null!");
        }
        Name = name;
        listeners = new List<ISchedulerListener>();
        logger = LogProvider.CreateLogger<BroadcastSchedulerListener>();
    }

    /// <summary>
    /// Construct an instance with the given name, and List of listeners.
    /// </summary>
    /// <param name="name">the name of this instance</param>
    /// <param name="listeners">The initial List of SchedulerListeners to broadcast to.</param>
    public BroadcastSchedulerListener(string name, IReadOnlyCollection<ISchedulerListener> listeners) : this(name)
    {
        this.listeners.AddRange(listeners);
    }

    public string Name { get; }

    public void AddListener(ISchedulerListener listener)
    {
        listeners.Add(listener);
    }

    public bool RemoveListener(ISchedulerListener listener)
    {
        return listeners.Remove(listener);
    }

    public bool RemoveListener(string listenerName)
    {
        ISchedulerListener? listener = listeners.Find(x => x.Name == listenerName);
        if (listener is not null)
        {
            listeners.Remove(listener);
            return true;
        }
        return false;
    }

    public IReadOnlyList<ISchedulerListener> Listeners => listeners;

    public ValueTask JobAdded(IScheduler scheduler, IJobDetail jobDetail, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobAdded(scheduler, jobDetail, cancellationToken), nameof(JobAdded));
    }

    public ValueTask JobDeleted(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobDeleted(scheduler, jobKey, cancellationToken), nameof(JobDeleted));
    }

    public ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobScheduled(scheduler, trigger, cancellationToken), nameof(JobScheduled));
    }

    public ValueTask JobUnscheduled(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobUnscheduled(scheduler, triggerKey, cancellationToken), nameof(JobUnscheduled));
    }

    public ValueTask TriggerFinalized(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerFinalized(scheduler, trigger, cancellationToken), nameof(TriggerFinalized));
    }

    public ValueTask TriggersPaused(IScheduler scheduler, string? triggerGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggersPaused(scheduler, triggerGroup, cancellationToken), nameof(TriggersPaused));
    }

    public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerPaused(scheduler, triggerKey, cancellationToken), nameof(TriggerPaused));
    }

    public ValueTask TriggerInError(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerInError(scheduler, triggerKey, cancellationToken), nameof(TriggerInError));
    }

    public ValueTask TriggersInError(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggersInError(scheduler, jobKey, cancellationToken), nameof(TriggersInError));
    }

    public ValueTask TriggersResumed(IScheduler scheduler, string? triggerGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggersResumed(scheduler, triggerGroup, cancellationToken), nameof(TriggerResumed));
    }

    public ValueTask SchedulingDataCleared(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulingDataCleared(scheduler, cancellationToken), nameof(SchedulingDataCleared));
    }

    public ValueTask TriggerResumed(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerResumed(scheduler, triggerKey, cancellationToken), nameof(TriggerResumed));
    }

    public ValueTask JobInterrupted(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = new CancellationToken())
    {
        return IterateListenersInGuard(l => l.JobInterrupted(scheduler, jobKey, cancellationToken), nameof(JobInterrupted));
    }

    public ValueTask JobsPaused(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobsPaused(scheduler, jobGroup, cancellationToken), nameof(JobsPaused));
    }

    public ValueTask JobPaused(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobPaused(scheduler, jobKey, cancellationToken), nameof(JobPaused));
    }

    public ValueTask JobsResumed(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobsResumed(scheduler, jobGroup, cancellationToken), nameof(JobsResumed));
    }

    public ValueTask JobResumed(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.JobResumed(scheduler, jobKey, cancellationToken), nameof(JobResumed));
    }

    public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerError(scheduler, errorContext, cancellationToken), nameof(SchedulerError));
    }

    public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerStarted(scheduler, cancellationToken), nameof(SchedulerStarted));
    }

    public ValueTask SchedulerStarting(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerStarting(scheduler, cancellationToken), nameof(SchedulerStarting));
    }

    public ValueTask SchedulerInStandbyMode(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerInStandbyMode(scheduler, cancellationToken), nameof(SchedulerInStandbyMode));
    }

    public ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerShutdown(scheduler, cancellationToken), nameof(SchedulerShutdown));
    }

    public ValueTask SchedulerShuttingDown(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.SchedulerShuttingDown(scheduler, cancellationToken), nameof(SchedulerShuttingDown));
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
                    logger.SchedulerListenerRaisedException(methodName, e.Message, e);
                }
            }
        }
    }
}