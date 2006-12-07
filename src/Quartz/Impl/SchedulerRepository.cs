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

namespace Quartz.Impl
{
	/// <summary>
	/// Holds references to Scheduler instances - ensuring uniqueness, and
	/// preventing garbage collection, and allowing 'global' lookups - all within a
	/// ClassLoader space.
	/// </summary>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class SchedulerRepository
	{
		/// <summary>
		/// Gets the singleton instance.
		/// </summary>
		/// <value>The instance.</value>
		public static SchedulerRepository Instance
		{
			get
			{
				return inst;
			}
		}

		private Hashtable schedulers;
		private static readonly SchedulerRepository inst = new SchedulerRepository();
		private object syncRoot = new object();
		
		private SchedulerRepository()
		{
			schedulers = new Hashtable();
		}

		public virtual void Bind(IScheduler sched)
		{
			lock (syncRoot)
			{
				if (schedulers[sched.SchedulerName] != null)
				{
					throw new SchedulerException("Scheduler with name '" + sched.SchedulerName + "' already exists.",
					                             SchedulerException.ERR_BAD_CONFIGURATION);
				}

				schedulers[sched.SchedulerName] = sched;
			}
		}

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

		public virtual IScheduler Lookup(string schedName)
		{
			lock (syncRoot)
			{
				return (IScheduler) schedulers[schedName];
			}
		}

		public virtual ICollection lookupAll()
		{
			lock (syncRoot)
			{
				return ArrayList.ReadOnly(new ArrayList(schedulers.Values));
			}
		}
	}
}