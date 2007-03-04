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

namespace Quartz
{
	/// <summary> <p>
	/// The interface to be implemented by classes that want to be informed of major
	/// <code>{@link Scheduler}</code> events.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IScheduler">
	/// </seealso>
	/// <seealso cref="IJobListener">
	/// </seealso>
	/// <seealso cref="ITriggerListener">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	public interface ISchedulerListener
	{
		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link org.quartz.JobDetail}</code>
		/// is scheduled.
		/// </p>
		/// </summary>
		void JobScheduled(Trigger trigger);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link org.quartz.JobDetail}</code>
		/// is unscheduled.
		/// </p>
		/// </summary>
		void JobUnscheduled(string triggerName, string triggerGroup);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link Trigger}</code>
		/// has reached the condition in which it will never fire again.
		/// </p>
		/// </summary>
		void TriggerFinalized(Trigger trigger);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link Trigger}</code>
		/// or group of <code>{@link Trigger}s</code> has been paused.
		/// </p>
		/// 
		/// <p>
		/// If a group was paused, then the <code>triggerName</code> parameter
		/// will be null.
		/// </p>
		/// </summary>
		void TriggersPaused(string triggerName, string triggerGroup);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link Trigger}</code>
		/// or group of <code>{@link Trigger}s</code> has been un-paused.
		/// </p>
		/// 
		/// <p>
		/// If a group was resumed, then the <code>triggerName</code> parameter
		/// will be null.
		/// </p>
		/// </summary>
		void TriggersResumed(string triggerName, string triggerGroup);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link org.quartz.JobDetail}</code>
		/// or group of <code>{@link org.quartz.JobDetail}s</code> has been
		/// paused.
		/// </p>
		/// 
		/// <p>
		/// If a group was paused, then the <code>jobName</code> parameter will be
		/// null. If all jobs were paused, then both parameters will be null.
		/// </p>
		/// </summary>
		void JobsPaused(string jobName, string jobGroup);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link org.quartz.JobDetail}</code>
		/// or group of <code>{@link org.quartz.JobDetail}s</code> has been
		/// un-paused.
		/// </p>
		/// 
		/// <p>
		/// If a group was resumed, then the <code>jobName</code> parameter will
		/// be null. If all jobs were paused, then both parameters will be null.
		/// </p>
		/// </summary>
		void JobsResumed(string jobName, string jobGroup);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a serious error has
		/// occured within the scheduler - such as repeated failures in the <code>JobStore</code>,
		/// or the inability to instantiate a <code>Job</code> instance when its
		/// <code>Trigger</code> has fired.
		/// </p>
		/// 
		/// <p>
		/// The <code>getErrorCode()</code> method of the given SchedulerException
		/// can be used to determine more specific information about the type of
		/// error that was encountered.
		/// </p>
		/// </summary>
		void SchedulerError(string msg, SchedulerException cause);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> to inform the listener
		/// that it has Shutdown.
		/// </p>
		/// </summary>
		void SchedulerShutdown();
	}
}