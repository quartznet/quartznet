/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Collections;
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
        private readonly IDictionary schedulers;
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
			schedulers = new Hashtable();
		}

        /// <summary>
        /// Binds the specified sched.
        /// </summary>
        /// <param name="sched">The sched.</param>
		public virtual void Bind(IScheduler sched)
		{
			lock (syncRoot)
			{
				if (schedulers[sched.SchedulerName] != null)
				{
					throw new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Scheduler with name '{0}' already exists.", sched.SchedulerName),
					                             SchedulerException.ErrorBadConfiguration);
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
				Object tempObject;
				tempObject = schedulers[schedName];
				schedulers.Remove(schedName);
				return (tempObject != null);
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
				return (IScheduler) schedulers[schedName];
			}
		}

        /// <summary>
        /// Lookups all.
        /// </summary>
        /// <returns></returns>
		public virtual ICollection LookupAll()
		{
			lock (syncRoot)
			{
				return ArrayList.ReadOnly(new ArrayList(schedulers.Values));
			}
		}
	}
}
