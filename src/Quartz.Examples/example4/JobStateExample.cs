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
using log4net;
using Quartz;
using Quartz.Examples;
using Quartz.Impl;

namespace org.quartz.examples.example4
{
	
	/// <summary> 
	/// This Example will demonstrate how job parameters can be 
	/// passed into jobs and how state can be maintained
	/// </summary>
	/// <author>Bill Kratzer</author>
	public class JobStateExample : IExample
	{
		public string Name
		{
			get { return GetType().Name; }
		}

		public virtual void  Run()
		{
			ILog log = LogManager.GetLogger(typeof(JobStateExample));
			
			log.Info("------- Initializing -------------------");
			
			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();
			
			log.Info("------- Initialization Complete --------");
			
			log.Info("------- Scheduling Jobs ----------------");
			
			// get a "nice round" time a few seconds in the future....
			DateTime ts = TriggerUtils.GetNextGivenSecondDate(null, 10);
			
			// job1 will only run 5 times, every 10 seconds
			JobDetail job1 = new JobDetail("job1", "group1", typeof(ColorJob));
			SimpleTrigger trigger1 = new SimpleTrigger("trigger1", "group1", "job1", "group1", ts, null, 4, 10000);
			// pass initialization parameters into the job
			job1.JobDataMap.Put(ColorJob.FAVORITE_COLOR, "Green");
			job1.JobDataMap.Put(ColorJob.EXECUTION_COUNT, 1);
			
			// schedule the job to run
			DateTime scheduleTime1 = sched.ScheduleJob(job1, trigger1);
			log.Info(job1.FullName + " will run at: " + scheduleTime1.ToString("r") + " and repeat: " + trigger1.RepeatCount + " times, every " + (trigger1.RepeatInterval / 1000) + " seconds");
			
			// job2 will also run 5 times, every 10 seconds
			JobDetail job2 = new JobDetail("job2", "group1", typeof(ColorJob));
			SimpleTrigger trigger2 = new SimpleTrigger("trigger2", "group1", "job2", "group1", ts.AddSeconds(1), null, 4, 10000);
			// pass initialization parameters into the job
			// this job has a different favorite color!
			job2.JobDataMap.Put(ColorJob.FAVORITE_COLOR, "Red");
			job2.JobDataMap.Put(ColorJob.EXECUTION_COUNT, 1);
			
			// schedule the job to run
			DateTime scheduleTime2 = sched.ScheduleJob(job2, trigger2);
			log.Info(job1.FullName + " will run at: " + scheduleTime1.ToString("r") + " and repeat: " + trigger1.RepeatCount + " times, every " + (trigger1.RepeatInterval / 1000) + " seconds");
			
			
			
			log.Info("------- Starting Scheduler ----------------");
			
			// All of the jobs have been added to the scheduler, but none of the jobs
			// will run until the scheduler has been started
			sched.Start();
			
			log.Info("------- Started Scheduler -----------------");
			
			log.Info("------- Waiting 60 seconds... -------------");
			try
			{
				// wait five minutes to show jobs
				Thread.Sleep(300 * 1000);
				// executing...
			}
			catch (Exception)
			{
			}
			
			log.Info("------- Shutting Down ---------------------");
			
			sched.Shutdown(true);
			
			log.Info("------- Shutdown Complete -----------------");
			
			SchedulerMetaData metaData = sched.GetMetaData();
			log.Info("Executed " + metaData.NumJobsExecuted + " jobs.");
		}
		
	}
}