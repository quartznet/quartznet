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

using System.Threading;

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
	/// <see cref="IJob" /> itself (the <see cref="Interrupt" /> method of this 
	/// interface is simply a means for the scheduler to inform the <see cref="IJob" />
	/// that a request has been made for it to be interrupted). The mechanism that
	/// your jobs use to interrupt themselves might vary between implementations.
	/// However the principle idea in any implementation should be to have the
	/// body of the job's <see cref="IJob.Execute" /> periodically check some flag to
	/// see if an interruption has been requested, and if the flag is set, somehow
	/// abort the performance of the rest of the job's work.  An example of 
	/// interrupting a job can be found in the source for the class Example7's DumbInterruptableJob 
	/// It is legal to use
	/// some combination of <see cref="Monitor.Wait(object)" /> and <see cref="Monitor.Pulse" /> 
	/// synchronization within <see cref="Thread.Interrupt" /> and <see cref="IJob.Execute" />
	/// in order to have the <see cref="Thread.Interrupt" /> method block until the
	/// <see cref="IJob.Execute" /> signals that it has noticed the set flag.
	/// </para>
	/// 
	/// <para>
	/// If the Job performs some form of blocking I/O or similar functions, you may
	/// want to consider having the <see cref="IJob.Execute" /> method store a
	/// reference to the calling <see cref="Thread" /> as a member variable.  Then the
	/// implementation of this interfaces <see cref="Thread.Interrupt" /> method can call 
	/// <see cref="Thread.Interrupt" /> on that Thread.   Before attempting this, make
	/// sure that you fully understand what <see cref="Thread.Interrupt" /> 
	/// does and doesn't do.  Also make sure that you clear the Job's member 
	/// reference to the Thread when the Execute(..) method exits (preferably in a
	/// <see langword="finally" /> block).
	/// </para>
    /// </remarks>
	/// <seealso cref="IJob" />
	/// <seealso cref="IScheduler.Interrupt(JobKey)"/>
    /// <seealso cref="IScheduler.Interrupt(string)"/>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface IInterruptableJob : IJob
	{
		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a user
		/// interrupts the <see cref="IJob" />.
		/// </summary>
		/// <returns> void (nothing) if job interrupt is successful.</returns>
		void Interrupt();
	}
}