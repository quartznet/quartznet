/* 
 * Copyright 2006 OpenSymphony 
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
using System.Collections.Specialized;
using System.Threading;

using Common.Logging;

using Quartz.Impl;

namespace Quartz.Examples.Example14
{
    /// <summary>
    /// This Example will demonstrate how Triggers are ordered by priority.
    /// </summary>
    public class PriorityExample : IExample
    {
        #region IExample Members

        public string Name
        {
            get { return GetType().Name; }
        }

        public void Run()
        {
            ILog log = LogManager.GetLogger(typeof (PriorityExample));

            log.Info("------- Initializing ----------------------");

            // First we must get a reference to a scheduler
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.scheduler.instanceName"] = "PriorityExampleScheduler";
            // Set thread count to 1 to force Triggers scheduled for the same time to 
            // to be ordered by priority.
            properties["quartz.threadPool.threadCount"] = "1";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz";
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();

            log.Info("------- Initialization Complete -----------");

            log.Info("------- Scheduling Jobs -------------------");

            JobDetail job = new JobDetail("TriggerEchoJob", null, typeof (TriggerEchoJob));

            // All three triggers will fire their first time at the same time, 
            // ordered by their priority, and then repeat once, firing in a 
            // staggered order that therefore ignores priority.
            //
            // We should see the following firing order:
            // 1. Priority10Trigger15SecondRepeat
            // 2. Priority5Trigger10SecondRepeat
            // 3. PriorityNeg5Trigger5SecondRepeat
            // 4. PriorityNeg5Trigger5SecondRepeat
            // 5. Priority5Trigger10SecondRepeat
            // 6. Priority10Trigger15SecondRepeat

            // Calculate the start time of all triggers as 5 seconds from now
            DateTime startTime = DateTime.UtcNow.AddSeconds(5);

            // First trigger has priority of 1, and will repeat after 5 seconds
            SimpleTrigger trigger1 =
                new SimpleTrigger("PriorityNeg5Trigger5SecondRepeat", null, startTime, null, 1, TimeSpan.FromSeconds(5));
            trigger1.Priority = 1;
            trigger1.JobName = "TriggerEchoJob";

            // Second trigger has default priority of 5, and will repeat after 10 seconds
            SimpleTrigger trigger2 =
                new SimpleTrigger("Priority5Trigger10SecondRepeat", null, startTime, null, 1, TimeSpan.FromSeconds(10));
            trigger2.JobName = "TriggerEchoJob";

            // Third trigger has priority 10, and will repeat after 15 seconds
            SimpleTrigger trigger3 =
                new SimpleTrigger("Priority10Trigger15SecondRepeat", null, startTime, null, 1, TimeSpan.FromSeconds(15));
            trigger3.Priority = 10;
            trigger3.JobName = "TriggerEchoJob";

            // Tell quartz to schedule the job using our trigger
            sched.ScheduleJob(job, trigger1);
            sched.ScheduleJob(trigger2);
            sched.ScheduleJob(trigger3);

            // Start up the scheduler (nothing can actually run until the 
            // scheduler has been started)
            sched.Start();
            log.Info("------- Started Scheduler -----------------");

            // wait long enough so that the scheduler as an opportunity to 
            // fire the triggers
            log.Info("------- Waiting 30 seconds... -------------");

            try
            {
                Thread.Sleep(30*1000);
            }
            catch (ThreadInterruptedException)
            {
            }

            // shut down the scheduler
            log.Info("------- Shutting Down ---------------------");
            sched.Shutdown(true);
            log.Info("------- Shutdown Complete -----------------");
        }

        #endregion
    }
}