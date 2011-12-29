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

namespace Quartz.Examples.Example6
{
    /// <summary> 
    /// This job demonstrates how Quartz can handle JobExecutionExceptions that are
    /// thrown by jobs.
    /// </summary>
    /// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class JobExceptionExample : IExample
    {
        public virtual void Run()
        {
            ILog log = LogManager.GetLogger(typeof (JobExceptionExample));

            log.Info("------- Initializing ----------------------");

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = sf.GetScheduler();

            log.Info("------- Initialization Complete ------------");

            log.Info("------- Scheduling Jobs -------------------");

            // jobs can be scheduled before start() has been called

            // get a "nice round" time a few seconds in the future...
            DateTimeOffset startTime = DateBuilder.NextGivenSecondDate(null, 15);

            // badJob1 will run every 10 seconds
            // this job will throw an exception and refire
            // immediately
            IJobDetail job = JobBuilder.Create<BadJob1>()
                .WithIdentity("badJob1", "group1")
                .UsingJobData("denominator", "0")
                .Build();

            ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                                          .WithIdentity("trigger1", "group1")
                                                          .StartAt(startTime)
                                                          .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).RepeatForever())
                                                          .Build();

            DateTimeOffset ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key + " will run at: " + ft + " and repeat: "
                     + trigger.RepeatCount + " times, every "
                     + trigger.RepeatInterval.TotalSeconds + " seconds");

            // badJob2 will run every five seconds
            // this job will throw an exception and never
            // refire
            job = JobBuilder.Create<BadJob2>()
                .WithIdentity("badJob2", "group1")
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger2", "group1")
                                           .StartAt(startTime)
                                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(5).RepeatForever())
                                           .Build();
            ft = sched.ScheduleJob(job, trigger);
            log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.Key, ft.ToString("r"), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds));

            log.Info("------- Starting Scheduler ----------------");

            // jobs don't start firing until start() has been called...
            sched.Start();

            log.Info("------- Started Scheduler -----------------");

            // sleep for 30 seconds
            try
            {
                Thread.Sleep(TimeSpan.FromSeconds(30));
            }
            catch (ThreadInterruptedException)
            {
            }

            log.Info("------- Shutting Down ---------------------");

            sched.Shutdown(false);

            log.Info("------- Shutdown Complete -----------------");

            SchedulerMetaData metaData = sched.GetMetaData();
            log.Info(string.Format("Executed {0} jobs.", metaData.NumberOfJobsExecuted));
        }

        public string Name
        {
            get { return GetType().Name; }
        }
    }
}