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

namespace Quartz.Examples.Example07;

/// <summary>
/// This example will demonstrate how to interrupt
/// jobs after they have been scheduled/started.
/// </summary>
/// <remarks>
/// The job would take twenty-four seconds if left alone. It is interrupted after seven, over and over,
/// so what there is to watch is a job starting, working across several thread pool threads, and being
/// cut short every time.
/// </remarks>
/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
/// <author>Marko Lahma (.NET)</author>
public class InterruptingJobsInProgressExample : IExample
{
    private const int Interruptions = 6;

    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

        // a few seconds in the future
        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddSeconds(5);

        IJobDetail job = JobBuilder.Create<InterruptableJob>()
            .WithIdentity("interruptableJob1", "group1")
            .Build();

        ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
            .Build();

        DateTimeOffset firstFireTime = await scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken);
        Console.WriteLine($"{job.Key} will run at {firstFireTime.LocalDateTime:HH:mm:ss}, every {trigger.RepeatInterval.TotalSeconds:0} seconds, and takes {InterruptableJob.WorkSteps * 3} seconds each time");

        // start up the scheduler (jobs do not start to fire until
        // the scheduler has been started)
        await scheduler.Start(cancellationToken);
        Console.WriteLine("------- Started Scheduler -----------------");

        Console.WriteLine($"------- Interrupting the job every 7 seconds, {Interruptions} times ----");
        Console.WriteLine("------- (Ctrl+C stops early)");

        try
        {
            for (int i = 1; i <= Interruptions; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(7), cancellationToken);

                // Interrupt cancels the token every executing instance of this job holds, and answers
                // whether it found one to interrupt
                bool interrupted = await scheduler.Interrupt(job.Key, cancellationToken);
                Console.WriteLine($"------- Interrupt {i}/{Interruptions} of {job.Key}: {(interrupted ? "a job was running" : "nothing was running")}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("------- Seen enough, shutting down -------");
        }

        Console.WriteLine("------- Shutting Down ---------------------");

        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);

        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}
