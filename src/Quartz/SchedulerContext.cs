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
using System.Collections;

using Quartz.Util;

namespace Quartz
{
	/// <summary>
	/// Holds context/environment data that can be made available to Jobs as they
	/// are executed. 
	/// </summary>
	/// <seealso cref="IScheduler.Context" />
	/// <author>James House</author>
	[Serializable]
    public class SchedulerContext : StringKeyDirtyFlagMap
	{

		/// <summary>
		/// Create an empty <see cref="JobDataMap" />.
		/// </summary>
		public SchedulerContext() : base(15)
		{
		}

		/// <summary>
		/// Create a <see cref="JobDataMap" /> with the given data.
		/// </summary>
		public SchedulerContext(IDictionary map) : this()
		{
			PutAll(map);
		}

	}
}