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

using System;
using System.Collections.Generic;
using System.Globalization;

using Quartz.Spi;

namespace Quartz.Core
{
	/// <summary>
	/// Contains all of the resources (<see cref="IJobStore" />,<see cref="IThreadPool" />,
	/// etc.) necessary to create a <see cref="QuartzScheduler" /> instance.
	/// </summary>
	/// <seealso cref="QuartzScheduler" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class QuartzSchedulerResources
	{
        private string name;
        private string instanceId;
        private string threadName;
        private IThreadPool threadPool;
        private IJobStore jobStore;
        private IJobRunShellFactory jobRunShellFactory;
        private readonly IList<ISchedulerPlugin> schedulerPlugins = new List<ISchedulerPlugin>(10);

	    public QuartzSchedulerResources()
	    {
	        MaxBatchSize = 1;
	        BatchTimeWindow = TimeSpan.Zero;
	    }

	    /// <summary>
		/// Get or set the name for the <see cref="QuartzScheduler" />.
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
					ThreadName = string.Format(CultureInfo.InvariantCulture, "{0}_QuartzSchedulerThread", value);
				}
			}
		}

		/// <summary>
		/// Get or set the instance Id for the <see cref="QuartzScheduler" />.
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
		/// Get or set the name for the <see cref="QuartzSchedulerThread" />.
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
		/// Get or set the <see cref="ThreadPool" /> for the <see cref="QuartzScheduler" />
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
		/// Get or set the <see cref="IJobStore" /> for the <see cref="QuartzScheduler" />
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
		/// Get or set the <see cref="JobRunShellFactory" /> for the <see cref="QuartzScheduler" />
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

		/// <summary>
		/// Gets the unique identifier.
		/// </summary>
		/// <param name="schedName">Name of the scheduler.</param>
		/// <param name="schedInstId">The scheduler instance id.</param>
		/// <returns></returns>
		public static string GetUniqueIdentifier(string schedName, string schedInstId)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0}_$_{1}", schedName, schedInstId);
		}

		/// <summary>
		/// Gets the unique identifier.
		/// </summary>
		/// <returns></returns>
		public virtual string GetUniqueIdentifier()
		{
			return GetUniqueIdentifier(name, instanceId);
		}

        /// <summary>
        /// Add the given <see cref="ISchedulerPlugin" /> for the 
        /// <see cref="QuartzScheduler" /> to use. This method expects the plugin's
         /// "initialize" method to be invoked externally (either before or after
        /// this method is called).
        /// </summary>
        /// <param name="plugin"></param>
        public void AddSchedulerPlugin(ISchedulerPlugin plugin)
        {
            schedulerPlugins.Add(plugin);
        }

	    /// <summary>
        /// Get the <see cref="IList&lt;ISchedulerPlugin&gt;" /> of all  <see cref="ISchedulerPlugin" />s for the 
        /// <see cref="QuartzScheduler" /> to use.
	    /// </summary>
	    /// <returns></returns>
	    public IList<ISchedulerPlugin> SchedulerPlugins
	    {
	        get { return schedulerPlugins; }
	    }

	    /// <summary>
	    /// Gets or sets a value indicating whether to make scheduler thread daemon.
	    /// </summary>
	    /// <value>
	    /// 	<c>true</c> if scheduler should be thread daemon; otherwise, <c>false</c>.
	    /// </value>
	    public bool MakeSchedulerThreadDaemon { get; set; }

	    /// <summary>
	    /// Gets or sets the scheduler exporter.
	    /// </summary>
	    /// <value>The scheduler exporter.</value>
	    public ISchedulerExporter SchedulerExporter { get; set; }

	    /// <summary>
	    /// The ThreadExecutor which runs the QuartzSchedulerThread.
	    /// </summary>
	    public IThreadExecutor ThreadExecutor { get; set; }

	    /// <summary>
	    /// Gets or sets the batch time window.
	    /// </summary>
	    public TimeSpan BatchTimeWindow { get; set; }

	    public int MaxBatchSize { get; set; }

	    public bool InterruptJobsOnShutdown { get; set; }

	    public bool InterruptJobsOnShutdownWithWait { get; set; }
	}
}
