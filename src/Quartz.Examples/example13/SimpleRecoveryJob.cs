/* 
* Copyright 2005 OpenSymphony 
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
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Log = org.apache.commons.logging.Log;
//UPGRADE_TODO: The type 'org.apache.commons.logging.LogFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using LogFactory = org.apache.commons.logging.LogFactory;
//UPGRADE_TODO: The type 'org.quartz.Job' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Job = org.quartz.Job;
//UPGRADE_TODO: The type 'org.quartz.JobDataMap' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobDataMap = org.quartz.JobDataMap;
//UPGRADE_TODO: The type 'org.quartz.JobExecutionContext' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobExecutionContext = org.quartz.JobExecutionContext;
//UPGRADE_TODO: The type 'org.quartz.JobExecutionException' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobExecutionException = org.quartz.JobExecutionException;
namespace org.quartz.examples.example13
{
	
	/// <summary> <p>
	/// A dumb implementation of Job, for unittesting purposes.
	/// </p>
	/// 
	/// </summary>
	/// <author>  James House
	/// </author>
	public class SimpleRecoveryJob : Job
	{
		
		//UPGRADE_NOTE: The initialization of  '_log' was moved to static method 'org.quartz.examples.example13.SimpleRecoveryJob'. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1005'"
		private static Log _log;
		
		private const System.String COUNT = "count";
		
		/// <summary> Quartz requires a public empty constructor so that the
		/// scheduler can instantiate the class whenever it needs.
		/// </summary>
		public SimpleRecoveryJob()
		{
		}
		
		/// <summary> <p>
		/// Called by the <code>{@link org.quartz.Scheduler}</code> when a
		/// <code>{@link org.quartz.Trigger}</code> fires that is associated with
		/// the <code>Job</code>.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  JobExecutionException </throws>
		/// <summary>             if there is an exception while executing the job.
		/// </summary>
		public virtual void  execute(JobExecutionContext context)
		{
			
			System.String jobName = context.getJobDetail().getFullName();
			
			// if the job is recovering print a message
			if (context.isRecovering())
			{
				//UPGRADE_TODO: Method 'java.util.Date.toString' was converted to 'System.DateTime.ToString' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDatetoString'"
				_log.info("SimpleRecoveryJob: " + jobName + " RECOVERING at " + System.DateTime.Now.ToString("r"));
			}
			else
			{
				//UPGRADE_TODO: Method 'java.util.Date.toString' was converted to 'System.DateTime.ToString' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDatetoString'"
				_log.info("SimpleRecoveryJob: " + jobName + " starting at " + System.DateTime.Now.ToString("r"));
			}
			
			// delay for ten seconds
			long delay = 10L * 1000L;
			try
			{
				//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javalangThreadsleep_long'"
				System.Threading.Thread.Sleep(new System.TimeSpan((System.Int64) 10000 * delay));
			}
			catch (System.Exception e)
			{
			}
			
			JobDataMap data = context.getJobDetail().getJobDataMap();
			int count;
			if (data.containsKey(COUNT))
			{
				count = data.getInt(COUNT);
			}
			else
			{
				count = 0;
			}
			count++;
			data.put(COUNT, count);
			
			//UPGRADE_TODO: Method 'java.util.Date.toString' was converted to 'System.DateTime.ToString' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDatetoString'"
			_log.info("SimpleRecoveryJob: " + jobName + " done at " + System.DateTime.Now.ToString("r") + "\n Execution #" + count);
		}
		static SimpleRecoveryJob()
		{
			_log = LogFactory.getLog(typeof(SimpleRecoveryJob));
		}
	}
}