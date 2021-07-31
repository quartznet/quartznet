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
using System.Text;

using Quartz.Spi;

namespace Quartz
{
    /// <summary>
    /// Describes the settings and capabilities of a given <see cref="IScheduler" />
    /// instance.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class SchedulerMetaData
	{
	    /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerMetaData"/> class.
        /// </summary>
        /// <param name="schedName">Name of the scheduler.</param>
        /// <param name="schedInst">The scheduler instance.</param>
        /// <param name="schedType">The scheduler type.</param>
        /// <param name="isRemote">if set to <c>true</c>, scheduler is a remote scheduler.</param>
        /// <param name="started">if set to <c>true</c>, scheduler is started.</param>
        /// <param name="isInStandbyMode">if set to <c>true</c>, scheduler is in standby mode.</param>
        /// <param name="shutdown">if set to <c>true</c>, scheduler is shutdown.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="numberOfJobsExec">The number of jobs executed.</param>
        /// <param name="jsType">The job store type.</param>
        /// <param name="jsPersistent">if set to <c>true</c>, job store is persistent.</param>
        /// <param name="jsClustered">if set to <c>true</c>, the job store is clustered</param>
        /// <param name="tpType">The thread pool type.</param>
        /// <param name="tpSize">Size of the thread pool.</param>
        /// <param name="version">The version string.</param>
        public SchedulerMetaData(
            string schedName, string schedInst, Type schedType, bool isRemote, bool started, bool isInStandbyMode,
			bool shutdown, DateTimeOffset? startTime, int numberOfJobsExec, Type jsType, bool jsPersistent, bool jsClustered,
            Type tpType, int tpSize, string version)
		{
			SchedulerName = schedName;
			SchedulerInstanceId = schedInst;
			SchedulerType = schedType;
			SchedulerRemote = isRemote;
			Started = started;
			InStandbyMode = isInStandbyMode;
			Shutdown = shutdown;
			RunningSince = startTime;
			NumberOfJobsExecuted = numberOfJobsExec;
			JobStoreType = jsType;
			JobStoreSupportsPersistence = jsPersistent;
		    JobStoreClustered = jsClustered;
		    ThreadPoolType = tpType;
			ThreadPoolSize = tpSize;
			Version = version;
		}

		/// <summary>
		/// Returns the name of the <see cref="IScheduler" />.
		/// </summary>
		public virtual string SchedulerName { get; }

	    /// <summary>
		/// Returns the instance Id of the <see cref="IScheduler" />.
		/// </summary>
		public virtual string SchedulerInstanceId { get; }

	    /// <summary>
		/// Returns the class-name of the <see cref="IScheduler" /> instance.
		/// </summary>
		public virtual Type SchedulerType { get; }

	    /// <summary>
		/// Returns whether the <see cref="IScheduler" /> is being used remotely (via remoting).
		/// </summary>
		public virtual bool SchedulerRemote { get; }

	    /// <summary>
		/// Returns whether the scheduler has been started.
		/// </summary>
		/// <remarks>
		/// Note: <see cref="Started" /> may return <see langword="true" /> even if
        /// <see cref="InStandbyMode" /> returns <see langword="true" />.
		/// </remarks>
		public virtual bool Started { get; }

	    /// <summary>
        /// Reports whether the <see cref="IScheduler" /> is in standby mode.
		/// </summary>
		/// <remarks>
		/// Note: <see cref="Started" /> may return <see langword="true" /> even if
		/// <see cref="InStandbyMode" /> returns <see langword="true" />.
		/// </remarks>
        public virtual bool InStandbyMode { get; }

	    /// <summary>
		/// Reports whether the <see cref="IScheduler" /> has been Shutdown.
		/// </summary>
		public virtual bool Shutdown { get; }

	    /// <summary>
		/// Returns the class-name of the <see cref="IJobStore" /> instance that is
		/// being used by the <see cref="IScheduler" />.
		/// </summary>
		public virtual Type JobStoreType { get; }

	    /// <summary>
		/// Returns the type name of the thread pool instance that is
		/// being used by the <see cref="IScheduler" />.
		/// </summary>
		public virtual Type ThreadPoolType { get; }

	    /// <summary>
		/// Returns the number of threads currently in the <see cref="IScheduler" />'s
		/// </summary>
		public virtual int ThreadPoolSize { get; }

	    /// <summary>
		/// Returns the version of Quartz that is running.
		/// </summary>
		public virtual string Version { get; }

	    /// <summary>
		/// Returns a formatted (human readable) string describing all the <see cref="IScheduler" />'s
		/// meta-data values.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The format of the string looks something like this:
		/// <pre>
		/// Quartz Scheduler 'SchedulerName' with instanceId 'SchedulerInstanceId' Scheduler class: 'Quartz.Impl.StdScheduler' - running locally. Running since: '11:33am on Jul 19, 2002' Not currently paused. Number of Triggers fired: '123' Using thread pool 'Quartz.Simpl.DefaultThreadPool' - with '8' threads Using job-store 'Quartz.Impl.JobStore' - which supports persistence.
		/// </pre>
		/// </para>
		/// </remarks>
		public string GetSummary()
		{
			StringBuilder str = new StringBuilder("Quartz Scheduler (v");
			str.Append(Version);
			str.Append(") '");

			str.Append(SchedulerName);
			str.Append("' with instanceId '");
			str.Append(SchedulerInstanceId);
			str.Append("'\n");

			str.Append("  Scheduler class: '");
			str.Append(SchedulerType.FullName);
			str.Append("'");
			if (SchedulerRemote)
			{
                str.Append(" - access via remote invocation.");
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
                    str.Append("  NOT STARTED.");
				}
				str.Append("\n");


				if (InStandbyMode)
				{
					str.Append("  Currently in standby mode.");
				}
				else
				{
					str.Append("  Not currently in standby mode.");
				}
			}
			else
			{
				str.Append("  Scheduler has been SHUTDOWN.");
			}
			str.Append("\n");

			str.Append("  Number of jobs executed: ");
			str.Append(NumberOfJobsExecuted);
			str.Append("\n");

			str.Append("  Using thread pool '");
			str.Append(ThreadPoolType.FullName);
			str.Append("' - with ");
			str.Append(ThreadPoolSize);
			str.Append(" threads.");
			str.Append("\n");

			str.Append("  Using job-store '");
			str.Append(JobStoreType.FullName);
			str.Append("' - which ");
			if (JobStoreSupportsPersistence)
			{
				str.Append("supports persistence.");
			}
			else
			{
				str.Append("does not support persistence.");
			}
            if (JobStoreClustered)
            {
                str.Append(" and is clustered.");
            }
            else
            {
                str.Append(" and is not clustered.");
            }
			str.Append("\n");

			return str.ToString();
		}

		/// <summary>
		/// Returns the <see cref="DateTimeOffset" /> at which the Scheduler started running.
		/// </summary>
		/// <returns> null if the scheduler has not been started.
		/// </returns>
        public virtual DateTimeOffset? RunningSince { get; }

	    /// <summary>
		/// Returns the number of jobs executed since the <see cref="IScheduler" />
		/// started..
		/// </summary>
		public virtual int NumberOfJobsExecuted { get; }

	    /// <summary>
		/// Returns whether or not the <see cref="IScheduler" />'s<see cref="IJobStore" />
		/// instance supports persistence.
		/// </summary>
		public virtual bool JobStoreSupportsPersistence { get; }

	    /// <summary>
        /// Returns whether or not the <see cref="IScheduler" />'s <see cref="IJobStore" />
        /// is clustered.
        /// </summary>
        public virtual bool JobStoreClustered { get; }

	    /// <summary>
		/// Return a simple string representation of this object.
		/// </summary>
		public override string ToString()
		{
			try
			{
				return GetSummary();
			}
			catch (SchedulerException)
			{
                return "SchedulerMetaData: indeterminable.";
			}
		}
	}
}