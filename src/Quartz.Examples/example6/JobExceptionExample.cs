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
using log4net;
using Quartz.Impl;

namespace Quartz.Examples.Example6
{
	
	/// <summary> 
	/// This job demonstrates how Quartz can handle JobExecutionExceptions that are
	/// thrown by jobs.
	/// 
	/// </summary>
	/// <author>  Bill Kratzer
	/// </author>
	public class JobExceptionExample : IExample
	{
		
		public virtual void  Run()
		{
			ILog log = LogManager.GetLogger(typeof(JobExceptionExample));
			
			log.Info("------- Initializing ----------------------");
			
			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();
			
			log.Info("------- Initialization Complete ------------");
			
			log.Info("------- Scheduling Jobs -------------------");
			
			// jobs can be scheduled before start() has been called
			
			// get a "nice round" time a few seconds in the future...
			DateTime ts = TriggerUtils.GetNextGivenSecondDate(null, 15);
			
			// badJob1 will run every three seconds
			// this job will throw an exception and refire
			// immediately
			JobDetail job = new JobDetail("badJob1", "group1", typeof(BadJob1));
			SimpleTrigger trigger = new SimpleTrigger("trigger1", "group1", ts, null, SimpleTrigger.REPEAT_INDEFINITELY, 3000L);
			System.DateTime ft = sched.ScheduleJob(job, trigger);

			log.Info(job.FullName + " will run at: " + ft.ToString("r") + " and repeat: " + trigger.RepeatCount + " times, every " + (trigger.RepeatInterval / 1000) + " seconds");
			
			// badJob2 will run every three seconds
			// this job will throw an exception and never
			// refire
			job = new JobDetail("badJob2", "group1", typeof(BadJob2));
			trigger = new SimpleTrigger("trigger2", "group1", ts, null, SimpleTrigger.REPEAT_INDEFINITELY, 3000L);
			ft = sched.ScheduleJob(job, trigger);
			log.Info(job.FullName + " will run at: " + ft.ToString("r") + " and repeat: " + trigger.RepeatCount + " times, every " + (trigger.RepeatInterval / 1000) + " seconds");
			
			log.Info("------- Starting Scheduler ----------------");
			
			// jobs don't start firing until start() has been called...
			sched.Start();
			
			log.Info("------- Started Scheduler -----------------");
			
			try
			{
				// sleep for 60 seconds
				System.Threading.Thread.Sleep(60 * 1000);
			}
			catch (System.Exception)
			{
			}
			
			log.Info("------- Shutting Down ---------------------");
			
			sched.Shutdown(true);
			
			log.Info("------- Shutdown Complete -----------------");
			
			SchedulerMetaData metaData = sched.GetMetaData();
			log.Info("Executed " + metaData.NumJobsExecuted + " jobs.");
		}

		public string Name
		{
			get
			{
				return GetType().Name;
			}
		}
	}
}