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

namespace Quartz.Examples.Example04;

/// <summary>
/// This example will demonstrate how job parameters can be
/// passed into jobs and how state can be maintained.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class JobParametersAndJobsStateMaintenanceExample : IExample
{
    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing -------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete --------");

        Console.WriteLine("------- Scheduling Jobs ----------------");

        // a few seconds in the future, so both jobs start together
        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddSeconds(5);

        // job1 runs five times in all - at the start time, plus four repeats ten seconds apart
        IJobDetail job1 = JobBuilder.Create<ColorJob>()
            .WithIdentity("job1", "group1")
            // the initial parameters, put into the job's data map before it is scheduled
            .UsingJobData(ColorJob.FavoriteColor, "Green")
            .UsingJobData(ColorJob.ExecutionCount, 1)
            .Build();

        ISimpleTrigger trigger1 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).WithRepeatCount(4))
            .Build();

        DateTimeOffset scheduleTime1 = await scheduler.ScheduleJob(job1, trigger1, cancellationToken: cancellationToken);
        Console.WriteLine($"{job1.Key} will run at {scheduleTime1.LocalDateTime:HH:mm:ss}, then {trigger1.RepeatCount} more times every {trigger1.RepeatInterval.TotalSeconds:0} seconds");

        // job2 is the same job on the same schedule, with a different colour: two job details of one
        // job type, each with its own data map and its own persisted count
        IJobDetail job2 = JobBuilder.Create<ColorJob>()
            .WithIdentity("job2", "group1")
            .UsingJobData(ColorJob.FavoriteColor, "Red")
            .UsingJobData(ColorJob.ExecutionCount, 1)
            .Build();

        ISimpleTrigger trigger2 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger2", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).WithRepeatCount(4))
            .Build();

        DateTimeOffset scheduleTime2 = await scheduler.ScheduleJob(job2, trigger2, cancellationToken: cancellationToken);
        Console.WriteLine($"{job2.Key} will run at {scheduleTime2.LocalDateTime:HH:mm:ss}, then {trigger2.RepeatCount} more times every {trigger2.RepeatInterval.TotalSeconds:0} seconds");

        Console.WriteLine("------- Starting Scheduler ----------------");

        await scheduler.Start(cancellationToken);

        Console.WriteLine("------- Started Scheduler -----------------");

        await Watching.For(TimeSpan.FromSeconds(55), "the map's count climbing 1, 2, 3... while the field's count stays at 1", cancellationToken);

        Console.WriteLine("------- Shutting Down ---------------------");

        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);

        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}
