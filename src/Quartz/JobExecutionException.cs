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
	/// <summary>
	/// An exception that can be thrown by a <see cref="IJob" />
	/// to indicate to the Quartz <see cref="IScheduler" /> that an error
	/// occured while executing, and whether or not the <see cref="IJob" /> requests
	/// to be re-fired immediately (using the same <see cref="JobExecutionContext" />,
	/// or whether it wants to be unscheduled.
	/// 
	/// <p>
	/// Note that if the flag for 'refire immediately' is set, the flags for
	/// unscheduling the Job are ignored.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IJob" />
	/// <seealso cref="JobExecutionContext" />
	/// <seealso cref="SchedulerException" />
	/// <author>James House</author>
	[Serializable]
	public class JobExecutionException : SchedulerException
	{
		private bool refire = false;
		private bool unscheduleTrigg = false;
		private bool unscheduleAllTriggs = false;

		/// <summary>
		/// Gets or sets a value indicating whether to unschedule firing trigger.
		/// </summary>
		/// <value>
		/// 	<c>true</c> if firing trigger should be unscheduled; otherwise, <c>false</c>.
		/// </value>
		public virtual bool UnscheduleFiringTrigger
		{
			set { unscheduleTrigg = value; }
			get { return unscheduleTrigg; }
		}

		/// <summary>
		/// Gets or sets a value indicating whether to unschedule all triggers.
		/// </summary>
		/// <value>
		/// 	<c>true</c> if all triggers should be unscheduled; otherwise, <c>false</c>.
		/// </value>
		public virtual bool UnscheduleAllTriggers
		{
			set { unscheduleAllTriggs = value; }
			get { return unscheduleAllTriggs; }
		}


		/// <summary>
		/// Create a JobExcecutionException, with the 're-fire immediately' flag set
		/// to <see langword="false" />.
		/// </summary>
		public JobExecutionException()
		{
		}

		/// <summary>
		/// Create a JobExcecutionException, with the given cause.
		/// </summary>
		/// <param name="cause">The cause.</param>
		public JobExecutionException(Exception cause) : base(cause)
		{
		}

		/// <summary>
		/// Create a JobExcecutionException, with the given message.
		/// </summary>
		public JobExecutionException(string msg) : base(msg)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="JobExecutionException"/> class.
		/// </summary>
		/// <param name="msg">The message.</param>
		/// <param name="cause">The original cause.</param>
		public JobExecutionException(string msg, Exception cause) : base(msg, cause)
		{
		}

		/// <summary>
		/// Create a JobExcecutionException with the 're-fire immediately' flag set
		/// to the given value.
		/// </summary>
		public JobExecutionException(bool refireImmediately)
		{
			refire = refireImmediately;
		}

		/// <summary>
		/// Create a JobExcecutionException with the given underlying exception, and
		/// the 're-fire immediately' flag set to the given value.
		/// </summary>
		public JobExecutionException(Exception cause, bool refireImmediately) : base(cause)
		{
			refire = refireImmediately;
		}

		/// <summary>
		/// Create a JobExcecutionException with the given message, and underlying
		/// exception, and the 're-fire immediately' flag set to the given value.
		/// </summary>
		public JobExecutionException(string msg, Exception cause, bool refireImmediately) : base(msg, cause)
		{
			refire = refireImmediately;
		}

		/// <summary>
		/// Gets or sets a value indicating whether to refire immediately.
		/// </summary>
		/// <value><c>true</c> if to refire immediately; otherwise, <c>false</c>.</value>
		public virtual bool RefireImmediately
		{
			get { return refire; }
            set { refire = value; }
		}
	}
}