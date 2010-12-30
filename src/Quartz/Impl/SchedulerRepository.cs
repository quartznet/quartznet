#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System.Collections.Generic;
using System.Globalization;

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
        private static readonly SchedulerRepository inst = new SchedulerRepository();
        private readonly object syncRoot = new object();
        
        /// <summary>
		/// Gets the singleton instance.
		/// </summary>
		/// <value>The instance.</value>
		public static SchedulerRepository Instance
		{
			get { return inst; }
		}

		private SchedulerRepository()
		{
			schedulers = new Dictionary<string, IScheduler>();
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
					throw new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Scheduler with name '{0}' already exists.", sched.SchedulerName));
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
        /// <param name="schedName">Name of the sched.</param>
        /// <returns></returns>
		public virtual IScheduler Lookup(string schedName)
		{
			lock (syncRoot)
			{
			    IScheduler retValue;
			    schedulers.TryGetValue(schedName, out retValue);
				return retValue;
			}
		}

        /// <summary>
        /// Lookups all.
        /// </summary>
        /// <returns></returns>
		public virtual ICollection<IScheduler> LookupAll()
		{
			lock (syncRoot)
			{
				return new List<IScheduler>(schedulers.Values).AsReadOnly();
			}
		}
	}
}
