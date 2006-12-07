/* 
* Copyright 2007 OpenSymphony 
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
using System;
using Common.Logging;
using Quartz;

namespace org.quartz.examples.example4
{
	
	/// <summary>
	/// This is just a simple job that receives parameters and
	/// maintains state
	/// </summary>
	/// <author>Bill Kratzer</author>
	public class ColorJob : IStatefulJob
	{
		
		private static ILog _log = LogManager.GetLogger(typeof(ColorJob));
		
		// parameter names specific to this job
		public const string FAVORITE_COLOR = "favorite color";
		public const string EXECUTION_COUNT = "count";
		
		// Since Quartz will re-instantiate a class every time it
		// gets executed, members non-static member variables can
		// not be used to maintain state!
		private int _counter = 1;
		
	
		/// <summary>
		/// Called by the <code>Scheduler</code> when a
		/// <code>Trigger</code> fires that is associated with
		/// the <code>Job</code>.
		/// </summary>
		public virtual void Execute(JobExecutionContext context)
		{
			
			// This job simply prints out its job name and the
			// date and time that it is running
			string jobName = context.JobDetail.FullName;
			
			// Grab and print passed parameters
			JobDataMap data = context.JobDetail.JobDataMap;
			string favoriteColor = data.GetString(FAVORITE_COLOR);
			int count = data.GetInt(EXECUTION_COUNT);
			_log.Info("ColorJob: " + jobName + " executing at " + DateTime.Now.ToString("r") + "\n" + "  favorite color is " + favoriteColor + "\n" + "  execution count (from job map) is " + count + "\n" + "  execution count (from job member variable) is " + _counter);
			
			// increment the count and store it back into the 
			// job map so that job state can be properly maintained
			count++;
			data.Put(EXECUTION_COUNT, count);
			
			// Increment the local member variable 
			// This serves no real purpose since job state can not 
			// be maintained via member variables!
			_counter++;
		}

	}
}