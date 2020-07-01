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
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Logging;

namespace Quartz.Examples.Example16
{
    /// <summary>
    /// This example will show hot to run asynchronous jobs.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class RunningAsynchronousJobsExample : IExample
    {
        public virtual async Task Run()
        {
            ILog log = LogProvider.GetLogger(typeof(RunningAsynchronousJobsExample));

            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = await sf.GetScheduler();

            log.Info("------- Initialization Complete -----------");

            log.Info("------- Scheduling Jobs -------------------");

            IJobDetail job = JobBuilder
                .Create<AsyncJob>()
                .WithIdentity("asyncJob")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("triggerForAsyncJob")
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(1))
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(20).RepeatForever())
                .Build();

            await sched.ScheduleJob(job, trigger);

            log.Info("------- Starting Scheduler ----------------");

            // start the schedule
            await sched.Start();

            log.Info("------- Started Scheduler -----------------");

            await Task.Delay(TimeSpan.FromSeconds(5));
            log.Info("------- Cancelling job via scheduler.Interrupt() -----------------");
            await sched.Interrupt(job.Key);

            log.Info("------- Waiting five minutes... -----------");

            // wait five minutes to give our job a chance to run
            await Task.Delay(TimeSpan.FromMinutes(5));

            // shut down the scheduler
            log.Info("------- Shutting Down ---------------------");
            await sched.Shutdown(true);
            log.Info("------- Shutdown Complete -----------------");
        }
    }
}