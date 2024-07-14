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

using Quartz.Impl;

namespace Quartz.Examples.Example11;

/// <summary>
/// This example will demonstrate how to run a large number
/// of jobs.
/// </summary>
/// <author>James House, Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class RunningLargeNumberOfJobsExample : IExample
{
    private const int NumberOfJobs = 500;

    public virtual async Task Run()
    {
        // First we must get a reference to a scheduler
        StdSchedulerFactory sf = new StdSchedulerFactory();
        IScheduler sched = await sf.GetScheduler();

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

        Random r = new Random();
        // schedule 500 jobs to run
        for (int count = 1; count <= NumberOfJobs; count++)
        {
            IJobDetail job = JobBuilder
                .Create<SimpleJob>()
                .WithIdentity("job" + count, "group_1")
                .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                .Build();

            // tell the job to delay some small amount... to simulate work...
            long timeDelay = (long) (r.NextDouble() * 2500);
            job.JobDataMap.Put(SimpleJob.DelayTime, timeDelay);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger_" + count, "group_1")
                .StartAt(DateBuilder.FutureDate(10000 + count * 100, IntervalUnit.Millisecond)) // space fire times a small bit
                .Build();

            await sched.ScheduleJob(job, trigger);
            if (count % 25 == 0)
            {
                Console.WriteLine("...scheduled " + count + " jobs");
            }
        }

        Console.WriteLine("------- Starting Scheduler ----------------");

        // start the schedule
        await sched.Start();

        Console.WriteLine("------- Started Scheduler -----------------");

        Console.WriteLine("------- Waiting five minutes... -----------");

        // wait five minutes to give our jobs a chance to run
        await Task.Delay(TimeSpan.FromMinutes(5));

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await sched.Shutdown(true);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetaData metaData = await sched.GetMetaData();
        Console.WriteLine("Executed " + metaData.NumberOfJobsExecuted + " jobs.");
    }
}