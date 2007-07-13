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
using Quartz;
using Quartz.Examples;
using Quartz.Impl;

namespace Quartz.Examples.Example3
{
	
	/// <summary> This Example will demonstrate all of the basics of scheduling capabilities of
	/// Quartz using Cron Triggers.
	/// 
	/// </summary>
	/// <author>  Bill Kratzer
	/// </author>
	public class CronTriggerExample : IExample
	{
		public string Name
		{
			get { throw new NotImplementedException(); }
		}

		public virtual void  Run()
		{
			ILog log = LogManager.GetLogger(typeof(CronTriggerExample));
			
			log.Info("------- Initializing -------------------");
			
			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();
			
			log.Info("------- Initialization Complete --------");
			
			log.Info("------- Scheduling Jobs ----------------");
			
			// jobs can be scheduled before sched.start() has been called
			
			// job 1 will run every 20 seconds
			JobDetail job = new JobDetail("job1", "group1", typeof(SimpleJob));
			CronTrigger trigger = new CronTrigger("trigger1", "group1", "job1", "group1", "0/20 * * * * ?");
			sched.AddJob(job, true);
			DateTime ft = sched.ScheduleJob(trigger);

			log.Info(string.Format("{0} has been scheduled to run at: {1} and repeat based on expression: {2}", job.FullName, ft.ToString("r"), trigger.CronExpressionString));
			
			// job 2 will run every other minute (at 15 seconds past the minute)
			job = new JobDetail("job2", "group1", typeof(SimpleJob));
			trigger = new CronTrigger("trigger2", "group1", "job2", "group1", "15 0/2 * * * ?");
			sched.AddJob(job, true);
			ft = sched.ScheduleJob(trigger);
			log.Info(string.Format("{0} has been scheduled to run at: {1} and repeat based on expression: {2}", job.FullName, ft.ToString("r"), trigger.CronExpressionString));
			
			// job 3 will run every other minute but only between 8am and 5pm
			job = new JobDetail("job3", "group1", typeof(SimpleJob));
			trigger = new CronTrigger("trigger3", "group1", "job3", "group1", "0 0/2 8-17 * * ?");
			sched.AddJob(job, true);
			ft = sched.ScheduleJob(trigger);
			log.Info(string.Format("{0} has been scheduled to run at: {1} and repeat based on expression: {2}", job.FullName, ft.ToString("r"), trigger.CronExpressionString));
			
			// job 4 will run every three minutes but only between 5pm and 11pm
			job = new JobDetail("job4", "group1", typeof(SimpleJob));
			trigger = new CronTrigger("trigger4", "group1", "job4", "group1", "0 0/3 17-23 * * ?");
			sched.AddJob(job, true);
			ft = sched.ScheduleJob(trigger);
			log.Info(string.Format("{0} has been scheduled to run at: {1} and repeat based on expression: {2}", job.FullName, ft.ToString("r"), trigger.CronExpressionString));
			
			// job 5 will run at 10am on the 1st and 15th days of the month
			job = new JobDetail("job5", "group1", typeof(SimpleJob));
			trigger = new CronTrigger("trigger5", "group1", "job5", "group1", "0 0 10am 1,15 * ?");
			sched.AddJob(job, true);
			ft = sched.ScheduleJob(trigger);
			log.Info(string.Format("{0} has been scheduled to run at: {1} and repeat based on expression: {2}", job.FullName, ft.ToString("r"), trigger.CronExpressionString));
			
			// job 6 will run every 30 seconds but only on Weekdays (Monday through
			// Friday)
			job = new JobDetail("job6", "group1", typeof(SimpleJob));
			trigger = new CronTrigger("trigger6", "group1", "job6", "group1", "0,30 * * ? * MON-FRI");
			sched.AddJob(job, true);
			ft = sched.ScheduleJob(trigger);
			log.Info(string.Format("{0} has been scheduled to run at: {1} and repeat based on expression: {2}", 
                job.FullName, ft.ToString("r"), trigger.CronExpressionString));
			
			// job 7 will run every 30 seconds but only on Weekends (Saturday and
			// Sunday)
			job = new JobDetail("job7", "group1", typeof(SimpleJob));
			trigger = new CronTrigger("trigger7", "group1", "job7", "group1", "0,30 * * ? * SAT,SUN");
			sched.AddJob(job, true);
			ft = sched.ScheduleJob(trigger);
			log.Info(string.Format("{0} has been scheduled to run at: {1} and repeat based on expression: {2}", 
                job.FullName, ft.ToString("r"), trigger.CronExpressionString));
			
			log.Info("------- Starting Scheduler ----------------");
			
			// All of the jobs have been added to the scheduler, but none of the
			// jobs
			// will run until the scheduler has been started
			sched.Start();
			
			log.Info("------- Started Scheduler -----------------");
			
			log.Info("------- Waiting five minutes... ------------");
			try
			{
				// wait five minutes to show jobs
				System.Threading.Thread.Sleep(300*1000);
				// executing...
			}
			catch (Exception)
			{
			}
			
			log.Info("------- Shutting Down ---------------------");
			
			sched.Shutdown(true);
			
			log.Info("------- Shutdown Complete -----------------");
			
			SchedulerMetaData metaData = sched.GetMetaData();
			log.Info(string.Format("Executed {0} jobs.", metaData.NumJobsExecuted));
		}

	}
}
