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
using System.Collections.Generic;
using System.Runtime.Serialization;

using Quartz.Util;

namespace Quartz
{
	/// <summary>
	/// Holds context/environment data that can be made available to Jobs as they
	/// are executed. 
	/// </summary>
	/// <remarks>
	/// Future versions of Quartz may make distinctions on how it propagates
    /// data in <see cref="SchedulerContext" /> between instances of proxies to a 
    /// single scheduler instance - i.e. if Quartz is being used via WCF of Remoting.
	/// </remarks>
	/// <seealso cref="IScheduler.Context" />
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
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
		public SchedulerContext(IDictionary<string, object> map) : this()
		{
			PutAll(map);
		}

        /// <summary>
        /// Serialization constructor.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected SchedulerContext(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
	}
}