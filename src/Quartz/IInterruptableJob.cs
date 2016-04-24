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

using System;

namespace Quartz
{
	/// <summary>
	/// The interface to be implemented by <see cref="IJob" />s that provide a 
	/// mechanism for having their execution interrupted.  It is NOT a requirement
	/// for jobs to implement this interface - in fact, for most people, none of
	/// their jobs will.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The means of actually interrupting the Job must be implemented within the
	/// <see cref="IJob" /> itself by observing the cancellation token <see cref="IJobExecutionContext.CancellationToken" />. The mechanism that
	/// your jobs use to interrupt themselves might vary between implementations.
	/// However the principle idea in any implementation should be to have the
	/// body of the job's <see cref="IJob.Execute" /> periodically check the cancellation token or pass it onto the asynchronous method that support cancellation.  An example of 
	/// interrupting a job can be found in the source for the class Example7's DumbInterruptableJob 
	/// </para>
    /// </remarks>
	/// <seealso cref="IJob" />
	/// <seealso cref="IScheduler.Interrupt(JobKey)"/>
    /// <seealso cref="IScheduler.Interrupt(string)"/>
    /// <author>Marko Lahma (.NET)</author>
    [Obsolete("You should check for JobExecutionContext.CancellationToken")]
    public interface IInterruptableJob : IJob
	{
	}
}