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
using Quartz;

namespace Quartz.Examples.Example5
{
	
	/// <summary>
	/// A dumb implementation of Job, for unittesting purposes.
	/// </summary>
	/// <author>James House</author>
	public class StatefulDumbJob : IStatefulJob
	{
		
		public const string NUM_EXECUTIONS = "NumExecutions";
		public const string EXECUTION_DELAY = "ExecutionDelay";
		
		
		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="Trigger" />
		/// fires that is associated with the <see cref="IJob" />.
		/// </summary>
		public virtual void  Execute(JobExecutionContext context)
		{
			Console.Error.WriteLine("---{0} executing.[{1}]", context.JobDetail.FullName, DateTime.Now.ToString("r"));
			
			JobDataMap map = context.JobDetail.JobDataMap;
			
			int executeCount = 0;
			if (map.Contains(NUM_EXECUTIONS))
				executeCount = map.GetInt(NUM_EXECUTIONS);
			
			executeCount++;
			
			map.Put(NUM_EXECUTIONS, executeCount);
			
			int delay = 5;
			if (map.Contains(EXECUTION_DELAY))
				delay = map.GetInt(EXECUTION_DELAY);
			
			try
			{
				Thread.Sleep(1000 * delay);
			}
            catch (ThreadInterruptedException)
			{
			}
			
			Console.Error.WriteLine("  -{0} complete ({1}).", context.JobDetail.FullName, executeCount);
		}
	}
}
