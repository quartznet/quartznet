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
using System.Globalization;
using System.Runtime.Serialization;

namespace Quartz
{
	/// <summary>
	/// An exception that is thrown to indicate that an attempt to store a new
	/// object (i.e. <see cref="JobDetail" />,<see cref="Trigger" />
	/// or <see cref="ICalendar" />) in a <see cref="IScheduler" />
	/// failed, because one with the same name and group already exists.
	/// </summary>
	/// <author>James House</author>
	[Serializable]
	public class ObjectAlreadyExistsException : JobPersistenceException
	{
		/// <summary> <p>
		/// Create a <see cref="ObjectAlreadyExistsException" /> with the given
		/// message.
		/// </p>
		/// </summary>
		public ObjectAlreadyExistsException(string msg) : base(msg)
		{
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectAlreadyExistsException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo"></see> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext"></see> that contains contextual information about the source or destination.</param>
        /// <exception cref="T:System.Runtime.Serialization.SerializationException">The class name is null or <see cref="P:System.Exception.HResult"></see> is zero (0). </exception>
        /// <exception cref="T:System.ArgumentNullException">The info parameter is null. </exception>
        public ObjectAlreadyExistsException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

		/// <summary> <p>
		/// Create a <see cref="ObjectAlreadyExistsException" /> and auto-generate a
		/// message using the name/group from the given <see cref="JobDetail" />.
		/// </p>
		/// 
		/// <p>
		/// The message will read: <br />"Unable to store Job with name: '__' and
		/// group: '__', because one already exists with this identification."
		/// </p>
		/// </summary>
		public ObjectAlreadyExistsException(JobDetail offendingJob)
			: base(
				string.Format(CultureInfo.InvariantCulture, "Unable to store Job with name: '{0}' and group: '{1}', because one already exists with this identification.", offendingJob.Name, offendingJob.Group))
		{
		}

		/// <summary> <p>
		/// Create a <see cref="ObjectAlreadyExistsException" /> and auto-generate a
		/// message using the name/group from the given <see cref="Trigger" />.
		/// </p>
		/// 
		/// <p>
		/// The message will read: <br />"Unable to store Trigger with name: '__' and
		/// group: '__', because one already exists with this identification."
		/// </p>
		/// </summary>
		public ObjectAlreadyExistsException(Trigger offendingTrigger)
			: base(
				string.Format(CultureInfo.InvariantCulture, "Unable to store Trigger with name: '{0}' and group: '{1}', because one already exists with this identification.", offendingTrigger.Name, offendingTrigger.Group))
		{
		}
	}
}
