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
//UPGRADE_TODO: The type 'org.quartz.JobDetail' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobDetail = org.quartz.JobDetail;
//UPGRADE_TODO: The type 'org.quartz.Scheduler' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Scheduler = org.quartz.Scheduler;
//UPGRADE_TODO: The type 'org.quartz.SchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SchedulerFactory = org.quartz.SchedulerFactory;
//UPGRADE_TODO: The type 'org.quartz.SchedulerMetaData' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SchedulerMetaData = org.quartz.SchedulerMetaData;
//UPGRADE_TODO: The type 'org.quartz.SimpleTrigger' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SimpleTrigger = org.quartz.SimpleTrigger;
//UPGRADE_TODO: The type 'org.quartz.impl.StdSchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using StdSchedulerFactory = org.quartz.impl.StdSchedulerFactory;
//UPGRADE_TODO: The type 'org.apache.commons.logging.LogFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using LogFactory = org.apache.commons.logging.LogFactory;
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Log = org.apache.commons.logging.Log;
namespace org.quartz.examples.example11
{
	
	/// <summary> This example will spawn a large number of jobs to run
	/// 
	/// </summary>
	/// <author>  James House, Bill Kratzer
	/// </author>
	public class LoadExample
	{
		
		private int _numberOfJobs = 500;
		
		public LoadExample(int inNumberOfJobs)
		{
			_numberOfJobs = inNumberOfJobs;
		}
		
		public virtual void  run()
		{
			Log log = LogFactory.getLog(typeof(LoadExample));
			
			// First we must get a reference to a scheduler
			SchedulerFactory sf = new StdSchedulerFactory();
			Scheduler sched = sf.getScheduler();
			
			log.info("------- Initialization Complete -----------");
			
			log.info("------- (Not Scheduling any Jobs - relying on XML definitions --");
			
			System.String schedId = sched.getSchedulerInstanceId();
			
			// schedule 500 jobs to run
			for (int count = 1; count <= _numberOfJobs; count++)
			{
				JobDetail job = new JobDetail("job" + count, "group1", typeof(SimpleJob));
				// tell the job to wait one minute (60 seconds)
				job.getJobDataMap().put(SimpleJob.DELAY_TIME, 60000L);
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.setRequestsRecovery(true);
				SimpleTrigger trigger = new SimpleTrigger("trigger_" + count, "group_1");
				//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'System.DateTime.DateTime' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDateDate_long'"
				trigger.setStartTime(new System.DateTime((System.DateTime.Now.Ticks - 621355968000000000) / 10000 + 10000L + (count * 100)));
				sched.scheduleJob(job, trigger);
				if (count % 25 == 0)
				{
					log.info("...scheduled " + count + " jobs");
				}
			}
			
			
			log.info("------- Starting Scheduler ----------------");
			
			// start the schedule 
			sched.start();
			
			log.info("------- Started Scheduler -----------------");
			
			log.info("------- Waiting five minutes... -----------");
			
			// wait five minutes to give our jobs a chance to run
			try
			{
				//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javalangThreadsleep_long'"
				System.Threading.Thread.Sleep(new System.TimeSpan((System.Int64) 10000 * 300L * 1000L));
			}
			catch (System.Exception e)
			{
			}
			
			// shut down the scheduler
			log.info("------- Shutting Down ---------------------");
			sched.shutdown(true);
			log.info("------- Shutdown Complete -----------------");
			
			SchedulerMetaData metaData = sched.getMetaData();
			log.info("Executed " + metaData.numJobsExecuted() + " jobs.");
		}
		
		[STAThread]
		public static void  Main(System.String[] args)
		{
			
			int numberOfJobs = 500;
			if (args.Length == 1)
			{
				numberOfJobs = System.Int32.Parse(args[0]);
			}
			if (args.Length > 1)
			{
				System.Console.Out.WriteLine("Usage: java " + typeof(LoadExample).FullName + "[# of jobs]");
				return ;
			}
			LoadExample example = new LoadExample(numberOfJobs);
			example.run();
		}
	}
}