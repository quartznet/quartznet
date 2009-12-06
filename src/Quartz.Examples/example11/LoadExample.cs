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

namespace Quartz.Examples.Example11
{
	/// <summary> 
	/// This example will spawn a large number of jobs to run.
	/// </summary>
	/// <author>James House, Bill Kratzer</author>
	public class LoadExample : IExample
	{
		private int _numberOfJobs = 500;

		public LoadExample(int inNumberOfJobs)
		{
			_numberOfJobs = inNumberOfJobs;
		}

		public string Name
		{
			get { throw new NotImplementedException(); }
		}

		public virtual void Run()
		{
			ILog log = LogManager.GetLogger(typeof (LoadExample));

			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();

			log.Info("------- Initialization Complete -----------");

			log.Info("------- (Not Scheduling any Jobs - relying on XML definitions --");

            Random r = new Random();
			// schedule 500 jobs to run
			for (int count = 1; count <= _numberOfJobs; count++)
			{
				JobDetail job = new JobDetail("job" + count, "group1", typeof (SimpleJob));
                // tell the job to delay some small amount... to simulate work...
                long timeDelay = (long)(r.NextDouble() * 2500);
                job.JobDataMap.Put(SimpleJob.DELAY_TIME, timeDelay);
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = true;
				SimpleTrigger trigger = new SimpleTrigger("trigger_" + count, "group_1");
				trigger.StartTimeUtc = DateTime.UtcNow.AddMilliseconds(10000L).AddMilliseconds(count*100);
				sched.ScheduleJob(job, trigger);
				if (count%25 == 0)
				{
					log.Info("...scheduled " + count + " jobs");
				}
			}


			log.Info("------- Starting Scheduler ----------------");

			// start the schedule 
			sched.Start();

			log.Info("------- Started Scheduler -----------------");

			log.Info("------- Waiting five minutes... -----------");

			// wait five minutes to give our jobs a chance to run
			try
			{
				Thread.Sleep(TimeSpan.FromMinutes(5));
			}
            catch (ThreadInterruptedException)
			{
			}

			// shut down the scheduler
			log.Info("------- Shutting Down ---------------------");
			sched.Shutdown(true);
			log.Info("------- Shutdown Complete -----------------");

			SchedulerMetaData metaData = sched.GetMetaData();
			log.Info("Executed " + metaData.NumberOfJobsExecuted + " jobs.");
		}
	}
}