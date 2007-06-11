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
	/// An exception that is thrown to indicate that an attempt to store a new
	/// object (i.e. <see cref="JobDetail" />,<see cref="Trigger" />
	/// or <see cref="Calendar" />) in a <see cref="IScheduler" />
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
				"Unable to store Job with name: '" + offendingJob.Name + "' and group: '" + offendingJob.Group +
				"', because one already exists with this identification.")
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
				"Unable to store Trigger with name: '" + offendingTrigger.Name + "' and group: '" + offendingTrigger.Group +
				"', because one already exists with this identification.")
		{
		}
	}
}