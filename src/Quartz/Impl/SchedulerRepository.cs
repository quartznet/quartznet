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
/// <para>
/// A scheduler that has shut down is dropped as soon as a read notices it. A scheduler unbinds itself
/// from the repository its own container owns, and from no other, so one bound here by hand — the way a
/// standalone scheduler is made visible to a dashboard or the HTTP API — would otherwise stay listed as a
/// live scheduler for the rest of the process. A scheduler in another process is exempt: noticing would
/// mean a network request under the lock, and it is not this process's to notice.
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
    /// always available for a local scheduler. A scheduler in another process is bound under the id its
    /// registration gave it — <c>AddQuartzHttpClient</c> passes the scheduler's name — and is never asked
    /// for one here: the property is a request, and a bind that had to wait for the target to answer it
    /// would make an unreachable target an unstartable application. One registration is one remote
    /// scheduler, so the name tells the entries apart on its own.
    /// </remarks>
    public void Bind(IScheduler scheduler, string? instanceId = null)
    {
        if (instanceId is null)
        {
            if (scheduler is IProxyScheduler)
            {
                instanceId = scheduler.SchedulerName;
            }
            else
            {
                try
                {
                    instanceId = scheduler.SchedulerInstanceId;
                }
                catch
                {
                    // A scheduler of somebody else's that cannot answer right now is still bound, under
                    // the name, preserving single-per-name semantics. Callers needing instance-aware
                    // operations should pass an instance ID.
                    instanceId = scheduler.SchedulerName;
                }
            }
        }

        lock (syncRoot)
        {
            if (schedulers.TryGetValue(scheduler.SchedulerName, out List<SchedulerEntry>? list))
            {
                // A dead entry must not block its replacement. Eviction otherwise happens on reads, and
                // re-binding after a shutdown is exactly the sequence with no read in between.
                EvictShutdown(scheduler.SchedulerName, list);
            }

            if (schedulers.TryGetValue(scheduler.SchedulerName, out list))
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
    /// <remarks>
    /// Schedulers registered under this name that have shut down are evicted rather than returned.
    /// </remarks>
    public IScheduler? Lookup(string schedulerName, string? instanceId = null)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                return null;
            }

            EvictShutdown(schedulerName, list);

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
    /// <remarks>
    /// Schedulers registered under this name that have shut down are evicted rather than returned.
    /// </remarks>
    public List<IScheduler> LookupByName(string schedulerName)
    {
        lock (syncRoot)
        {
            if (!schedulers.TryGetValue(schedulerName, out List<SchedulerEntry>? list))
            {
                return [];
            }

            EvictShutdown(schedulerName, list);
            return list.ConvertAll(e => e.Scheduler);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Schedulers that have shut down are evicted rather than returned. This is the read that sweeps the
    /// whole repository, so it is what a dashboard or the HTTP API listing schedulers cleans up with.
    /// </remarks>
    public List<IScheduler> LookupAll()
    {
        lock (syncRoot)
        {
            List<IScheduler> result = new List<IScheduler>();
            List<string>? emptied = null;

            // The lists are mutated in place, which leaves the dictionary itself untouched and so safe to
            // enumerate; the names whose lists ran dry are removed afterwards.
            foreach ((string name, List<SchedulerEntry> list) in schedulers)
            {
                list.RemoveAll(static entry => HasShutDown(entry.Scheduler));
                if (list.Count == 0)
                {
                    (emptied ??= []).Add(name);
                    continue;
                }

                foreach (SchedulerEntry entry in list)
                {
                    result.Add(entry.Scheduler);
                }
            }

            if (emptied is not null)
            {
                foreach (string name in emptied)
                {
                    schedulers.Remove(name);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Drops the entries under one name whose schedulers have shut down. Called with the lock held.
    /// </summary>
    private void EvictShutdown(string schedulerName, List<SchedulerEntry> list)
    {
        if (list.RemoveAll(static entry => HasShutDown(entry.Scheduler)) > 0 && list.Count == 0)
        {
            schedulers.Remove(schedulerName);
        }
    }

    /// <summary>
    /// Asks a scheduler whether it has shut down, treating an unanswerable question as "no".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A local scheduler reads a field, which is what makes asking it under the lock free. A scheduler
    /// in another process is not asked at all: its <see cref="IScheduler.Status" /> is a round trip, and
    /// every read of this repository is made with <c>syncRoot</c> held — so one unreachable target would
    /// stall every lookup in the process for as long as its client's timeout, the HTTP API's own
    /// scheduler resolution included.
    /// </para>
    /// <para>
    /// Nothing is lost by not asking. Unreachable is not shut down, so a proxy that failed to answer
    /// would have been kept anyway; a proxy that did answer <see cref="SchedulerStatus.Shutdown" />
    /// names a scheduler this process never started and cannot restart, and the registration that
    /// created it is what removes it. Eviction exists for the schedulers this container owns.
    /// </para>
    /// </remarks>
    private static bool HasShutDown(IScheduler scheduler)
    {
        if (scheduler is IProxyScheduler)
        {
            return false;
        }

        try
        {
            return scheduler.Status == SchedulerStatus.Shutdown;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct SchedulerEntry(string InstanceId, IScheduler Scheduler);
}
