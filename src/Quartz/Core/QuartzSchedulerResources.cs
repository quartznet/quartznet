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

using Quartz.Spi;

namespace Quartz.Core
{
	/// <summary>
	/// Contains all of the resources (<code>IJobStore</code>,<code>IThreadPool</code>,
	/// etc.) necessary to create a <code>QuartzScheduler</code> instance.
	/// </summary>
	/// <seealso cref="QuartzScheduler" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class QuartzSchedulerResources
	{
		/// <summary>
		/// Get or set the name for the <code>QuartzScheduler</code>.
		/// </summary>
		/// <exception cref="ArgumentException">
		/// if name is null or empty.
		/// </exception>
		public virtual string Name
		{
			get { return name; }

			set
			{
				if (value == null || value.Trim().Length == 0)
				{
					throw new ArgumentException("Scheduler name cannot be empty.");
				}

				name = value;

				if (threadName == null)
				{
					// thread name not already set, use default thread name
					ThreadName = value + "_QuartzSchedulerThread";
				}
			}
		}

		/// <summary>
		/// Get or set the instance Id for the <code>QuartzScheduler</code>.
		/// </summary>
		/// <exception cref="ArgumentException"> 
		/// if name is null or empty.
		/// </exception>
		public virtual string InstanceId
		{
			get { return instanceId; }

			set
			{
				if (value == null || value.Trim().Length == 0)
				{
					throw new ArgumentException("Scheduler instanceId cannot be empty.");
				}

				instanceId = value;
			}
		}


		/// <summary>
		/// Get or set the name for the <code>QuartzSchedulerThread</code>.
		/// </summary>
		/// <exception cref="ArgumentException"> 
		/// if name is null or empty.
		/// </exception>
		public virtual string ThreadName
		{
			get { return threadName; }

			set
			{
				if (value == null || value.Trim().Length == 0)
				{
					throw new ArgumentException("Scheduler thread name cannot be empty.");
				}

				threadName = value;
			}
		}

		/// <summary>
		/// Get or set the <code>ThreadPool</code> for the <code>QuartzScheduler</code>
		/// to use.
		/// </summary>
		/// <exception cref="ArgumentException"> 
		/// if threadPool is null.
		/// </exception>
		public virtual IThreadPool ThreadPool
		{
			get { return threadPool; }

			set
			{
				if (value == null)
				{
					throw new ArgumentException("ThreadPool cannot be null.");
				}

				threadPool = value;
			}
		}

		/// <summary>
		/// Get or set the <code>JobStore</code> for the <code>QuartzScheduler</code>
		/// to use.
		/// </summary>
		/// <exception cref="ArgumentException"> 
		/// if jobStore is null.
		/// </exception>
		public virtual IJobStore JobStore
		{
			get { return jobStore; }

			set
			{
				if (value == null)
				{
					throw new ArgumentException("JobStore cannot be null.");
				}

				jobStore = value;
			}
		}

		/// <summary> 
		/// Get or set the <code>JobRunShellFactory</code> for the <code>QuartzScheduler</code>
		/// to use.
		/// </summary>
		/// <exception cref="ArgumentException"> 
		/// if jobRunShellFactory is null.
		/// </exception>
		public virtual IJobRunShellFactory JobRunShellFactory
		{
			get { return jobRunShellFactory; }

			set
			{
				if (value == null)
				{
					throw new ArgumentException("JobRunShellFactory cannot be null.");
				}

				jobRunShellFactory = value;
			}
		}

		private string name;

		private string instanceId;

		private string threadName;

		private IThreadPool threadPool;

		private IJobStore jobStore;

		private IJobRunShellFactory jobRunShellFactory;

		/// <summary>
		/// Gets the unique identifier.
		/// </summary>
		/// <param name="schedName">Name of the scheduler.</param>
		/// <param name="schedInstId">The scheduler instance id.</param>
		/// <returns></returns>
		public static string GetUniqueIdentifier(string schedName, string schedInstId)
		{
			return schedName + "_$_" + schedInstId;
		}

		/// <summary>
		/// Gets the unique identifier.
		/// </summary>
		/// <returns></returns>
		public virtual string GetUniqueIdentifier()
		{
			return GetUniqueIdentifier(name, instanceId);
		}
	}
}