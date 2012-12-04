#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
#endregion

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
    /// <author>Marko Lahma (.NET)</author>
    public class LoadExample : IExample
	{
		private readonly int numberOfJobs = 500;

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

            log.Info("------- Scheduling Jobs -------------------");

            Random r = new Random();
			// schedule 500 jobs to run
			for (int count = 1; count <= numberOfJobs; count++)
			{
				IJobDetail job = JobBuilder
                    .Create<SimpleJob>()
                    .WithIdentity("job" + count, "group_1")
                    .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                    .Build();

                // tell the job to delay some small amount... to simulate work...
                long timeDelay = (long)(r.NextDouble() * 2500);
                job.JobDataMap.Put(SimpleJob.DelayTime, timeDelay);

			    ITrigger trigger = TriggerBuilder.Create()
			        .WithIdentity("trigger_" + count, "group_1")
			        .StartAt(DateBuilder.FutureDate((10000 + (count*100)), IntervalUnit.Millisecond)) // space fire times a small bit
			        .Build();

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