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

using Quartz.Spi;

namespace Quartz.Impl;


/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and preventing garbage collection, and allowing 'global' lookups.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public sealed class SchedulerRepository : ISchedulerRepository
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
    /// <param name="schedulerName">The sched.</param>
    public void Bind(IScheduler schedulerName)
    {
        lock (syncRoot)
        {
            if (schedulers.ContainsKey(schedulerName.SchedulerName))
            {
                throw new SchedulerException($"Scheduler with name '{schedulerName.SchedulerName}' already exists.");
            }

            schedulers[schedulerName.SchedulerName] = schedulerName;
        }
    }

    public void Remove(string schedulerName)
    {
        lock (syncRoot)
        {
            schedulers.Remove(schedulerName);
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

    public List<IScheduler> LookupAll()
    {
        lock (syncRoot)
        {
            return [..schedulers.Values];
        }
    }
}