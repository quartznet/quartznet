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
using Quartz.Impl.Matchers;

namespace Quartz.Examples.Example09;

/// <summary>
/// Demonstrates the behavior of <see cref="IJobListener" />s.  In particular,
/// this example will use a job listener to trigger another job after one
/// job successfully executes.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public class TriggeringAJobUsingJobListenersExample : IExample
{
    public virtual async Task Run()
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        StdSchedulerFactory sf = new StdSchedulerFactory();
        IScheduler sched = await sf.GetScheduler();

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

        // schedule a job to run immediately
        IJobDetail job = JobBuilder.Create<SimpleJob1>()
            .WithIdentity("job1")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1")
            .StartNow()
            .Build();

        // Set up the listener
        IJobListener listener = new SimpleJob1Listener();
        IMatcher<JobKey> matcher = KeyMatcher<JobKey>.KeyEquals(job.Key);
        sched.ListenerManager.AddJobListener(listener, matcher);

        // schedule the job to run
        await sched.ScheduleJob(job, trigger);

        // All of the jobs have been added to the scheduler, but none of the jobs
        // will run until the scheduler has been started
        Console.WriteLine("------- Starting Scheduler ----------------");
        await sched.Start();

        // wait 30 seconds:
        // note:  nothing will run
        Console.WriteLine("------- Waiting 30 seconds... --------------");

        // wait 30 seconds to show jobs
        await Task.Delay(TimeSpan.FromSeconds(30));
        // executing...

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await sched.Shutdown(true);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetaData metaData = await sched.GetMetaData();
        Console.WriteLine($"Executed {metaData.NumberOfJobsExecuted} jobs.");
    }
}