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

using Quartz.Spi;

namespace Quartz
{
	/// <summary>
	/// The interface to be implemented by classes that want to be informed of major
	/// <see cref="IScheduler" /> events.
	/// </summary>
	/// <seealso cref="IScheduler" />
	/// <seealso cref="IJobListener" />
	/// <seealso cref="ITriggerListener" />
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface ISchedulerListener
	{
		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="JobDetailImpl" />
		/// is scheduled.
		/// </summary>
		void JobScheduled(Trigger trigger);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="JobDetailImpl" />
		/// is unscheduled.
		/// </summary>
		void JobUnscheduled(string triggerName, string triggerGroup);

		/// <summary> 
		/// Called by the <see cref="IScheduler" /> when a <see cref="Trigger" />
		/// has reached the condition in which it will never fire again.
		/// </summary>
		void TriggerFinalized(Trigger trigger);

		/// <summary>
		/// Called by the <see cref="IScheduler"/> when a <see cref="Trigger"/>
		/// or group of <see cref="Trigger"/>s has been paused.
		/// <p>
		/// If a group was paused, then the <see param="triggerName"/> parameter
		/// will be null.
		/// </p>
		/// </summary>
		/// <param name="triggerName">Name of the trigger.</param>
		/// <param name="triggerGroup">The trigger group.</param>
		void TriggersPaused(string triggerName, string triggerGroup);

		/// <summary>
		/// Called by the <see cref="IScheduler"/> when a <see cref="Trigger"/>
		/// or group of <see cref="Trigger"/>s has been un-paused.
		/// <p>
		/// If a group was resumed, then the <see param="triggerName"/> parameter
		/// will be null.
		/// </p>
		/// </summary>
		/// <param name="triggerName">Name of the trigger.</param>
		/// <param name="triggerGroup">The trigger group.</param>
		void TriggersResumed(string triggerName, string triggerGroup);

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="JobDetailImpl" />
        /// has been added.
        /// </summary>
        /// <param name="jobDetail"></param>
        void JobAdded(JobDetailImpl jobDetail);

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="JobDetailImpl" />
        /// has been deleted.
        /// </summary>
        /// <param name="jobName"></param>
        /// <param name="groupName"></param>
        void JobDeleted(string jobName, string groupName);

		/// <summary>
		/// Called by the <see cref="IScheduler"/> when a <see cref="JobDetailImpl"/>
		/// or group of <see cref="JobDetailImpl"/>s has been  paused.
		/// <p>
		/// If a group was paused, then the <see param="jobName"/> parameter will be
		/// null. If all jobs were paused, then both parameters will be null.
		/// </p>
		/// </summary>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		void JobsPaused(string jobName, string jobGroup);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="JobDetailImpl" />
		/// or group of <see cref="JobDetailImpl" />s has been  un-paused.
		/// <p>
		/// If a group was resumed, then the <param name="jobName" /> parameter will
		/// be null. If all jobs were paused, then both parameters will be null.
		/// </p>
		/// </summary>
		/// <param name="jobGroup">The job group.</param>
		void JobsResumed(string jobName, string jobGroup);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a serious error has
		/// occurred within the scheduler - such as repeated failures in the <see cref="IJobStore" />,
		/// or the inability to instantiate a <see cref="IJob" /> instance when its
		/// <see cref="Trigger" /> has fired.
		/// </summary>
		void SchedulerError(string msg, SchedulerException cause);

        /// <summary>
        /// Called by the <see cref="IScheduler" /> to inform the listener
        /// that it has move to standby mode.
        /// </summary>
        void SchedulerInStandbyMode();

        /// <summary>
        /// Called by the <see cref="IScheduler" /> to inform the listener
        /// that it has started.
        /// </summary>
        void SchedulerStarted();

		/// <summary> 
		/// Called by the <see cref="IScheduler" /> to inform the listener
		/// that it has Shutdown.
		/// </summary>
		void SchedulerShutdown();

        /// <summary>
        /// Called by the <see cref="IScheduler" /> to inform the listener
        /// that it has begun the shutdown sequence.
        /// </summary>
        void SchedulerShuttingdown();
	}
}