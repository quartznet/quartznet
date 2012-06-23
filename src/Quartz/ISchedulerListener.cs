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
		/// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
		/// is scheduled.
		/// </summary>
		void JobScheduled(ITrigger trigger);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
		/// is unscheduled.
		/// </summary>
        /// <seealso cref="SchedulingDataCleared"/>
        void JobUnscheduled(TriggerKey triggerKey);

		/// <summary> 
		/// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
		/// has reached the condition in which it will never fire again.
		/// </summary>
        void TriggerFinalized(ITrigger trigger);
        
        /// <summary>
        /// Called by the <see cref="IScheduler"/> a <see cref="ITrigger"/>s has been paused.
        /// </summary>
        void TriggerPaused(TriggerKey triggerKey);

		/// <summary>
		/// Called by the <see cref="IScheduler"/> a group of 
		/// <see cref="ITrigger"/>s has been paused.
        /// </summary>
        /// <remarks>
		/// If a all groups were paused, then the <see param="triggerName"/> parameter
		/// will be null.
        /// </remarks>
		/// <param name="triggerGroup">The trigger group.</param>
		void TriggersPaused(string triggerGroup);

        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="ITrigger"/>
        /// has been un-paused.
        /// </summary>
        void TriggerResumed(TriggerKey triggerKey);

		/// <summary>
		/// Called by the <see cref="IScheduler"/> when a
		/// group of <see cref="ITrigger"/>s has been un-paused.
        /// </summary>
        /// <remarks>
		/// If all groups were resumed, then the <see param="triggerName"/> parameter
		/// will be null.
        /// </remarks>
		/// <param name="triggerGroup">The trigger group.</param>
		void TriggersResumed(string triggerGroup);

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
        /// has been added.
        /// </summary>
        /// <param name="jobDetail"></param>
        void JobAdded(IJobDetail jobDetail);

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
        /// has been deleted.
        /// </summary>
        void JobDeleted(JobKey jobKey);

        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/>
        /// has been  paused.
        /// </summary>
        void JobPaused(JobKey jobKey);

		/// <summary>
		/// Called by the <see cref="IScheduler"/> when a
		/// group of <see cref="IJobDetail"/>s has been  paused.
		/// <para>
		/// If all groups were paused, then the <see param="jobName"/> parameter will be
		/// null. If all jobs were paused, then both parameters will be null.
		/// </para>
		/// </summary>
		/// <param name="jobGroup">The job group.</param>
		void JobsPaused(string jobGroup);

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
        /// has been  un-paused.
        /// </summary>
        void JobResumed(JobKey jobKey);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
		/// has been  un-paused.
		/// </summary>
		/// <param name="jobGroup">The job group.</param>
		void JobsResumed(string jobGroup);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a serious error has
		/// occurred within the scheduler - such as repeated failures in the <see cref="IJobStore" />,
		/// or the inability to instantiate a <see cref="IJob" /> instance when its
		/// <see cref="ITrigger" /> has fired.
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
        /// Called by the <see cref="IScheduler" /> to inform the listener that it is starting.
        /// </summary>
        void SchedulerStarting();

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


        /// <summary>
        /// Called by the <see cref="IScheduler" /> to inform the listener
        /// that all jobs, triggers and calendars were deleted.
        /// </summary>
        void SchedulingDataCleared();
	}
}