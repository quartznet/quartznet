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
using System.Threading.Tasks;

namespace Quartz.Impl
{
	/// <summary>
	/// Holds references to Scheduler instances - ensuring uniqueness, and
	/// preventing garbage collection, and allowing 'global' lookups.
	/// </summary>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class SchedulerRepository
	{
        private readonly Dictionary<string, IScheduler> schedulers;
		private readonly object syncRoot = new object();
        
        /// <summary>
		/// Gets the singleton instance.
		/// </summary>
		/// <value>The instance.</value>
		public static SchedulerRepository Instance { get; } = new SchedulerRepository();

		private SchedulerRepository()
		{
			schedulers = new Dictionary<string, IScheduler>(StringComparer.OrdinalIgnoreCase);
		}

        /// <summary>
        /// Binds the specified sched.
        /// </summary>
        /// <param name="sched">The sched.</param>
		public virtual void Bind(IScheduler sched)
		{
			lock (syncRoot)
			{
				if (schedulers.ContainsKey(sched.SchedulerName))
				{
					throw new SchedulerException($"Scheduler with name '{sched.SchedulerName}' already exists.");
				}

				schedulers[sched.SchedulerName] = sched;
			}
		}

        /// <summary>
        /// Removes the specified sched name.
        /// </summary>
        /// <param name="schedName">Name of the sched.</param>
        /// <returns></returns>
		public virtual bool Remove(string schedName)
		{
			lock (syncRoot)
			{
				return schedulers.Remove(schedName);
			}
		}

	    /// <summary>
	    /// Lookups the specified sched name.
	    /// </summary>
	    public virtual Task<IScheduler> Lookup(
		    string schedName, 
		    CancellationToken cancellationToken = default)
		{
			lock (syncRoot)
			{
				schedulers.TryGetValue(schedName, out var retValue);
				return Task.FromResult(retValue);
			}
		}

	    /// <summary>
	    /// Lookups all.
	    /// </summary>
	    /// <returns></returns>
	    public virtual Task<IReadOnlyList<IScheduler>> LookupAll(
		    CancellationToken cancellationToken = default)
		{
			lock (syncRoot)
			{
			    IReadOnlyList<IScheduler> result = new List<IScheduler>(schedulers.Values);
			    return Task.FromResult(result);
			}
		}
	}
}
