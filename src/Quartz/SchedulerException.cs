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
	/// Base class for exceptions thrown by the Quartz <code>Scheduler</code>.
	/// <p>
	/// <code>SchedulerException</code> s may contain a reference to another
	/// <code>Exception</code>, which was the underlying cause of the <code>SchedulerException</code>.
	/// </p>
	/// </summary>
	/// <author>James House</author>
	[Serializable]
	public class SchedulerException : ApplicationException
	{
		/// <summary>
		/// Return the exception that is the underlying cause of this exception.
		/// This may be used to find more detail about the cause of the error.
		/// </summary>
		/// <returns> The underlying exception, or <code>null</code> if there is not
		/// one.
		/// </returns>
		public virtual Exception UnderlyingException
		{
			get { return cause; }
		}

		/// <summary>
		/// Get the error code associated with this exception.
		/// This may be used to find more detail about the cause of the error.
		/// </summary>
		/// <returns> 
		/// One of the ERR_XXX constants defined in this class.
		/// </returns>
		public int ErrorCode
		{
			get { return errorCode; }
			set { errorCode = value; }
		}

		/// <summary> <p>
		/// Determine if the specified error code is in the <code>'ERR_PERSISTENCE'</code>
		/// category of errors.
		/// </p>
		/// </summary>
		public virtual bool PersistenceError
		{
			get { return (errorCode >= ERR_PERSISTENCE && errorCode <= ERR_PERSISTENCE + 99); }
		}

		/// <summary> <p>
		/// Determine if the specified error code is in the <code>'ERR_THREAD_POOL'</code>
		/// category of errors.
		/// </p>
		/// </summary>
		public virtual bool ThreadPoolError
		{
			get { return (errorCode >= ERR_THREAD_POOL && errorCode <= ERR_THREAD_POOL + 99); }
		}

		/// <summary> <p>
		/// Determine if the specified error code is in the <code>'ERR_JOB_LISTENER'</code>
		/// category of errors.
		/// </p>
		/// </summary>
		public virtual bool JobListenerError
		{
			get { return (errorCode >= ERR_JOB_LISTENER && errorCode <= ERR_JOB_LISTENER + 99); }
		}

		/// <summary> <p>
		/// Determine if the specified error code is in the <code>'ERR_TRIGGER_LISTENER'</code>
		/// category of errors.
		/// </p>
		/// </summary>
		public virtual bool TriggerListenerError
		{
			get { return (errorCode >= ERR_TRIGGER_LISTENER && errorCode <= ERR_TRIGGER_LISTENER + 99); }
		}

		/// <summary> <p>
		/// Determine if the specified error code is in the <code>'ERR_CLIENT_ERROR'</code>
		/// category of errors.
		/// </p>
		/// </summary>
		public virtual bool ClientError
		{
			get { return (errorCode >= ERR_CLIENT_ERROR && errorCode <= ERR_CLIENT_ERROR + 99); }
		}

		/// <summary> <p>
		/// Determine if the specified error code is in the <code>'ERR_CLIENT_ERROR'</code>
		/// category of errors.
		/// </p>
		/// </summary>
		public virtual bool ConfigurationError
		{
			get { return (errorCode >= ERR_BAD_CONFIGURATION && errorCode <= ERR_BAD_CONFIGURATION + 49); }
		}


		public const int ERR_UNSPECIFIED = 0;

		public const int ERR_BAD_CONFIGURATION = 50;

		public const int ERR_TIME_BROKER_FAILURE = 70;

		public const int ERR_CLIENT_ERROR = 100;

		public const int ERR_COMMUNICATION_FAILURE = 200;

		public const int ERR_UNSUPPORTED_FUNCTION_IN_THIS_CONFIGURATION = 210;

		public const int ERR_PERSISTENCE = 400;

		public const int ERR_PERSISTENCE_JOB_DOES_NOT_EXIST = 410;

		public const int ERR_PERSISTENCE_CALENDAR_DOES_NOT_EXIST = 420;

		public const int ERR_PERSISTENCE_TRIGGER_DOES_NOT_EXIST = 430;

		public const int ERR_PERSISTENCE_CRITICAL_FAILURE = 499;

		public const int ERR_THREAD_POOL = 500;

		public const int ERR_THREAD_POOL_EXHAUSTED = 510;

		public const int ERR_THREAD_POOL_CRITICAL_FAILURE = 599;

		public const int ERR_JOB_LISTENER = 600;

		public const int ERR_JOB_LISTENER_NOT_FOUND = 610;

		public const int ERR_TRIGGER_LISTENER = 700;

		public const int ERR_TRIGGER_LISTENER_NOT_FOUND = 710;

		public const int ERR_JOB_EXECUTION_THREW_EXCEPTION = 800;

		public const int ERR_TRIGGER_THREW_EXCEPTION = 850;


		private Exception cause;

		private int errorCode = ERR_UNSPECIFIED;


        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerException"/> class.
        /// </summary>
		public SchedulerException() 
		{
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerException"/> class.
        /// </summary>
        /// <param name="msg">The MSG.</param>
		public SchedulerException(string msg) : base(msg)
		{
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerException"/> class.
        /// </summary>
        /// <param name="msg">The MSG.</param>
        /// <param name="errorCode">The error code.</param>
		public SchedulerException(string msg, int errorCode) : base(msg)
		{
			ErrorCode = errorCode;
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerException"/> class.
        /// </summary>
        /// <param name="cause">The cause.</param>
		public SchedulerException(Exception cause) : base(cause.ToString())
		{
			this.cause = cause;
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerException"/> class.
        /// </summary>
        /// <param name="msg">The MSG.</param>
        /// <param name="cause">The cause.</param>
		public SchedulerException(string msg, Exception cause) : base(msg)
		{
			this.cause = cause;
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerException"/> class.
        /// </summary>
        /// <param name="msg">The MSG.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="errorCode">The error code.</param>
		public SchedulerException(string msg, Exception cause, int errorCode) : base(msg)
		{
			this.cause = cause;
			ErrorCode = errorCode;
		}

        /// <summary>
        /// Creates and returns a string representation of the current exception.
        /// </summary>
        /// <returns>
        /// A string representation of the current exception.
        /// </returns>
        /// <PermissionSet><IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" PathDiscovery="*AllFiles*"/></PermissionSet>
		public override string ToString()
		{
			if (cause == null)
			{
				return base.ToString();
			}
			else
			{
				return base.ToString() + " [See nested exception: " + cause + "]";
			}
		}

	}
}