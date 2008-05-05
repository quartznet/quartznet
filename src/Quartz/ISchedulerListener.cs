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
	/// <author> James House</author>
	public interface ISchedulerListener
	{
		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="JobDetail" />
		/// is scheduled.
		/// </summary>
		void JobScheduled(Trigger trigger);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="JobDetail" />
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
		/// Called by the <see cref="IScheduler"/> when a <see cref="JobDetail"/>
		/// or group of <see cref="JobDetail"/>s has been  paused.
		/// <p>
		/// If a group was paused, then the <see param="jobName"/> parameter will be
		/// null. If all jobs were paused, then both parameters will be null.
		/// </p>
		/// </summary>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		void JobsPaused(string jobName, string jobGroup);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="JobDetail" />
		/// or group of <see cref="JobDetail" />s has been  un-paused.
		/// <p>
		/// If a group was resumed, then the <param name="jobName" /> parameter will
		/// be null. If all jobs were paused, then both parameters will be null.
		/// </p>
		/// </summary>
		/// <param name="jobGroup">The job group.</param>
		void JobsResumed(string jobName, string jobGroup);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a serious error has
		/// occured within the scheduler - such as repeated failures in the <see cref="IJobStore" />,
		/// or the inability to instantiate a <see cref="IJob" /> instance when its
		/// <see cref="Trigger" /> has fired.
		/// <p>
		/// The <see cref="SchedulerException.ErrorCode" /> property of the given SchedulerException
		/// can be used to determine more specific information about the type of
		/// error that was encountered.
		/// </p>
		/// </summary>
		void SchedulerError(string msg, SchedulerException cause);

		/// <summary> 
		/// Called by the <see cref="IScheduler" /> to inform the listener
		/// that it has Shutdown.
		/// </summary>
		void SchedulerShutdown();
	}
}