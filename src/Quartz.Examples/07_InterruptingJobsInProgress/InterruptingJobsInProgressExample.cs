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

namespace Quartz.Examples.Example07;

/// <summary>
/// This example will demonstrate how to interrupt
/// jobs after they have been scheduled/started.
/// </summary>
/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
/// <author>Marko Lahma (.NET)</author>
public class InterruptingJobsInProgressExample : IExample
{
    public virtual async Task Run()
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create();

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

        // get a "nice round" time a few seconds in the future...

        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddSeconds(15);

        IJobDetail job = JobBuilder.Create<InterruptableJob>()
            .WithIdentity("interruptableJob1", "group1")
            .Build();

        ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
            .Build();

        DateTimeOffset ft = await scheduler.ScheduleJob(job, trigger);
        Console.WriteLine($"{job.Key} will run at: {ft:r} and repeat: {trigger.RepeatCount} times, every {trigger.RepeatInterval.TotalSeconds} seconds");

        // start up the scheduler (jobs do not start to fire until
        // the scheduler has been started)
        await scheduler.Start();
        Console.WriteLine("------- Started Scheduler -----------------");

        Console.WriteLine("------- Starting loop to interrupt job every 7 seconds ----------");
        for (int i = 0; i < 50; i++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(7));
                // tell the scheduler to interrupt our job
                await scheduler.Interrupt(job.Key);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        Console.WriteLine("------- Shutting Down ---------------------");

        await scheduler.Shutdown(true);

        Console.WriteLine("------- Shutdown Complete -----------------");
        SchedulerMetadata metadata = await scheduler.GetMetadata();
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}