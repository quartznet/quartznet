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
using Quartz.Impl;

namespace Quartz.Examples.Example9
{
	
	/// <summary> 
	/// Demonstrates the behavior of <see cref="IJobListener" />s.  In particular, 
	/// this example will use a job listener to trigger another job after one
	/// job succesfully executes.
	/// </summary>
	public class ListenerExample : IExample
	{
		public string Name
		{
			get { return GetType().Name; }
		}

		public virtual void Run()
		{
			ILog log = LogManager.GetLogger(typeof(ListenerExample));
			
			log.Info("------- Initializing ----------------------");
			
			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();
			
			log.Info("------- Initialization Complete -----------");
			
			log.Info("------- Scheduling Jobs -------------------");
			
			// schedule a job to run immediately
			JobDetail job = new JobDetail("job1", "group1", typeof(SimpleJob1));
			SimpleTrigger trigger = new SimpleTrigger("trigger1", "group1", DateTime.UtcNow, null, 0, 0);
			
			// Set up the listener
			IJobListener listener = new Job1Listener();
			sched.AddJobListener(listener);
			
			// make sure the listener is associated with the job
			job.AddJobListener(listener.Name);
			
			// schedule the job to run
			sched.ScheduleJob(job, trigger);
			
			// All of the jobs have been added to the scheduler, but none of the jobs
			// will run until the scheduler has been started
			log.Info("------- Starting Scheduler ----------------");
			sched.Start();
			
			// wait 30 seconds:
			// note:  nothing will run
			log.Info("------- Waiting 30 seconds... --------------");
			try
			{
				// wait 30 seconds to show jobs
				Thread.Sleep(30 * 1000);
				// executing...
			}
            catch (ThreadInterruptedException)
			{
			}
			
			
			// shut down the scheduler
			log.Info("------- Shutting Down ---------------------");
			sched.Shutdown(true);
			log.Info("------- Shutdown Complete -----------------");
			
			SchedulerMetaData metaData = sched.GetMetaData();
			log.Info(string.Format("Executed {0} jobs.", metaData.NumJobsExecuted));
		}
		
	}
}
