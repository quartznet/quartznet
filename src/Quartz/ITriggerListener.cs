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
	/// <code>Trigger</code> fires. In general, applications that use a
	/// <code>Scheduler</code> will not have use for this mechanism.
	/// </summary>
	/// <seealso cref="IScheduler" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="IJobListener" />
	/// <seealso cref="JobExecutionContext" />
	/// <author>James House</author>
	public interface ITriggerListener
	{
		/// <summary>
		/// Get the name of the <code>TriggerListener</code>.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Called by the <code>IScheduler}</code> when a <code>Trigger</code>
		/// has fired, and it's associated <code>JobDetail</code>
		/// is about to be executed.
		/// <p>
		/// It is called before the <code>VetoJobExecution(..)</code> method of this
		/// interface.
		/// </p>
		/// </summary>
		/// <param name="trigger">The <code>Trigger</code> that has fired.</param>
		/// <param name="context">
		///     The <code>JobExecutionContext</code> that will be passed to the <code>IJob</code>'s<code>Execute(xx)</code> method.
		/// </param>
		void TriggerFired(Trigger trigger, JobExecutionContext context);

		/// <summary>
		/// Called by the <code>IScheduler</code> when a <code>Trigger</code>
		/// has fired, and it's associated <code>JobDetail</code>
		/// is about to be executed.
		/// <p>
		/// It is called after the <code>TriggerFired(..)</code> method of this
		/// interface.
		/// </p>
		/// </summary>
		/// <param name="trigger">The <code>Trigger</code> that has fired.</param>
		/// <param name="context">
		/// The <code>JobExecutionContext</code> that will be passed to
		/// the <code>Job</code>'s<code>Execute(xx)</code> method.
		/// </param>
		bool VetoJobExecution(Trigger trigger, JobExecutionContext context);


		/// <summary>
		/// Called by the <code>IScheduler</code> when a <code>Trigger</code>
		/// has misfired.
		/// <p>
		/// Consideration should be given to how much time is spent in this method,
		/// as it will affect all triggers that are misfiring.  If you have lots
		/// of triggers misfiring at once, it could be an issue it this method
		/// does a lot.
		/// </p>
		/// </summary>
		/// <param name="trigger">The <code>Trigger</code> that has misfired.</param>
		void TriggerMisfired(Trigger trigger);

		/// <summary>
		/// Called by the <code>IScheduler</code> when a <code>Trigger</code>
		/// has fired, it's associated <code>JobDetail</code>
		/// has been executed, and it's <code>Triggered(xx)</code> method has been
		/// called.
		/// </summary>
		/// <param name="trigger">The <code>Trigger</code> that was fired.</param>
		/// <param name="context">
		/// The <code>JobExecutionContext</code> that was passed to the
		/// <code>Job</code>'s<code>Execute(xx)</code> method.
		/// </param>
		/// <param name="triggerInstructionCode">
		/// The result of the call on the <code>Trigger</code>'s<code>triggered(xx)</code>  method.
		/// </param>
		void TriggerComplete(Trigger trigger, JobExecutionContext context, int triggerInstructionCode);
	}
}