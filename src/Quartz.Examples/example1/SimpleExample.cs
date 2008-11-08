/* 
* Copyright 2007 the original author or authors. 
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
#if !NET_20

#endif
using Quartz.Impl;

namespace Quartz.Examples.Example1
{
	
	/// <summary> This Example will demonstrate how to start and shutdown the Quartz 
	/// scheduler and how to schedule a job to run in Quartz.
	/// 
	/// </summary>
	/// <author>  Bill Kratzer
	/// </author>
	public class SimpleExample : IExample
	{
		public string Name
		{
			get { throw new NotImplementedException(); }
		}

		public virtual void  Run()
		{
			ILog log = LogManager.GetLogger(typeof(SimpleExample));
	
			log.Info("------- Initializing ----------------------");
			
			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();
			
			log.Info("------- Initialization Complete -----------");
			
			log.Info("------- Scheduling Jobs -------------------");
			
			// computer a time that is on the next round minute
			DateTime runTime = TriggerUtils.GetEvenMinuteDate(DateTime.UtcNow);
			
			// define the job and tie it to our HelloJob class
			JobDetail job = new JobDetail("job1", "group1", typeof(HelloJob));
			
			// Trigger the job to run on the next round minute
			SimpleTrigger trigger = new SimpleTrigger("trigger1", "group1", runTime);
			
			// Tell quartz to schedule the job using our trigger
			sched.ScheduleJob(job, trigger);
			log.Info(string.Format("{0} will run at: {1}", job.FullName, runTime.ToString("r")));
			
			// Start up the scheduler (nothing can actually run until the 
			// scheduler has been started)
			sched.Start();
			log.Info("------- Started Scheduler -----------------");
			
			// wait long enough so that the scheduler as an opportunity to 
			// run the job!
			log.Info("------- Waiting 90 seconds... -------------");

			// wait 90 seconds to show jobs
			Thread.Sleep(90 * 1000);

			// shut down the scheduler
			log.Info("------- Shutting Down ---------------------");
			sched.Shutdown(true);
			log.Info("------- Shutdown Complete -----------------");
		}
		

	}
}
