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
using System.Runtime.Serialization;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary>
	/// Exception class for when a driver delegate cannot be found for a given
	/// configuration, or lack thereof.
	/// </summary>
	/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
	/// <author>Marko Lahma (.NET)</author>
	[Serializable]
	public class NoSuchDelegateException : JobPersistenceException
	{
		public NoSuchDelegateException(string msg, Exception cause) : base(msg, cause)
		{
		}

		public NoSuchDelegateException(string msg) : base(msg)
		{
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="NoSuchDelegateException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo"></see> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext"></see> that contains contextual information about the source or destination.</param>
        /// <exception cref="T:System.Runtime.Serialization.SerializationException">The class name is null or <see cref="P:System.Exception.HResult"></see> is zero (0). </exception>
        /// <exception cref="T:System.ArgumentNullException">The info parameter is null. </exception>
        protected NoSuchDelegateException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

	}
}