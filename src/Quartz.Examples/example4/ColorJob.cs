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
using Common.Logging;

namespace Quartz.Examples.Example4
{
	
	/// <summary>
	/// This is just a simple job that receives parameters and
	/// maintains state.
	/// </summary>
	/// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    [PersistJobDataAfterExecution]
    [DisallowConcurrentExecution]
    public class ColorJob : IJob
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(ColorJob));
		
		// parameter names specific to this job
		public const string FavoriteColor = "favorite color";
		public const string ExecutionCount = "count";
		
		// Since Quartz will re-instantiate a class every time it
		// gets executed, members non-static member variables can
		// not be used to maintain state!
		private int counter = 1;
		
		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a
		/// <see cref="ITrigger" /> fires that is associated with
		/// the <see cref="IJob" />.
		/// </summary>
		public virtual void Execute(IJobExecutionContext context)
		{
			
			// This job simply prints out its job name and the
			// date and time that it is running
			JobKey jobKey = context.JobDetail.Key;
			
			// Grab and print passed parameters
			JobDataMap data = context.JobDetail.JobDataMap;
			string favoriteColor = data.GetString(FavoriteColor);
			int count = data.GetInt(ExecutionCount);
			log.InfoFormat(
                "ColorJob: {0} executing at {1}\n  favorite color is {2}\n  execution count (from job map) is {3}\n  execution count (from job member variable) is {4}", 
                jobKey, 
                DateTime.Now.ToString("r"), 
                favoriteColor, 
                count, counter);
			
			// increment the count and store it back into the 
			// job map so that job state can be properly maintained
			count++;
			data.Put(ExecutionCount, count);
			
			// Increment the local member variable 
			// This serves no real purpose since job state can not 
			// be maintained via member variables!
			counter++;
		}

	}
}
