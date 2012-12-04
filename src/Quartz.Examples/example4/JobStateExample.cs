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

namespace Quartz.Examples.Example4
{
    /// <summary> 
    /// This example will demonstrate how job parameters can be 
    /// passed into jobs and how state can be maintained.
    /// </summary>
    /// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class JobStateExample : IExample
    {
        public string Name
        {
            get { return GetType().Name; }
        }

        public virtual void Run()
        {
            ILog log = LogManager.GetLogger(typeof (JobStateExample));

            log.Info("------- Initializing -------------------");

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = sf.GetScheduler();

            log.Info("------- Initialization Complete --------");

            log.Info("------- Scheduling Jobs ----------------");

            // get a "nice round" time a few seconds in the future....
            DateTimeOffset startTime = DateBuilder.NextGivenSecondDate(null, 10);

            // job1 will only run 5 times (at start time, plus 4 repeats), every 10 seconds
            IJobDetail job1 = JobBuilder.Create<ColorJob>()
                .WithIdentity("job1", "group1")
                .Build();

            ISimpleTrigger trigger1 = (ISimpleTrigger) TriggerBuilder.Create()
                                                           .WithIdentity("trigger1", "group1")
                                                           .StartAt(startTime)
                                                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).WithRepeatCount(4))
                                                           .Build();

            // pass initialization parameters into the job
            job1.JobDataMap.Put(ColorJob.FavoriteColor, "Green");
            job1.JobDataMap.Put(ColorJob.ExecutionCount, 1);

            // schedule the job to run
            DateTimeOffset scheduleTime1 = sched.ScheduleJob(job1, trigger1);
            log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job1.Key, scheduleTime1.ToString("r"), trigger1.RepeatCount, trigger1.RepeatInterval.TotalSeconds));

            // job2 will also run 5 times, every 10 seconds

            IJobDetail job2 = JobBuilder.Create<ColorJob>()
                .WithIdentity("job2", "group1")
                .Build();

            ISimpleTrigger trigger2 = (ISimpleTrigger) TriggerBuilder.Create()
                                                           .WithIdentity("trigger2", "group1")
                                                           .StartAt(startTime)
                                                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).WithRepeatCount(4))
                                                           .Build();

            // pass initialization parameters into the job
            // this job has a different favorite color!
            job2.JobDataMap.Put(ColorJob.FavoriteColor, "Red");
            job2.JobDataMap.Put(ColorJob.ExecutionCount, 1);

            // schedule the job to run
            DateTimeOffset scheduleTime2 = sched.ScheduleJob(job2, trigger2);
            log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job2.Key, scheduleTime2.ToString("r"), trigger2.RepeatCount, trigger2.RepeatInterval.TotalSeconds));


            log.Info("------- Starting Scheduler ----------------");

            // All of the jobs have been added to the scheduler, but none of the jobs
            // will run until the scheduler has been started
            sched.Start();

            log.Info("------- Started Scheduler -----------------");

            log.Info("------- Waiting 60 seconds... -------------");
            try
            {
                // wait five minutes to show jobs
                Thread.Sleep(300*1000);
                // executing...
            }
            catch (ThreadInterruptedException)
            {
            }

            log.Info("------- Shutting Down ---------------------");

            sched.Shutdown(true);

            log.Info("------- Shutdown Complete -----------------");

            SchedulerMetaData metaData = sched.GetMetaData();
            log.Info(string.Format("Executed {0} jobs.", metaData.NumberOfJobsExecuted));
        }
    }
}