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
//UPGRADE_TODO: The type 'org.quartz.JobExecutionContext' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobExecutionContext = org.quartz.JobExecutionContext;
//UPGRADE_TODO: The type 'org.quartz.JobExecutionException' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobExecutionException = org.quartz.JobExecutionException;
namespace org.quartz.examples.example11
{
	
	/// <summary> <p>
	/// This is just a simple job that gets fired off many times by example 1
	/// </p>
	/// 
	/// </summary>
	/// <author>  Bill Kratzer
	/// </author>
	public class SimpleJob : Job
	{
		
		//UPGRADE_NOTE: The initialization of  '_log' was moved to static method 'org.quartz.examples.example11.SimpleJob'. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1005'"
		private static Log _log;
		
		// job parameter
		public const System.String DELAY_TIME = "delay time";
		
		/// <summary> Empty constructor for job initilization</summary>
		public SimpleJob()
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
			
			// This job simply prints out its job name and the
			// date and time that it is running
			System.String jobName = context.getJobDetail().getFullName();
			//UPGRADE_TODO: Method 'java.util.Date.toString' was converted to 'System.DateTime.ToString' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDatetoString'"
			_log.info("Executing job: " + jobName + " executing at " + System.DateTime.Now.ToString("r"));
			
			// wait for a period of time
			long delayTime = context.getJobDetail().getJobDataMap().getLong(DELAY_TIME);
			try
			{
				//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javalangThreadsleep_long'"
				System.Threading.Thread.Sleep(new System.TimeSpan((System.Int64) 10000 * delayTime));
			}
			catch (System.Exception e)
			{
			}
			
			//UPGRADE_TODO: Method 'java.util.Date.toString' was converted to 'System.DateTime.ToString' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDatetoString'"
			_log.info("Finished Executing job: " + jobName + " at " + System.DateTime.Now.ToString("r"));
		}
		static SimpleJob()
		{
			_log = LogFactory.getLog(typeof(SimpleJob));
		}
	}
}