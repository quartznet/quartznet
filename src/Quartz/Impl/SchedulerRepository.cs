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

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and preventing garbage collection, and allowing lookups by name.
/// </summary>
/// <remarks>
/// <para>
/// A repository is owned by the container it is registered in; there is no process-wide instance.
/// Resolve <see cref="ISchedulerRepository"/> to reach the one belonging to a scheduler, which is what
/// makes two sets of schedulers in one process independent of each other.
/// </para>
/// <para>
/// Schedulers are indexed by name. Multiple schedulers with the same name but different instance IDs
/// can coexist (e.g., remote proxies to different cluster nodes). Pass an instance ID to
/// <see cref="Lookup"/> to disambiguate between them.
/// </para>
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public sealed class SchedulerRepository : ISchedulerRepository
{
    private readonly Dictionary<string, List<SchedulerEntry>> schedulers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock syncRoot = new();

    /// <inheritdoc />
    /// <remarks>
    /// Without an explicit instance ID this reads <see cref="IScheduler.SchedulerInstanceId"/>, which is
    /// always available for a local scheduler. For a remote one (e.g., <c>HttpScheduler</c>) reading it may
    /// cost a network call, so pass the instance ID instead. If it cannot be resolved at all, the scheduler
    /// name is used as a fallback, preserving single-scheduler-per-name semantics.
    /// </remarks>
    public void Bind(IScheduler scheduler, string? instanceId = null)
    {
        if (instanceId is null)
        {
            try
            {
                instanceId = scheduler.SchedulerInstanceId;
            }
            catch
            {
                // Remote schedulers may not be reachable during bind.
                // Fall back to scheduler name, preserving single-per-name semantics.
                // Callers needing instance-aware operations should pass an instance ID.
                instanceId = scheduler.SchedulerName;
            }
        }

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
    public bool Remove(string schedulerName, string? instanceId = null)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (instanceId is null || string.Equals(list[i].InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
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
    public IScheduler? Lookup(string schedulerName, string? instanceId = null)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                return null;
            }

            foreach (SchedulerEntry entry in list)
            {
                if (instanceId is null || string.Equals(entry.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
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
