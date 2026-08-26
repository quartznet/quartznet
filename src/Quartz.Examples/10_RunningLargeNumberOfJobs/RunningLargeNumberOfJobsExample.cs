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

namespace Quartz.Examples.Example10;

/// <summary>
/// This example will demonstrate how to run a large number
/// of jobs.
/// </summary>
/// <remarks>
/// Five hundred jobs, each doing up to a second of pretend work, fired within ten seconds of each
/// other. The thread pool is what decides how fast they get through: fifty at a time here, against
/// the ten the other examples use.
/// </remarks>
/// <author>James House, Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class RunningLargeNumberOfJobsExample : IExample
{
    private const int NumberOfJobs = 500;
    private const int MaxConcurrency = 50;

    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(
            maxConcurrency: MaxConcurrency,
            cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine($"------- Scheduling {NumberOfJobs} Jobs ---------------");

        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddSeconds(5);

        for (int count = 1; count <= NumberOfJobs; count++)
        {
            IJobDetail job = JobBuilder
                .Create<SimpleJob>()
                .WithIdentity("job" + count, "group_1")
                .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                .UsingJobData(SimpleJob.DelayMilliseconds, Random.Shared.Next(1000))
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger_" + count, "group_1")
                .StartAt(startTime.AddMilliseconds(count * 20)) // space fire times a small bit
                .Build();

            await scheduler.ScheduleJob(job, trigger, cancellationToken);

            if (count % 100 == 0)
            {
                Console.WriteLine($"...scheduled {count} jobs");
            }
        }

        Console.WriteLine("------- Starting Scheduler ----------------");

        // start the schedule
        await scheduler.Start(cancellationToken);

        Console.WriteLine("------- Started Scheduler -----------------");

        await Watching.For(TimeSpan.FromSeconds(45), $"{MaxConcurrency} threads working through {NumberOfJobs} jobs", cancellationToken);

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} of {NumberOfJobs} jobs.");
    }
}
