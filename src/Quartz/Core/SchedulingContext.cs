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

namespace Quartz.Core
{
	/// <summary>
	/// An object used to pass information about the 'client' to the <code>QuartzScheduler</code>.
	/// </summary>
	/// <seealso cref="QuartzScheduler" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	[Serializable]
	public class SchedulingContext
	{
		private string instanceId;

		/// <summary>
		/// get the instanceId in the cluster.
		/// </summary>
		/// <summary> <p>
		/// Set the instanceId.
		/// </p>
		/// </summary>
		public virtual string InstanceId
		{
			get { return instanceId; }
			set { instanceId = value; }
		}
	}
}