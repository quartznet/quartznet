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
using Quartz.Impl.Calendar;

namespace Quartz.Examples.Example8
{
    /// <summary> 
    /// This example will demonstrate how calendars can be used 
    /// to exclude periods of time when scheduling should not
    /// take place.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public class CalendarExample : IExample
    {
        public string Name
        {
            get { return GetType().Name; }
        }

        public virtual void Run()
        {
            ILog log = LogManager.GetLogger(typeof (CalendarExample));

            log.Info("------- Initializing ----------------------");

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = sf.GetScheduler();

            log.Info("------- Initialization Complete -----------");

            log.Info("------- Scheduling Jobs -------------------");

            // Add the holiday calendar to the schedule
            AnnualCalendar holidays = new AnnualCalendar();

            // fourth of July (July 4)
            DateTime fourthOfJuly = new DateTime(DateTime.UtcNow.Year, 7, 4);
            holidays.SetDayExcluded(fourthOfJuly, true);

            // halloween (Oct 31)
            DateTime halloween = new DateTime(DateTime.UtcNow.Year, 10, 31);
            holidays.SetDayExcluded(halloween, true);

            // christmas (Dec 25)
            DateTime christmas = new DateTime(DateTime.UtcNow.Year, 12, 25);
            holidays.SetDayExcluded(christmas, true);

            // tell the schedule about our holiday calendar
            sched.AddCalendar("holidays", holidays, false, false);

            // schedule a job to run hourly, starting on halloween
            // at 10 am

            DateTimeOffset runDate = DateBuilder.DateOf(0, 0, 10, 31, 10);

            IJobDetail job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job1", "group1")
                .Build();

            ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                                          .WithIdentity("trigger1", "group1")
                                                          .StartAt(runDate)
                                                          .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
                                                          .ModifiedByCalendar("holidays")
                                                          .Build();

            // schedule the job and print the first run date
            DateTimeOffset firstRunTime = sched.ScheduleJob(job, trigger);

            // print out the first execution date.
            // Note:  Since Halloween (Oct 31) is a holiday, then
            // we will not run until the next day! (Nov 1)
            log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.Key, firstRunTime.ToString("r"), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds));

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
                Thread.Sleep(30*1000);
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
            log.Info(string.Format("Executed {0} jobs.", metaData.NumberOfJobsExecuted));
        }
    }
}