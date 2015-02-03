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
	/// The interface to be implemented by classes that want to be informed when a
	/// <see cref="ITrigger" /> fires. In general, applications that use a
	/// <see cref="IScheduler" /> will not have use for this mechanism.
	/// </summary>
	/// <seealso cref="IListenerManager" />
    /// <seealso cref="IMatcher{T}" />
    /// <seealso cref="ITrigger" />
	/// <seealso cref="IJobListener" />
	/// <seealso cref="IJobExecutionContext" />
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface ITriggerListener
	{
		/// <summary>
		/// Get the name of the <see cref="ITriggerListener" />.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
		/// has fired, and it's associated <see cref="IJobDetail" />
		/// is about to be executed.
		/// <para>
		/// It is called before the <see cref="VetoJobExecution" /> method of this
		/// interface.
		/// </para>
		/// </summary>
		/// <param name="trigger">The <see cref="ITrigger" /> that has fired.</param>
		/// <param name="context">
		///     The <see cref="IJobExecutionContext" /> that will be passed to the <see cref="IJob" />'s<see cref="IJob.Execute" /> method.
		/// </param>
		void TriggerFired(ITrigger trigger, IJobExecutionContext context);

        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="ITrigger"/>
        /// has fired, and it's associated <see cref="IJobDetail"/>
        /// is about to be executed.
        /// <para>
        /// It is called after the <see cref="TriggerFired"/> method of this
        /// interface.  If the implementation vetoes the execution (via
        /// returning <see langword="true" />), the job's execute method will not be called.
        /// </para>
        /// </summary>
        /// <param name="trigger">The <see cref="ITrigger"/> that has fired.</param>
        /// <param name="context">The <see cref="IJobExecutionContext"/> that will be passed to
        /// the <see cref="IJob"/>'s<see cref="IJob.Execute"/> method.</param>
        /// <returns>Returns true if job execution should be vetoed, false otherwise.</returns>
        bool VetoJobExecution(ITrigger trigger, IJobExecutionContext context);


		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
		/// has misfired.
		/// <para>
		/// Consideration should be given to how much time is spent in this method,
		/// as it will affect all triggers that are misfiring.  If you have lots
		/// of triggers misfiring at once, it could be an issue it this method
		/// does a lot.
		/// </para>
		/// </summary>
		/// <param name="trigger">The <see cref="ITrigger" /> that has misfired.</param>
        void TriggerMisfired(ITrigger trigger);

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
		/// has fired, it's associated <see cref="IJobDetail" />
		/// has been executed, and it's <see cref="IOperableTrigger.Triggered" /> method has been
		/// called.
		/// </summary>
		/// <param name="trigger">The <see cref="ITrigger" /> that was fired.</param>
		/// <param name="context">
        /// The <see cref="IJobExecutionContext" /> that was passed to the
		/// <see cref="IJob" />'s<see cref="IJob.Execute" /> method.
		/// </param>
		/// <param name="triggerInstructionCode">
		/// The result of the call on the <see cref="ITrigger" />'s<see cref="IOperableTrigger.Triggered" />  method.
		/// </param>
		void TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode);
	}
}
