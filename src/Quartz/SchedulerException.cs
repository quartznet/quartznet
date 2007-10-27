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
using System.Globalization;

namespace Quartz
{
    /// <summary> 
    /// Base class for exceptions thrown by the Quartz <see cref="IScheduler" />.
    /// </summary>
    /// <remarks>
    /// SchedulerExceptions may contain a reference to another
    /// <see cref="Exception" />, which was the underlying cause of the SchedulerException.
    /// </remarks>
    /// <author>James House</author>
    [Serializable]
    public class SchedulerException : Exception
    {
        public const int ErrorBadConfiguration = 50;

        public const int ErrorClientError = 100;

        public const int ErrorCommunicationFailure = 200;
        public const int ErrorJobExecutionThrewException = 800;
        public const int ErrorJobListener = 600;

        public const int ErrorJobListenerNotFound = 610;

        public const int ErrorPersistence = 400;

        public const int ErrorPersistenceCalendarDoesNotExist = 420;

        public const int ErrorPersistenceCriticalFailure = 499;
        public const int ErrorPersistenceJobDoesNotExist = 410;
        public const int ErrorPersistenceTriggerDoesNotExist = 430;

        public const int ErrorThreadPool = 500;

        public const int ErrorThreadPoolCriticalFailure = 599;
        public const int ErrorThreadPoolExhausted = 510;

        public const int ErrorTriggerListener = 700;

        public const int ErrorTriggerListenerNotFound = 710;

        public const int ErrorTriggerThrewException = 850;
        public const int ErrorUnspecified = 0;
        public const int ErrorUnsupportedFunctionInThisConfiguration = 210;


        private readonly Exception cause;

        private int errorCode = ErrorUnspecified;


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
        /// Return the exception that is the underlying cause of this exception.
        /// This may be used to find more detail about the cause of the error.
        /// </summary>
        /// <returns> The underlying exception, or <see langword="null" /> if there is not
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
        /// Determine if the specified error code is in the <see cref="ErrorPersistence" />
        /// category of errors.
        /// </p>
        /// </summary>
        public virtual bool PersistenceError
        {
            get { return (errorCode >= ErrorPersistence && errorCode <= ErrorPersistence + 99); }
        }

        /// <summary> <p>
        /// Determine if the specified error code is in the <see cref="ErrorThreadPool" />
        /// category of errors.
        /// </p>
        /// </summary>
        public virtual bool ThreadPoolError
        {
            get { return (errorCode >= ErrorThreadPool && errorCode <= ErrorThreadPool + 99); }
        }

        /// <summary>
        /// Determine if the specified error code is in the <see cref="ErrorJobListener" />
        /// category of errors.
        /// </summary>
        public virtual bool JobListenerError
        {
            get { return (errorCode >= ErrorJobListener && errorCode <= ErrorJobListener + 99); }
        }

        /// <summary>
        /// Determine if the specified error code is in the <see cref="ErrorTriggerListener" />
        /// category of errors.
        /// </summary>
        public virtual bool TriggerListenerError
        {
            get { return (errorCode >= ErrorTriggerListener && errorCode <= ErrorTriggerListener + 99); }
        }

        /// <summary>
        /// Determine if the specified error code is in the <see cref="ErrorClientError" />
        /// category of errors.
        /// </summary>
        public virtual bool ClientError
        {
            get { return (errorCode >= ErrorClientError && errorCode <= ErrorClientError + 99); }
        }

        /// <summary>
        /// Determine if the specified error code is in the <see cref="ErrorClientError" />
        /// category of errors.
        /// </summary>
        public virtual bool ConfigurationError
        {
            get { return (errorCode >= ErrorBadConfiguration && errorCode <= ErrorBadConfiguration + 49); }
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
                return string.Format(CultureInfo.InvariantCulture, "{0} [See nested exception: {1}]", base.ToString(), cause);
            }
        }
    }
}