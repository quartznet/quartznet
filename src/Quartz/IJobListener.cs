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
    /// <see cref="IJobDetail" /> executes. In general,  applications that use a 
	/// <see cref="IScheduler" /> will not have use for this mechanism.
	/// </summary>
    /// <seealso cref="IListenerManager.AddJobListener(Quartz.IJobListener,System.Collections.Generic.IList{Quartz.IMatcher{Quartz.JobKey}})" />
    /// <seealso cref="IMatcher{T}" />
	/// <seealso cref="IJob" />
	/// <seealso cref="IJobExecutionContext" />
	/// <seealso cref="JobExecutionException" />
	/// <seealso cref="ITriggerListener" />
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface IJobListener
	{
		/// <summary>
		/// Get the name of the <see cref="IJobListener" />.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
		/// is about to be executed (an associated <see cref="ITrigger" />
		/// has occurred).
		/// <para>
		/// This method will not be invoked if the execution of the Job was vetoed
		/// by a <see cref="ITriggerListener" />.
		/// </para>
		/// </summary>
		/// <seealso cref="JobExecutionVetoed(IJobExecutionContext)" />
		void JobToBeExecuted(IJobExecutionContext context);

		/// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
		/// was about to be executed (an associated <see cref="ITrigger" />
		/// has occurred), but a <see cref="ITriggerListener" /> vetoed it's 
		/// execution.
		/// </summary>
        /// <seealso cref="JobToBeExecuted(IJobExecutionContext)" />
        void JobExecutionVetoed(IJobExecutionContext context);


		/// <summary>
        /// Called by the <see cref="IScheduler" /> after a <see cref="IJobDetail" />
        /// has been executed, and be for the associated <see cref="IOperableTrigger" />'s
		/// <see cref="IOperableTrigger.Triggered" /> method has been called.
		/// </summary>
        void JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException);
	}
}