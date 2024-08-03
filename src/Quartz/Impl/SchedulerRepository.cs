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

using System.Collections.Concurrent;

using Quartz.Spi;

namespace Quartz.Impl;

/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and preventing garbage collection, and allowing 'global' lookups.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public sealed class SchedulerRepository : ISchedulerRepository
{
    private readonly ConcurrentDictionary<string, IScheduler> schedulers = new(StringComparer.OrdinalIgnoreCase);

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
        if (!schedulers.TryAdd(scheduler.SchedulerName, scheduler))
        {
            ThrowHelper.ThrowSchedulerException($"Scheduler with name '{scheduler.SchedulerName}' already exists.");
        }
    }

    /// <summary>
    /// Removes the specified sched name.
    /// </summary>
    /// <param name="schedulerName">Name of the sched.</param>
    /// <returns></returns>
    public void Remove(string schedulerName)
    {
        schedulers.Remove(schedulerName, out _);
    }

    public IScheduler? Lookup(string schedulerName)
    {
        return schedulers.GetValueOrDefault(schedulerName);
    }

    /// <summary>
    /// Lookups all.
    /// </summary>
    /// <returns></returns>
    public List<IScheduler> LookupAll()
    {
        return [..schedulers.Values];
    }
}