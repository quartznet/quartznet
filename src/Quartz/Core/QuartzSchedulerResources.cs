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

		public virtual string RMIRegistryHost
		{
			get { return rmiRegistryHost; }
			set { rmiRegistryHost = value; }
		}

		public virtual int RMIRegistryPort
		{
			get { return rmiRegistryPort; }

			set { rmiRegistryPort = value; }
		}

		public virtual int RMIServerPort
		{
			get { return rmiServerPort; }

			set { rmiServerPort = value; }
		}

		public virtual string RMICreateRegistryStrategy
		{
			get { return rmiCreateRegistryStrategy; }

			set
			{
				if (value == null || value.Trim().Length == 0)
				{
					value = CREATE_REGISTRY_NEVER;
				}
				else if (value.ToUpper().Equals("true".ToUpper()))
				{
					value = CREATE_REGISTRY_AS_NEEDED;
				}
				else if (value.ToUpper().Equals("false".ToUpper()))
				{
					value = CREATE_REGISTRY_NEVER;
				}
				else if (value.ToUpper().Equals(CREATE_REGISTRY_ALWAYS.ToUpper()))
				{
					value = CREATE_REGISTRY_ALWAYS;
				}
				else if (value.ToUpper().Equals(CREATE_REGISTRY_AS_NEEDED.ToUpper()))
				{
					value = CREATE_REGISTRY_AS_NEEDED;
				}
				else if (value.ToUpper().Equals(CREATE_REGISTRY_NEVER.ToUpper()))
				{
					value = CREATE_REGISTRY_NEVER;
				}
				else
				{
					throw new ArgumentException("Faild to set RMICreateRegistryStrategy - strategy unknown: '" + value + "'");
				}

				rmiCreateRegistryStrategy = value;
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

		public const string CREATE_REGISTRY_NEVER = "never";

		public const string CREATE_REGISTRY_ALWAYS = "always";

		public const string CREATE_REGISTRY_AS_NEEDED = "as_needed";

		private string name;

		private string instanceId;

		private string threadName;

		private string rmiRegistryHost = null;

		private int rmiRegistryPort = 1099;

		private int rmiServerPort = - 1;

		private string rmiCreateRegistryStrategy = CREATE_REGISTRY_NEVER;

		private IThreadPool threadPool;

		private IJobStore jobStore;

		private IJobRunShellFactory jobRunShellFactory;

		public static string GetUniqueIdentifier(string schedName, string schedInstId)
		{
			return schedName + "_$_" + schedInstId;
		}

		public virtual string GetUniqueIdentifier()
		{
			return GetUniqueIdentifier(name, instanceId);
		}
	}
}