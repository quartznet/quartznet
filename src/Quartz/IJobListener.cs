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
	/// <summary>
	/// The interface to be implemented by classes that want to be informed when a
	/// <code>JobDetail</code> executes. In general,  applications that use a 
	/// <code>Scheduler</code> will not have use for this mechanism.
	/// </summary>
	/// <seealso cref="IScheduler" />
	/// <seealso cref="IJob" />
	/// <seealso cref="JobExecutionContext" />
	/// <seealso cref="JobExecutionException" />
	/// <seealso cref="ITriggerListener" />
	/// <author>James House</author>
	public interface IJobListener
	{
		/// <summary>
		/// Get the name of the <code>JobListener</code>.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Called by the <code>IScheduler</code> when a <code>JobDetail</code>
		/// is about to be executed (an associated <code>Trigger</code>
		/// has occured).
		/// <p>
		/// This method will not be invoked if the execution of the Job was vetoed
		/// by a <code>TriggerListener</code>.
		/// </p>
		/// </summary>
		/// <seealso cref="JobExecutionVetoed(JobExecutionContext)" />
		void JobToBeExecuted(JobExecutionContext context);

		/// <summary>
		/// Called by the <code>IScheduler</code> when a <code>JobDetail</code>
		/// was about to be executed (an associated <code>Trigger</code>
		/// has occured), but a <code>TriggerListener</code> vetoed it's 
		/// execution.
		/// </summary>
		/// <seealso cref="JobToBeExecuted(JobExecutionContext)" />
		void JobExecutionVetoed(JobExecutionContext context);


		/// <summary>
		/// Called by the <code>Scheduler</code> after a <code>JobDetail</code>
		/// has been executed, and be for the associated <code>Trigger</code>'s
		/// <code>Triggered(xx)</code> method has been called.
		/// </summary>
		void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException);
	}
}