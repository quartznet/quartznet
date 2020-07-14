#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
using System.Collections.Specialized;
using System.Threading.Tasks;

using Quartz.Impl;

namespace Quartz.Examples.Example14
{
    /// <summary>
    /// This example will demonstrate how Triggers are ordered by priority.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public class RunJobsByPriorityWithTriggersPriorityExample : IExample
    {
        public async Task Run()
        {
            Console.WriteLine("------- Initializing ----------------------");

            // First we must get a reference to a scheduler
            NameValueCollection properties = new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = "PriorityExampleScheduler",
                // Set thread count to 1 to force Triggers scheduled for the same time to
                // to be ordered by priority.
                ["quartz.threadPool.threadCount"] = "1",
                ["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz",
                ["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz"
            };
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            Console.WriteLine("------- Initialization Complete -----------");

            Console.WriteLine("------- Scheduling Jobs -------------------");

            IJobDetail job = JobBuilder.Create<TriggerEchoJob>()
                .WithIdentity("TriggerEchoJob")
                .Build();

            // All three triggers will fire their first time at the same time,
            // ordered by their priority, and then repeat once, firing in a
            // staggered order that therefore ignores priority.
            //
            // We should see the following firing order:
            // 1. Priority10Trigger15SecondRepeat
            // 2. Priority5Trigger10SecondRepeat
            // 3. Priority1Trigger5SecondRepeat
            // 4. Priority1Trigger5SecondRepeat
            // 5. Priority5Trigger10SecondRepeat
            // 6. Priority10Trigger15SecondRepeat

            // Calculate the start time of all triggers as 5 seconds from now
            DateTimeOffset startTime = DateBuilder.FutureDate(5, IntervalUnit.Second);

            // First trigger has priority of 1, and will repeat after 5 seconds
            ITrigger trigger1 = TriggerBuilder.Create()
                .WithIdentity("Priority1Trigger5SecondRepeat")
                .StartAt(startTime)
                .WithSimpleSchedule(x => x.WithRepeatCount(1).WithIntervalInSeconds(5))
                .WithPriority(1)
                .ForJob(job)
                .Build();

            // Second trigger has default priority of 5 (default), and will repeat after 10 seconds
            ITrigger trigger2 = TriggerBuilder.Create()
                .WithIdentity("Priority5Trigger10SecondRepeat")
                .StartAt(startTime)
                .WithSimpleSchedule(x => x.WithRepeatCount(1).WithIntervalInSeconds(10))
                .ForJob(job)
                .Build();

            // Third trigger has priority 10, and will repeat after 15 seconds
            ITrigger trigger3 = TriggerBuilder.Create()
                .WithIdentity("Priority10Trigger15SecondRepeat")
                .StartAt(startTime)
                .WithSimpleSchedule(x => x.WithRepeatCount(1).WithIntervalInSeconds(15))
                .WithPriority(10)
                .ForJob(job)
                .Build();

            // Tell quartz to schedule the job using our trigger
            await sched.ScheduleJob(job, trigger1);
            await sched.ScheduleJob(trigger2);
            await sched.ScheduleJob(trigger3);

            // Start up the scheduler (nothing can actually run until the
            // scheduler has been started)
            await sched.Start();
            Console.WriteLine("------- Started Scheduler -----------------");

            // wait long enough so that the scheduler as an opportunity to
            // fire the triggers
            Console.WriteLine("------- Waiting 30 seconds... -------------");

            await Task.Delay(TimeSpan.FromSeconds(30));

            // shut down the scheduler
            Console.WriteLine("------- Shutting Down ---------------------");
            await sched.Shutdown(true);
            Console.WriteLine("------- Shutdown Complete -----------------");
        }
    }
}