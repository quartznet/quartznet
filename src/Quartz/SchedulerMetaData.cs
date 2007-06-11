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
using System.Text;

using Nullables;

namespace Quartz
{
	/// <summary> <p>
	/// Describes the settings and capabilities of a given <see cref="IScheduler" />
	/// instance.
	/// </p>
	/// 
	/// </summary>
	/// <author>  James House
	/// </author>
	[Serializable]
	public class SchedulerMetaData
	{
		/// <summary> <p>
		/// Returns the name of the <see cref="IScheduler" />.
		/// </p>
		/// </summary>
		public virtual string SchedulerName
		{
			get { return schedName; }
		}

		/// <summary> <p>
		/// Returns the instance Id of the <see cref="IScheduler" />.
		/// </p>
		/// </summary>
		public virtual string SchedulerInstanceId
		{
			get { return schedInst; }
		}

		/// <summary> <p>
		/// Returns the class-name of the <see cref="IScheduler" /> instance.
		/// </p>
		/// </summary>
		public virtual Type SchedulerClass
		{
			get { return schedClass; }
		}

		/// <summary> <p>
		/// Returns whether the <see cref="IScheduler" /> is being used remotely (via
		/// RMI).
		/// </p>
		/// </summary>
		public virtual bool SchedulerRemote
		{
			get { return isRemote; }
		}

		/// <summary> <p>
		/// Returns whether the scheduler has been started.
		/// </p>
		/// 
		/// <p>
		/// Note: <see cref="isStarted()" /> may return <see langword="true" /> even if
		/// <see cref="isPaused()" /> returns <see langword="true" />.
		/// </p>
		/// </summary>
		public virtual bool Started
		{
			get { return started; }
		}

		/// <summary> <p>
		/// Reports whether the <see cref="IScheduler" /> is paused.
		/// </p>
		/// 
		/// <p>
		/// Note: <see cref="isStarted()" /> may return <see langword="true" /> even if
		/// <see cref="isPaused()" /> returns <see langword="true" />.
		/// </p>
		/// </summary>
		public virtual bool Paused
		{
			get { return paused; }
		}

		/// <summary> <p>
		/// Reports whether the <see cref="IScheduler" /> has been Shutdown.
		/// </p>
		/// </summary>
		public virtual bool Shutdown
		{
			get { return shutdown; }
		}

		/// <summary> <p>
		/// Returns the class-name of the <see cref="IJobStore" /> instance that is
		/// being used by the <see cref="IScheduler" />.
		/// </p>
		/// </summary>
		public virtual Type JobStoreClass
		{
			get { return jsClass; }
		}

		/// <summary> <p>
		/// Returns the class-name of the <see cref="ThreadPool" /> instance that is
		/// being used by the <see cref="IScheduler" />.
		/// </p>
		/// </summary>
		public virtual Type ThreadPoolClass
		{
			get { return tpClass; }
		}

		/// <summary> <p>
		/// Returns the number of threads currently in the <see cref="IScheduler" />'s
		/// <see cref="ThreadPool" />.
		/// </p>
		/// </summary>
		public virtual int ThreadPoolSize
		{
			get { return tpSize; }
		}

		/// <summary> <p>
		/// Returns the version of Quartz that is running.
		/// </p>
		/// </summary>
		public virtual string Version
		{
			get { return version; }
		}

		/// <summary> <p>
		/// Returns a formatted (human readable) string describing all the <see cref="IScheduler" />'s
		/// meta-data values.
		/// </p>
		/// 
		/// <p>
		/// The format of the string looks something like this:
		/// 
		/// <pre>
		/// 
		/// 
		/// Quartz Scheduler 'SchedulerName' with instanceId 'SchedulerInstanceId' Scheduler class: 'org.quartz.impl.StdScheduler' - running locally. Running since: '11:33am on Jul 19, 2002' Not currently paused. Number of Triggers fired: '123' Using thread pool 'org.quartz.simpl.SimpleThreadPool' - with '8' threads Using job-store 'org.quartz.impl.JDBCJobStore' - which supports persistence.
		/// </pre>
		/// 
		/// </p>
		/// </summary>
		public virtual string Summary
		{
			get
			{
				StringBuilder str = new StringBuilder("Quartz Scheduler (v");
				str.Append(Version);
				str.Append(") '");

				str.Append(SchedulerName);
				str.Append("' with instanceId '");
				str.Append(SchedulerInstanceId);
				str.Append("'\n");

				str.Append("  Scheduler class: '");
				str.Append(SchedulerClass.FullName);
				str.Append("'");
				if (SchedulerRemote)
				{
					str.Append(" - access via RMI.");
				}
				else
				{
					str.Append(" - running locally.");
				}
				str.Append("\n");

				if (!Shutdown)
				{
					if (RunningSince.HasValue)
					{
						str.Append("  Running since: ");
						str.Append(RunningSince);
					}
					else
					{
						str.Append("NOT STARTED.");
					}
					str.Append("\n");

					if (Paused)
					{
						str.Append("  Currently PAUSED.");
					}
					else
					{
						str.Append("  Not currently paused.");
					}
				}
				else
				{
					str.Append("  Scheduler has been SHUTDOWN.");
				}
				str.Append("\n");

				str.Append("  Number of jobs executed: ");
				str.Append(NumJobsExecuted);
				str.Append("\n");

				str.Append("  Using thread pool '");
				str.Append(ThreadPoolClass.FullName);
				str.Append("' - with ");
				str.Append(ThreadPoolSize);
				str.Append(" threads.");
				str.Append("\n");

				str.Append("  Using job-store '");
				str.Append(JobStoreClass.FullName);
				str.Append("' - which ");
				if (JobStoreSupportsPersistence)
				{
					str.Append("supports persistence.");
				}
				else
				{
					str.Append("does not support persistence.");
				}
				str.Append("\n");

				return str.ToString();
			}
		}

		private string schedName;
		private string schedInst;
		private Type schedClass;
		private bool isRemote;
		private bool started;
		private bool paused;
		private bool shutdown;
		private NullableDateTime startTime;
		private int numJobsExec;
		private Type jsClass;
		private bool jsPersistent;
		private Type tpClass;
		private int tpSize;
		private string version;


		public SchedulerMetaData(string schedName, string schedInst, Type schedClass, bool isRemote, bool started, bool paused,
		                         bool shutdown, NullableDateTime startTime, int numJobsExec, Type jsClass, bool jsPersistent,
		                         Type tpClass, int tpSize, string version)
		{
			this.schedName = schedName;
			this.schedInst = schedInst;
			this.schedClass = schedClass;
			this.isRemote = isRemote;
			this.started = started;
			this.paused = paused;
			this.shutdown = shutdown;
			this.startTime = startTime;
			this.numJobsExec = numJobsExec;
			this.jsClass = jsClass;
			this.jsPersistent = jsPersistent;
			this.tpClass = tpClass;
			this.tpSize = tpSize;
			this.version = version;
		}

		/// <summary> <p>
		/// Returns the <see cref="DateTime" /> at which the Scheduler started running.
		/// </p>
		/// 
		/// </summary>
		/// <returns> null if the scheduler has not been started.
		/// </returns>
		public virtual NullableDateTime RunningSince
		{
			get { return startTime; }
		}

		/// <summary> <p>
		/// Returns the number of jobs executed since the <see cref="IScheduler" />
		/// started..
		/// </p>
		/// </summary>
		public virtual int NumJobsExecuted
		{
			get { return numJobsExec; }
		}

		/// <summary> <p>
		/// Returns whether or not the <see cref="IScheduler" />'s<see cref="IJobStore" />
		/// instance supports persistence.
		/// </p>
		/// </summary>
		public virtual bool JobStoreSupportsPersistence
		{
			get { return jsPersistent; }
		}

		/// <summary> <p>
		/// Return a simple string representation of this object.
		/// </p>
		/// </summary>
		public override string ToString()
		{
			try
			{
				return Summary;
			}
			catch (SchedulerException)
			{
				return "SchedulerMetaData: undeterminable.";
			}
		}
	}
}