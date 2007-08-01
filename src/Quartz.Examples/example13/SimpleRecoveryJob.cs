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

namespace Quartz.Examples.Example13
{
	
	/// <summary>
	/// A dumb implementation of Job, for unittesting purposes.
	/// </summary>
	/// <author>James House</author>
	public class SimpleRecoveryJob : IJob
	{
		private static ILog _log = LogManager.GetLogger(typeof(SimpleRecoveryJob));
		private const string COUNT = "count";
		
		/// <summary> 
		/// Called by the <see cref="IScheduler" /> when a
		/// <see cref="Trigger" /> fires that is associated with
		/// the <see cref="IJob" />.
		/// </summary>
		public virtual void Execute(JobExecutionContext context)
		{
			
			string jobName = context.JobDetail.FullName;
			
			// if the job is recovering print a message
			if (context.Recovering)
			{
				_log.Info("SimpleRecoveryJob: " + jobName + " RECOVERING at " + DateTime.Now.ToString("r"));
			}
			else
			{
				_log.Info("SimpleRecoveryJob: " + jobName + " starting at " + DateTime.Now.ToString("r"));
			}
			
			// delay for ten seconds
			int delay = 10 * 1000;
			try
			{
				Thread.Sleep(delay);
			}
            catch (ThreadInterruptedException)
			{
			}
			
			JobDataMap data = context.JobDetail.JobDataMap;
			int count;
			if (data.Contains(COUNT))
			{
				count = data.GetInt(COUNT);
			}
			else
			{
				count = 0;
			}
			count++;
			data.Put(COUNT, count);
			
			_log.Info("SimpleRecoveryJob: " + jobName + " done at " + DateTime.Now.ToString("r") + "\n Execution #" + count);
		}

	}
}