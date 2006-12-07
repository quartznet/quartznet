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
using System;

namespace Quartz
{
	/// <summary> <p>
	/// An exception that can be thrown by a <code>{@link org.quartz.Job}</code>
	/// to indicate to the Quartz <code>{@link Scheduler}</code> that an error
	/// occured while executing, and whether or not the <code>Job</code> requests
	/// to be re-fired immediately (using the same <code>{@link JobExecutionContext}</code>,
	/// or whether it wants to be unscheduled.
	/// </p>
	/// 
	/// <p>
	/// Note that if the flag for 'refire immediately' is set, the flags for
	/// unscheduling the Job are ignored.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IJob">
	/// </seealso>
	/// <seealso cref="JobExecutionContext">
	/// </seealso>
	/// <seealso cref="SchedulerException">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	[Serializable]
	public class JobExecutionException : SchedulerException
	{
		public virtual bool UnscheduleFiringTrigger
		{
			set { unscheduleTrigg = value; }
		}

		public virtual bool UnscheduleAllTriggers
		{
			set { unscheduleAllTriggs = value; }
		}

		private bool refire = false;
		private bool unscheduleTrigg = false;
		private bool unscheduleAllTriggs = false;

		/// <summary> <p>
		/// Create a JobExcecutionException, with the 're-fire immediately' flag set
		/// to <code>false</code>.
		/// </p>
		/// </summary>
		public JobExecutionException()
		{
		}

		/// <summary> <p>
		/// Create a JobExcecutionException, with the given cause.
		/// </p>
		/// </summary>
		public JobExecutionException(Exception cause) : base(cause)
		{
		}

		/// <summary> <p>
		/// Create a JobExcecutionException, with the given message.
		/// </p>
		/// </summary>
		public JobExecutionException(string msg) : base(msg)
		{
		}

		/// <summary> <p>
		/// Create a JobExcecutionException with the 're-fire immediately' flag set
		/// to the given value.
		/// </p>
		/// </summary>
		public JobExecutionException(bool refireImmediately)
		{
			refire = refireImmediately;
		}

		/// <summary> <p>
		/// Create a JobExcecutionException with the given underlying exception, and
		/// the 're-fire immediately' flag set to the given value.
		/// </p>
		/// </summary>
		public JobExecutionException(Exception cause, bool refireImmediately) : base(cause)
		{
			refire = refireImmediately;
		}

		/// <summary> <p>
		/// Create a JobExcecutionException with the given message, and underlying
		/// exception, and the 're-fire immediately' flag set to the given value.
		/// </p>
		/// </summary>
		public JobExecutionException(string msg, Exception cause, bool refireImmediately) : base(msg, cause)
		{
			refire = refireImmediately;
		}

		public virtual bool RefireImmediately()
		{
			return refire;
		}

		public virtual bool unscheduleFiringTrigger()
		{
			return unscheduleTrigg;
		}

		public virtual bool unscheduleAllTriggers()
		{
			return unscheduleAllTriggs;
		}
	}
}