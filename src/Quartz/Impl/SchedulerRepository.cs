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

using System;
using System.Collections.Generic;
using System.Threading;

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
    private readonly Dictionary<string, List<IScheduler>> schedulers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock syncRoot = new();

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    /// <value>The instance.</value>
    public static SchedulerRepository Instance { get; } = new();

    /// <summary>
    /// Binds the specified scheduler to this repository.
    /// </summary>
    /// <remarks>
    /// Multiple schedulers with the same name are allowed if they have different instance IDs
    /// (e.g., remote proxies to different nodes in a cluster). Throws if a scheduler with the
    /// same name and instance ID already exists.
    /// </remarks>
    /// <param name="scheduler">The scheduler to bind.</param>
    public void Bind(IScheduler scheduler)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(scheduler.SchedulerName, out List<IScheduler>? list))
            {
                string? newInstanceId = TryGetLocalInstanceId(scheduler);
                foreach (IScheduler existing in list)
                {
                    string? existingInstanceId = TryGetLocalInstanceId(existing);
                    if (newInstanceId is null || existingInstanceId is null ||
                        string.Equals(newInstanceId, existingInstanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SchedulerException($"Scheduler with name '{scheduler.SchedulerName}' already exists.");
                    }
                }

                list.Add(scheduler);
            }
            else
            {
                schedulers[scheduler.SchedulerName] = [scheduler];
            }
        }
    }

    /// <summary>
    /// Removes the first scheduler with the given name.
    /// </summary>
    public void Remove(string schedulerName)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(schedulerName, out List<IScheduler>? list))
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

    /// <summary>
    /// Removes a specific scheduler by name and instance ID.
    /// </summary>
    /// <returns><see langword="true"/> if the scheduler was found and removed.</returns>
    public bool Remove(string schedulerName, string instanceId)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<IScheduler>? list))
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                string? existingId = TryGetLocalInstanceId(list[i]);
                if (string.Equals(existingId, instanceId, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Looks up the first scheduler with the given name.
    /// </summary>
    public IScheduler? Lookup(string schedulerName)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(schedulerName, out List<IScheduler>? list) && list.Count > 0)
            {
                return list[0];
            }

            return null;
        }
    }

    /// <summary>
    /// Looks up a scheduler by name and instance ID.
    /// </summary>
    public IScheduler? Lookup(string schedulerName, string instanceId)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<IScheduler>? list))
            {
                return null;
            }

            foreach (IScheduler scheduler in list)
            {
                string? existingId = TryGetLocalInstanceId(scheduler);
                if (string.Equals(existingId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return scheduler;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Returns all schedulers with the given name.
    /// </summary>
    public List<IScheduler> LookupByName(string schedulerName)
    {
        lock (syncRoot)
        {
            if (schedulers.TryGetValue(schedulerName, out List<IScheduler>? list))
            {
                return [..list];
            }

            return [];
        }
    }

    /// <summary>
    /// Returns all registered schedulers.
    /// </summary>
    public List<IScheduler> LookupAll()
    {
        lock (syncRoot)
        {
            List<IScheduler> result = new List<IScheduler>();
            foreach (List<IScheduler> list in schedulers.Values)
            {
                result.AddRange(list);
            }

            return result;
        }
    }

    /// <summary>
    /// Gets the locally-available instance ID for a scheduler without making remote calls.
    /// </summary>
    private static string? TryGetLocalInstanceId(IScheduler scheduler)
    {
        // RemoteScheduler stores instance ID locally to avoid remote calls
        if (scheduler is RemoteScheduler remote)
        {
            return remote.RepositoryInstanceId;
        }

        // Local schedulers (StdScheduler) always have SchedulerInstanceId available
        return scheduler.SchedulerInstanceId;
    }
}
