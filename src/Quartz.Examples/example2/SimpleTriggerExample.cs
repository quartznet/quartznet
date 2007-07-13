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
using Nullables;
using Quartz.Impl;

namespace Quartz.Examples.Example2
{
	/// <summary> 
	/// This Example will demonstrate all of the basics of scheduling capabilities
	/// of Quartz using Simple Triggers.
	/// </summary>
	/// <author>  Bill Kratzer
	/// </author>
	public class SimpleTriggerExample : IExample
	{
		public string Name
		{
			get { return GetType().Name; }
		}

		public virtual void Run()
		{
			ILog log = LogManager.GetLogger(typeof (SimpleTriggerExample));

			log.Info("------- Initializing -------------------");

			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();

			log.Info("------- Initialization Complete --------");

			log.Info("------- Scheduling Jobs ----------------");

			// jobs can be scheduled before sched.start() has been called

			// get a "nice round" time a few seconds in the future...
			DateTime ts = TriggerUtils.GetNextGivenSecondDate(null, 15);

			// job1 will only fire once at date/time "ts"
			JobDetail job = new JobDetail("job1", "group1", typeof (SimpleJob));
			SimpleTrigger trigger = new SimpleTrigger("trigger1", "group1", ts);

			// schedule it to run!
			DateTime ft = sched.ScheduleJob(job, trigger);
			log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			// job2 will only fire once at date/time "ts"
			job = new JobDetail("job2", "group1", typeof (SimpleJob));
			trigger = new SimpleTrigger("trigger2", "group1", "job2", "group1", ts, null, 0, 0);
			ft = sched.ScheduleJob((job), trigger);
			log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			// job3 will run 11 times (run once and repeat 10 more times)
			// job3 will repeat every 10 seconds (10000 ms)
			job = new JobDetail("job3", "group1", typeof (SimpleJob));
			trigger = new SimpleTrigger("trigger3", "group1", "job3", "group1", ts, null, 10, 10000L);
			ft = sched.ScheduleJob(job, trigger);
			log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			// the same job (job3) will be scheduled by a another trigger
			// this time will only run every 70 seocnds (70000 ms)
			trigger = new SimpleTrigger("trigger3", "group2", "job3", "group1", ts, null, 2, 70000L);
			ft = sched.ScheduleJob(trigger);
			log.Info(string.Format("{0} will [also] run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			// job4 will run 6 times (run once and repeat 5 more times)
			// job4 will repeat every 10 seconds (10000 ms)
			job = new JobDetail("job4", "group1", typeof (SimpleJob));
			trigger = new SimpleTrigger("trigger4", "group1", "job4", "group1", ts, null, 5, 10000L);
			ft = sched.ScheduleJob(job, trigger);
			log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			// job5 will run once, five minutes past "ts" (300 seconds past "ts")
			job = new JobDetail("job5", "group1", typeof (SimpleJob));
			trigger = new SimpleTrigger("trigger5", "group1", "job5", "group1", ts.AddMilliseconds(300*1000), null, 0, 0);
			ft = sched.ScheduleJob(job, trigger);
			log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			// job6 will run indefinitely, every 50 seconds
			job = new JobDetail("job6", "group1", typeof (SimpleJob));
			trigger =
				new SimpleTrigger("trigger6", "group1", "job6", "group1", ts, null, SimpleTrigger.REPEAT_INDEFINITELY,
				                  50000L);
			ft = sched.ScheduleJob(job, trigger);
			log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			log.Info("------- Starting Scheduler ----------------");

			// All of the jobs have been added to the scheduler, but none of the jobs
			// will run until the scheduler has been started
			sched.Start();

			log.Info("------- Started Scheduler -----------------");

			// jobs can also be scheduled after start() has been called...
			// job7 will repeat 20 times, repeat every five minutes
			job = new JobDetail("job7", "group1", typeof (SimpleJob));
			trigger = new SimpleTrigger("trigger7", "group1", "job7", "group1", ts, null, 20, 300000);
			ft = sched.ScheduleJob(job, trigger);
			log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", 
                job.FullName, ft.ToString("r"), trigger.RepeatCount, (trigger.RepeatInterval/1000)));

			// jobs can be fired directly... (rather than waiting for a trigger)
			job = new JobDetail("job8", "group1", typeof (SimpleJob));
			job.Durability = (true);
			sched.AddJob(job, true);
			log.Info("'Manually' triggering job8...");
			sched.TriggerJob("job8", "group1");

			log.Info("------- Waiting 30 seconds... --------------");

			try
			{
				// wait 30 seconds to show jobs
				Thread.Sleep(30*1000);
				// executing...
			}
			catch (Exception)
			{
			}

			// jobs can be re-scheduled...  
			// job 7 will run immediately and repeat 10 times for every second
			log.Info("------- Rescheduling... --------------------");
			trigger = new SimpleTrigger("trigger7", "group1", "job7", "group1", DateTime.Now, null, 10, 1000L);
			NullableDateTime ft2 = sched.RescheduleJob("trigger7", "group1", trigger);
			if (ft2.HasValue)
			{
				log.Info("job7 rescheduled to run at: " + ft2.Value.ToString("r"));
			}
			else
			{
				log.Error("Reschedule failed, date was null");
			}

			log.Info("------- Waiting five minutes... ------------");
			try
			{
				// wait five minutes to show jobs
				Thread.Sleep(2*1000);
				// executing...
			}
			catch (Exception)
			{
			}

			log.Info("------- Shutting Down ---------------------");

			sched.Shutdown(true);

			log.Info("------- Shutdown Complete -----------------");

			// display some stats about the schedule that just ran
			SchedulerMetaData metaData = sched.GetMetaData();
			log.Info(string.Format("Executed {0} jobs.", metaData.NumJobsExecuted));
		}
	}
}
