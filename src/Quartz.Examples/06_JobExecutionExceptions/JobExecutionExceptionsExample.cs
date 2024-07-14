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

namespace Quartz.Examples.Example06;

/// <summary>
/// This example demonstrates how Quartz can handle <see cref="JobExecutionException"/> that are
/// thrown by jobs.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class JobExecutionExceptionsExample : IExample
{
    public virtual async Task Run()
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        StdSchedulerFactory sf = new StdSchedulerFactory();
        IScheduler sched = await sf.GetScheduler();

        Console.WriteLine("------- Initialization Complete ------------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

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

        DateTimeOffset ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " will run at: " + ft + " and repeat: "
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
        ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine($"{job.Key} will run at: {ft:r} and repeat: {trigger.RepeatCount} times, every {trigger.RepeatInterval.TotalSeconds} seconds");

        Console.WriteLine("------- Starting Scheduler ----------------");

        // jobs don't start firing until start() has been called...
        await sched.Start();

        Console.WriteLine("------- Started Scheduler -----------------");

        // sleep for 30 seconds
        await Task.Delay(TimeSpan.FromSeconds(30));

        Console.WriteLine("------- Shutting Down ---------------------");

        await sched.Shutdown(false);

        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetaData metaData = await sched.GetMetaData();
        Console.WriteLine($"Executed {metaData.NumberOfJobsExecuted} jobs.");
    }
}