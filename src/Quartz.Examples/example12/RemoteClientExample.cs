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
//UPGRADE_TODO: The type 'org.quartz.CronTrigger' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using CronTrigger = org.quartz.CronTrigger;
//UPGRADE_TODO: The type 'org.quartz.JobDataMap' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobDataMap = org.quartz.JobDataMap;
//UPGRADE_TODO: The type 'org.quartz.JobDetail' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobDetail = org.quartz.JobDetail;
//UPGRADE_TODO: The type 'org.quartz.Scheduler' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Scheduler = org.quartz.Scheduler;
//UPGRADE_TODO: The type 'org.quartz.SchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SchedulerFactory = org.quartz.SchedulerFactory;
//UPGRADE_TODO: The type 'org.quartz.impl.StdSchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using StdSchedulerFactory = org.quartz.impl.StdSchedulerFactory;
namespace org.quartz.examples.example12
{
	
	/// <summary> This example is a client program that will remotely 
	/// talk to the scheduler to schedule a job.   In this 
	/// example, we will need to use the JDBC Job Store.  The 
	/// client will connect to the JDBC Job Store remotely to 
	/// schedule the job.
	/// 
	/// </summary>
	/// <author>  James House, Bill Kratzer
	/// </author>
	public class RemoteClientExample
	{
		
		public virtual void  run()
		{
			
			Log log = LogFactory.getLog(typeof(RemoteClientExample));
			
			// First we must get a reference to a scheduler
			SchedulerFactory sf = new StdSchedulerFactory();
			Scheduler sched = sf.getScheduler();
			
			// define the job and ask it to run
			JobDetail job = new JobDetail("remotelyAddedJob", "default", typeof(SimpleJob));
			JobDataMap map = new JobDataMap();
			map.put("msg", "Your remotely added job has executed!");
			job.setJobDataMap(map);
			CronTrigger trigger = new CronTrigger("remotelyAddedTrigger", "default", "remotelyAddedJob", "default", System.DateTime.Now, null, "/5 * * ? * *");
			
			// schedule the job
			sched.scheduleJob(job, trigger);
			
			log.info("Remote job scheduled.");
		}
		
		[STAThread]
		public static void  Main(System.String[] args)
		{
			
			RemoteClientExample example = new RemoteClientExample();
			example.run();
		}
	}
}