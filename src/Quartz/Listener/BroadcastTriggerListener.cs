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
/// Holds a List of references to TriggerListener instances and broadcasts all
/// events to them (in order).
/// </summary>
/// <remarks>
/// <para>The broadcasting behavior of this listener to delegate listeners may be
/// more convenient than registering all of the listeners directly with the
/// Scheduler, and provides the flexibility of easily changing which listeners
/// get notified.</para>
/// </remarks>
/// <seealso cref="AddListener(ITriggerListener)" />
/// <seealso cref="RemoveListener(ITriggerListener)" />
/// <seealso cref="RemoveListener(string)" />
/// <author>James House (jhouse AT revolition DOT net)</author>
public sealed class BroadcastTriggerListener : ITriggerListener
{
    private readonly List<ITriggerListener> listeners;
    private readonly ILogger<BroadcastTriggerListener> logger;

    /// <summary>
    /// Construct an instance with the given name.
    /// </summary>
    /// <remarks>
    /// (Remember to add some delegate listeners!)
    /// </remarks>
    /// <param name="name">the name of this instance</param>
    public BroadcastTriggerListener(string name)
    {
        if (name is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(name), "Listener name cannot be null!");
        }
        Name = name;
        listeners = new List<ITriggerListener>();
        logger = LogProvider.CreateLogger<BroadcastTriggerListener>();
    }

    /// <summary>
    /// Construct an instance with the given name, and List of listeners.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="name">the name of this instance</param>
    /// <param name="listeners">the initial List of TriggerListeners to broadcast to.</param>
    public BroadcastTriggerListener(string name, IReadOnlyCollection<ITriggerListener> listeners) : this(name)
    {
        this.listeners.AddRange(listeners);
    }

    public string Name { get; }

    public void AddListener(ITriggerListener listener)
    {
        listeners.Add(listener);
    }

    public bool RemoveListener(ITriggerListener listener)
    {
        return listeners.Remove(listener);
    }

    public bool RemoveListener(string listenerName)
    {
        ITriggerListener? listener = listeners.Find(x => x.Name == listenerName);
        if (listener is not null)
        {
            listeners.Remove(listener);
            return true;
        }
        return false;
    }

    public IReadOnlyList<ITriggerListener> Listeners => listeners;

    public ValueTask TriggerFired(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerFired(trigger, context, cancellationToken), nameof(TriggerFired));
    }

    public async ValueTask<bool> VetoJobExecution(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var listener in listeners)
        {
            if (await listener.VetoJobExecution(trigger, context, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerMisfired(trigger, cancellationToken), nameof(TriggerMisfired));
    }

    public ValueTask TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        return IterateListenersInGuard(l => l.TriggerComplete(trigger, context, triggerInstructionCode, cancellationToken), nameof(TriggerComplete));
    }

    private async ValueTask IterateListenersInGuard(Func<ITriggerListener, ValueTask> action, string methodName)
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
                    logger.LogError(e, "Listener {ListenerName} - method {MethodName} raised an exception: {ExceptionMessage}",
                        listener.Name, methodName, e.Message);
                }
            }
        }
    }
}