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
	/// The interface to be implemented by <code>{@link Job}s</code> that provide a 
	/// mechanism for having their execution interrupted.  It is NOT a requirment
	/// for jobs to implement this interface - in fact, for most people, none of
	/// their jobs will.
	/// </p>
	/// 
	/// <p>
	/// The means of actually interrupting the Job must be implemented within the
	/// <code>Job</code> itself (the <code>interrupt()</code> method of this 
	/// interface is simply a means for the scheduler to inform the <code>Job</code>
	/// that a request has been made for it to be interrupted). The mechanism that
	/// your jobs use to interrupt themselves might vary between implementations.
	/// However the principle idea in any implementation should be to have the
	/// body of the job's <code>Execute(..)</code> periodically check some flag to
	/// see if an interruption has been requested, and if the flag is set, somehow
	/// abort the performance of the rest of the job's work.  An example of 
	/// interrupting a job can be found in the java source for the  class 
	/// <code>Quartz.Examples.Example2.DumbInterruptableJob</code>.  It is legal to use
	/// some combination of <code>wait()</code> and <code>notify()</code> 
	/// synchronization within <code>interrupt()</code> and <code>Execute(..)</code>
	/// in order to have the <code>interrupt()</code> method block until the
	/// <code>Execute(..)</code> signals that it has noticed the set flag.
	/// </p>
	/// 
	/// <p>
	/// If the Job performs some form of blocking I/O or similar functions, you may
	/// want to consider having the <code>Job.Execute(..)</code> method store a
	/// reference to the calling <code>Thread</code> as a member variable.  Then the
	/// impplementation of this interfaces <code>interrupt()</code> method can call 
	/// <code>interrupt()</code> on that Thread.   Before attempting this, make
	/// sure that you fully understand what <code>java.lang.Thread.interrupt()</code> 
	/// does and doesn't do.  Also make sure that you clear the Job's member 
	/// reference to the Thread when the Execute(..) method exits (preferrably in a
	/// <code>finally</code> block.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IJob">
	/// </seealso>
	/// <seealso cref="IStatefulJob">
	/// </seealso>
	/// <author>  James House
	/// </author>
	public interface IInterruptableJob : IJob
	{
		/// <summary> <p>
		/// Called by the <code>{@link Scheduler}</code> when a user
		/// interrupts the <code>Job</code>.
		/// </p>
		/// 
		/// </summary>
		/// <returns> void (nothing) if job interrupt is successful.
		/// </returns>
		/// <throws>  UnableToInterruptJobException </throws>
		/// <summary>           if there is an exception while interrupting the job.
		/// </summary>
		void Interrupt();
	}
}