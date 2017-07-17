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
	/// An exception that is thrown to indicate that there is a misconfiguration of
	/// the <see cref="ISchedulerFactory" />- or one of the components it
	/// configures.
	/// </summary>
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    public class SchedulerConfigException : SchedulerException
	{
		/// <summary>
		/// Create a <see cref="JobPersistenceException" /> with the given message.
		/// </summary>
		public SchedulerConfigException(string msg) : base(msg)
		{
		}

		/// <summary>
		/// Create a <see cref="JobPersistenceException" /> with the given message
		/// and cause.
		/// </summary>
		public SchedulerConfigException(string msg, Exception cause) : base(msg, cause)
		{
		}

#if BINARY_SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerConfigException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo"></see> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext"></see> that contains contextual information about the source or destination.</param>
        /// <exception cref="T:System.Runtime.Serialization.SerializationException">The class name is null or <see cref="P:System.Exception.HResult"></see> is zero (0). </exception>
        /// <exception cref="T:System.ArgumentNullException">The info parameter is null. </exception>
        public SchedulerConfigException(
			System.Runtime.Serialization.SerializationInfo info, 
			System.Runtime.Serialization.StreamingContext context) : base(info, context)
        {
        }
#endif // BINARY_SERIALIZATION
    }
}
