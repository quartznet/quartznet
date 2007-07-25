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
using System.Threading;
using Common.Logging;

namespace Quartz.Examples.Example11
{
	/// <summary>
	/// This is just a simple job that gets fired off many times by example 1.
	/// </summary>
	/// <author>Bill Kratzer</author>
	public class SimpleJob : IJob
	{
		private static ILog _log = LogManager.GetLogger(typeof (SimpleJob));
		// job parameter
		public const string DELAY_TIME = "delay time";

		/// <summary> 
		/// Called by the <see cref="IScheduler" /> when a
		/// <see cref="Trigger" /> fires that is associated with
		/// the <see cref="IJob" />.
		/// </summary>
		public virtual void Execute(JobExecutionContext context)
		{
			// This job simply prints out its job name and the
			// date and time that it is running
			string jobName = context.JobDetail.FullName;

			_log.Info("Executing job: " + jobName + " executing at " + DateTime.Now.ToString("r"));

			// wait for a period of time
			long delayTime = context.JobDetail.JobDataMap.GetLong(DELAY_TIME);
			try
			{
				Thread.Sleep(new TimeSpan(10000*delayTime));
			}
			catch
			{
			}

			_log.Info("Finished Executing job: " + jobName + " at " + DateTime.Now.ToString("r"));
		}
	}
}