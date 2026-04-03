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

using Quartz.Spi;

namespace Quartz.Impl;

/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and preventing garbage collection, and allowing 'global' lookups.
/// </summary>
/// <remarks>
/// Schedulers are indexed by name. Multiple schedulers with the same name but different instance IDs
/// can coexist (e.g., remote proxies to different cluster nodes). Use <see cref="Lookup(string, string)"/>
/// to disambiguate by instance ID.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public sealed class SchedulerRepository : ISchedulerRepository
{
    private readonly Dictionary<string, List<SchedulerEntry>> schedulers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock syncRoot = new();

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    /// <value>The instance.</value>
    public static SchedulerRepository Instance { get; } = new();

    /// <inheritdoc />
    /// <remarks>
    /// This overload reads <see cref="IScheduler.SchedulerInstanceId"/> which is always available
    /// for local schedulers. For remote schedulers (e.g., <c>HttpScheduler</c>) where
    /// <see cref="IScheduler.SchedulerInstanceId"/> may require a network call,
    /// use <see cref="Bind(IScheduler, string)"/> with an explicit instance ID instead.
    /// If <see cref="IScheduler.SchedulerInstanceId"/> cannot be resolved, the scheduler name
    /// is used as a fallback, preserving single-scheduler-per-name semantics.
    /// </remarks>
    public void Bind(IScheduler scheduler)
    {
        string instanceId;
        try
        {
            instanceId = scheduler.SchedulerInstanceId;
        }
        catch
        {
            // Remote schedulers may not be reachable during bind.
            // Fall back to scheduler name, preserving single-per-name semantics.
            // Callers needing instance-aware operations should use Bind(scheduler, instanceId).
            instanceId = scheduler.SchedulerName;
        }

        Bind(scheduler, instanceId);
    }

    /// <inheritdoc />
    public void Bind(IScheduler scheduler, string instanceId)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(scheduler.SchedulerName, out List<SchedulerEntry>? list))
            {
                foreach (SchedulerEntry entry in list)
                {
                    if (string.Equals(entry.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        Throw.SchedulerException($"Scheduler with name '{scheduler.SchedulerName}' already exists.");
                    }
                }

                list.Add(new SchedulerEntry(instanceId, scheduler));
            }
            else
            {
                schedulers[scheduler.SchedulerName] = [new SchedulerEntry(instanceId, scheduler)];
            }
        }
    }

    /// <inheritdoc />
    public void Remove(string schedulerName)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                if (list.Count <= 1)
                {
                    schedulers.Remove(schedulerName);
                }
                else
                {
                    list.RemoveAt(0);
                }
            }
        }
    }

    /// <inheritdoc />
    public bool Remove(string schedulerName, string instanceId)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    if (list.Count == 0)
                    {
                        schedulers.Remove(schedulerName);
                    }

                    return true;
                }
            }

            return false;
        }
    }

    /// <inheritdoc />
    public IScheduler? Lookup(string schedulerName)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list) && list.Count > 0)
            {
                return list[0].Scheduler;
            }

            return null;
        }
    }

    /// <inheritdoc />
    public IScheduler? Lookup(string schedulerName, string instanceId)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                return null;
            }

            foreach (SchedulerEntry entry in list)
            {
                if (string.Equals(entry.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Scheduler;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public List<IScheduler> LookupByName(string schedulerName)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                return list.ConvertAll(e => e.Scheduler);
            }

            return [];
        }
    }

    /// <inheritdoc />
    public List<IScheduler> LookupAll()
    {
        lock (syncRoot)
        {
            List<IScheduler> result = new List<IScheduler>();
            foreach (List<SchedulerEntry> list in schedulers.Values)
            {
                foreach (SchedulerEntry entry in list)
                {
                    result.Add(entry.Scheduler);
                }
            }

            return result;
        }
    }

    private readonly record struct SchedulerEntry(string InstanceId, IScheduler Scheduler);
}
