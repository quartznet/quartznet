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

namespace Quartz.Examples.Example01;

/// <summary>
/// This Example will demonstrate how to start and shutdown the Quartz
/// scheduler and how to schedule a job to run in Quartz.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleJobSchedulerExample : IExample
{
    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete -----------");

        // ten seconds from now, which is long enough to read the rest of this before it happens
        DateTimeOffset runTime = DateTimeOffset.UtcNow.AddSeconds(10);

        Console.WriteLine("------- Scheduling Job  -------------------");

        // define the job and tie it to our HelloJob class
        IJobDetail job = JobBuilder.Create<HelloJob>()
            .WithIdentity("job1", "group1")
            .Build();

        // Trigger the job to run once, at that time
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(runTime)
            .Build();

        // Tell quartz to schedule the job using our trigger
        await scheduler.ScheduleJob(job, trigger, cancellationToken);
        Console.WriteLine($"{job.Key} will run at: {runTime.LocalDateTime:HH:mm:ss}");

        // Start up the scheduler (nothing can actually run until the
        // scheduler has been started)
        await scheduler.Start(cancellationToken);
        Console.WriteLine("------- Started Scheduler -----------------");

        await Watching.For(TimeSpan.FromSeconds(20), "job1 greeting the world, once, ten seconds in", cancellationToken);

        Console.WriteLine("------- Shutting Down ---------------------");

        // CancellationToken.None, not the token: a Ctrl+C ends the watching, and the scheduler still
        // gets to stop in an orderly way afterwards. Handing it a cancelled token would abandon that.
        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);

        Console.WriteLine("------- Shutdown Complete -----------------");
    }
}
