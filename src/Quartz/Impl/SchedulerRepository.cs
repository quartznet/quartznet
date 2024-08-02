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

namespace Quartz.Impl;

internal interface ISchedulerRepository
{
    void Bind(IScheduler scheduler);

    bool Remove(string schedulerName);

    IScheduler? Lookup(string schedulerName);

    List<IScheduler> LookupAll();
}

/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and
/// preventing garbage collection, and allowing 'global' lookups.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class SchedulerRepository : ISchedulerRepository
{
    private readonly Dictionary<string, IScheduler> schedulers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object syncRoot = new();

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    /// <value>The instance.</value>
    public static SchedulerRepository Instance { get; } = new();

    /// <summary>
    /// Binds the specified sched.
    /// </summary>
    /// <param name="scheduler">The sched.</param>
    public void Bind(IScheduler scheduler)
    {
        lock (syncRoot)
        {
            if (schedulers.ContainsKey(scheduler.SchedulerName))
            {
                ThrowHelper.ThrowSchedulerException($"Scheduler with name '{scheduler.SchedulerName}' already exists.");
            }

            schedulers[scheduler.SchedulerName] = scheduler;
        }
    }

    /// <summary>
    /// Removes the specified sched name.
    /// </summary>
    /// <param name="schedulerName">Name of the sched.</param>
    /// <returns></returns>
    public bool Remove(string schedulerName)
    {
        lock (syncRoot)
        {
            return schedulers.Remove(schedulerName);
        }
    }

    public IScheduler? Lookup(string schedulerName)
    {
        lock (syncRoot)
        {
            schedulers.TryGetValue(schedulerName, out var retValue);
            return retValue;
        }
    }

    /// <summary>
    /// Lookups all.
    /// </summary>
    /// <returns></returns>
    public List<IScheduler> LookupAll()
    {
        lock (syncRoot)
        {
            return [..schedulers.Values];
        }
    }
}