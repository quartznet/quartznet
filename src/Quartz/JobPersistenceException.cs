/* 
* Copyright 2004-2009 James House 
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
using System.Runtime.Serialization;

namespace Quartz
{
	/// <summary>
	/// An exception that is thrown to indicate that there has been a failure in the
	/// scheduler's underlying persistence mechanism.
	/// </summary>
	/// <author>  James House
	/// </author>
	[Serializable]
	public class JobPersistenceException : SchedulerException
	{
		/// <summary> <p>
		/// Create a <see cref="JobPersistenceException" /> with the given message.
		/// </p>
		/// </summary>
		public JobPersistenceException(string msg) : base(msg)
		{
			ErrorCode = ErrorPersistence;
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="JobPersistenceException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo"></see> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext"></see> that contains contextual information about the source or destination.</param>
        /// <exception cref="T:System.Runtime.Serialization.SerializationException">The class name is null or <see cref="P:System.Exception.HResult"></see> is zero (0). </exception>
        /// <exception cref="T:System.ArgumentNullException">The info parameter is null. </exception>
        public JobPersistenceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

		/// <summary> <p>
		/// Create a <see cref="JobPersistenceException" /> with the given message
		/// and error code.
		/// </p>
		/// </summary>
		public JobPersistenceException(string msg, int errCode) : base(msg, errCode)
		{
		}

		/// <summary> <p>
		/// Create a <see cref="JobPersistenceException" /> with the given message
		/// and cause.
		/// </p>
		/// </summary>
		public JobPersistenceException(string msg, Exception cause) : base(msg, cause)
		{
			ErrorCode = ErrorPersistence;
		}

		/// <summary> <p>
		/// Create a <see cref="JobPersistenceException" /> with the given message,
		/// cause and error code.
		/// </p>
		/// </summary>
		public JobPersistenceException(string msg, Exception cause, int errorCode) : base(msg, cause, errorCode)
		{
		}

	}
}
