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

namespace Quartz.Examples.Example2
{
	
	/// <summary> <p>
	/// This is just a simple job that gets fired off many times by example 1
	/// </p>
	/// 
	/// </summary>
	/// <author>  Bill Kratzer
	/// </author>
	public class SimpleJob : IJob
	{
		
		private static ILog _log = LogManager.GetLogger(typeof(SimpleJob));
		
		/// <summary> 
		/// Empty constructor for job initilization.
		/// </summary>
		public SimpleJob()
		{
		}
		
		/// <summary>
		/// Called by the <code>Scheduler</code> when a
		/// <code>Trigger</code> fires that is associated with
		/// the <code>Job</code>.
		/// </summary>
		public virtual void  Execute(JobExecutionContext context)
		{
			// This job simply prints out its job name and the
			// date and time that it is running
			string jobName = context.JobDetail.FullName;
			_log.Info("SimpleJob says: " + jobName + " executing at " + DateTime.Now.ToString("r"));
		}
	}
}