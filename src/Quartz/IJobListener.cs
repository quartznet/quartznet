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
	/// The interface to be implemented by classes that want to be informed when a
	/// <code>{@link org.quartz.JobDetail}</code> executes. In general,
	/// applications that use a <code>Scheduler</code> will not have use for this
	/// mechanism.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IScheduler">
	/// </seealso>
	/// <seealso cref="IJob">
	/// </seealso>
	/// <seealso cref="JobExecutionContext">
	/// </seealso>
	/// <seealso cref="JobExecutionException">
	/// </seealso>
	/// <seealso cref="ITriggerListener">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	public interface IJobListener
	{
		/// <summary> <p>
		/// Get the name of the <code>JobListener</code>.
		/// </p>
		/// </summary>
		string Name { get; }

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link org.quartz.JobDetail}</code>
		/// is about to be executed (an associated <code>{@link Trigger}</code>
		/// has occured).
		/// </p>
		/// 
		/// <p>
		/// This method will not be invoked if the execution of the Job was vetoed
		/// by a <code>{@link TriggerListener}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="JobExecutionVetoed(JobExecutionContext)">
		/// </seealso>
		void JobToBeExecuted(JobExecutionContext context);

		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a <code>{@link org.quartz.JobDetail}</code>
		/// was about to be executed (an associated <code>{@link Trigger}</code>
		/// has occured), but a <code>{@link TriggerListener}</code> vetoed it's 
		/// execution.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="JobToBeExecuted(JobExecutionContext)">
		/// </seealso>
		void JobExecutionVetoed(JobExecutionContext context);


		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> after a <code>{@link org.quartz.JobDetail}</code>
		/// has been executed, and be for the associated <code>Trigger</code>'s
		/// <code>triggered(xx)</code> method has been called.
		/// </p>
		/// </summary>
		void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException);
	}
}