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

namespace Quartz.Examples.Example2
{
    /// <summary> 
    /// This example will demonstrate all of the basics of scheduling capabilities
    /// of Quartz using Simple Triggers.
    /// </summary>
    /// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
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
            DateTimeOffset startTime = DateBuilder.NextGivenSecondDate(null, 15);

            // job1 will only fire once at date/time "ts"
            IJobDetail job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job1", "group1")
                .Build();

            ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                                          .WithIdentity("trigger1", "group1")
                                                          .StartAt(startTime)
                                                          .Build();

            // schedule it to run!
            DateTimeOffset? ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key +
                     " will run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            // job2 will only fire once at date/time "ts"
            job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job2", "group1")
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger2", "group1")
                                           .StartAt(startTime)
                                           .Build();

            ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key +
                     " will run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            // job3 will run 11 times (run once and repeat 10 more times)
            // job3 will repeat every 10 seconds
            job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job3", "group1")
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger3", "group1")
                                           .StartAt(startTime)
                                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).WithRepeatCount(10))
                                           .Build();

            ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key +
                     " will run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            // the same job (job3) will be scheduled by a another trigger
            // this time will only repeat twice at a 70 second interval

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger3", "group2")
                                           .StartAt(startTime)
                                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).WithRepeatCount(2))
                                           .ForJob(job)
                                           .Build();

            ft = sched.ScheduleJob(trigger);
            log.Info(job.Key +
                     " will [also] run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            // job4 will run 6 times (run once and repeat 5 more times)
            // job4 will repeat every 10 seconds
            job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job4", "group1")
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger4", "group1")
                                           .StartAt(startTime)
                                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).WithRepeatCount(5))
                                           .Build();

            ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key +
                     " will run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            // job5 will run once, five minutes in the future
            job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job5", "group1")
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger5", "group1")
                                           .StartAt(DateBuilder.FutureDate(5, IntervalUnit.Minute))
                                           .Build();

            ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key +
                     " will run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            // job6 will run indefinitely, every 40 seconds
            job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job6", "group1")
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger6", "group1")
                                           .StartAt(startTime)
                                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(40).RepeatForever())
                                           .Build();

            ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key +
                     " will run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            log.Info("------- Starting Scheduler ----------------");

            // All of the jobs have been added to the scheduler, but none of the jobs
            // will run until the scheduler has been started
            sched.Start();

            log.Info("------- Started Scheduler -----------------");

            // jobs can also be scheduled after start() has been called...
            // job7 will repeat 20 times, repeat every five minutes
            job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job7", "group1")
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger7", "group1")
                                           .StartAt(startTime)
                                           .WithSimpleSchedule(x => x.WithIntervalInMinutes(5).WithRepeatCount(20))
                                           .Build();

            ft = sched.ScheduleJob(job, trigger);
            log.Info(job.Key +
                     " will run at: " + ft +
                     " and repeat: " + trigger.RepeatCount +
                     " times, every " + trigger.RepeatInterval.TotalSeconds + " seconds");

            // jobs can be fired directly... (rather than waiting for a trigger)
            job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job8", "group1")
                .StoreDurably()
                .Build();

            sched.AddJob(job, true);

            log.Info("'Manually' triggering job8...");
            sched.TriggerJob(new JobKey("job8", "group1"));

            log.Info("------- Waiting 30 seconds... --------------");

            try
            {
                // wait 30 seconds to show jobs
                Thread.Sleep(TimeSpan.FromSeconds(30));
                // executing...
            }
            catch (ThreadInterruptedException)
            {
            }

            // jobs can be re-scheduled...  
            // job 7 will run immediately and repeat 10 times for every second
            log.Info("------- Rescheduling... --------------------");
            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                           .WithIdentity("trigger7", "group1")
                                           .StartAt(startTime)
                                           .WithSimpleSchedule(x => x.WithIntervalInMinutes(5).WithRepeatCount(20))
                                           .Build();

            ft = sched.RescheduleJob(trigger.Key, trigger);
            log.Info("job7 rescheduled to run at: " + ft);

            log.Info("------- Waiting five minutes... ------------");
            try
            {
                // wait five minutes to show jobs
                Thread.Sleep(TimeSpan.FromMinutes(5));
                // executing...
            }
            catch (ThreadInterruptedException)
            {
            }

            log.Info("------- Shutting Down ---------------------");

            sched.Shutdown(true);

            log.Info("------- Shutdown Complete -----------------");

            // display some stats about the schedule that just ran
            SchedulerMetaData metaData = sched.GetMetaData();
            log.Info(string.Format("Executed {0} jobs.", metaData.NumberOfJobsExecuted));
        }
    }
}