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

namespace Quartz.Examples.Example02;

/// <summary>
/// This example will demonstrate all of the basics of scheduling capabilities
/// of Quartz using Simple Triggers <see cref="ISimpleTrigger"/>.
/// </summary>
/// <remarks>
/// Every schedule here fires inside the minute the example runs for. A trigger due in five minutes
/// would be just as valid, and there would be nothing to watch.
/// </remarks>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SchedulingCapabilitiesUsingSimpleTriggersExample : IExample
{
    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing -------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete --------");

        Console.WriteLine("------- Scheduling Jobs ----------------");

        // jobs can be scheduled before the scheduler has been started

        // a few seconds in the future, so that everything below starts together
        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddSeconds(5);

        // job1 fires exactly once, at startTime: no schedule builder means no repetition
        IJobDetail job1 = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job1", "group1")
            .Build();

        ISimpleTrigger trigger1 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(startTime)
            .Build();

        Describe(job1.Key, trigger1, await scheduler.ScheduleJob(job1, trigger1, cancellationToken));

        // job2 fires at startTime and then five more times, five seconds apart
        IJobDetail job2 = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job2", "group1")
            .Build();

        ISimpleTrigger trigger2 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger2", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).WithRepeatCount(5))
            .Build();

        Describe(job2.Key, trigger2, await scheduler.ScheduleJob(job2, trigger2, cancellationToken));

        // job3 has two triggers of its own: one job, two schedules, and the job runs for both
        IJobDetail job3 = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job3", "group1")
            .Build();

        ISimpleTrigger trigger3 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).WithRepeatCount(2))
            .Build();

        Describe(job3.Key, trigger3, await scheduler.ScheduleJob(job3, trigger3, cancellationToken));

        // the second one names the job it belongs to rather than carrying one
        ISimpleTrigger trigger4 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger3", "group2")
            .StartAt(startTime.AddSeconds(3))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).WithRepeatCount(2))
            .ForJob(job3)
            .Build();

        Describe(job3.Key, trigger4, await scheduler.ScheduleJob(trigger4, cancellationToken));

        Console.WriteLine("------- Starting Scheduler ----------------");

        // none of the above runs until this happens
        await scheduler.Start(cancellationToken);

        Console.WriteLine("------- Started Scheduler -----------------");

        // a job with no trigger at all: durable, so the store keeps it, and fired by hand
        IJobDetail job4 = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job4", "group1")
            .StoreDurably()
            .Build();

        await scheduler.AddJob(job4, new AddJobOptions { Replace = true }, cancellationToken);

        Console.WriteLine("'Manually' triggering job4...");
        await scheduler.TriggerJob(job4.Key, cancellationToken: cancellationToken);

        await Watching.For(TimeSpan.FromSeconds(25), "job1 once, job2 every five seconds, job3 from both of its triggers", cancellationToken);

        // a trigger can be replaced while the scheduler runs, under the same key
        Console.WriteLine("------- Rescheduling... --------------------");

        ISimpleTrigger faster = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger2", "group1")
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(3)).WithRepeatCount(5))
            .Build();

        DateTimeOffset? rescheduled = await scheduler.RescheduleJob(faster.Key, faster, cancellationToken);
        Console.WriteLine($"trigger2 rescheduled to run at: {rescheduled?.LocalDateTime:HH:mm:ss}, now every three seconds");

        await Watching.For(TimeSpan.FromSeconds(25), "job2 on its new, faster schedule", cancellationToken);

        Console.WriteLine("------- Shutting Down ---------------------");

        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);

        Console.WriteLine("------- Shutdown Complete -----------------");

        // display some stats about the schedule that just ran
        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }

    private static void Describe(JobKey jobKey, ISimpleTrigger trigger, DateTimeOffset firstFireTime)
    {
        string repetition = trigger.RepeatCount == 0
            ? "once"
            : $"{trigger.RepeatCount} more times, every {trigger.RepeatInterval.TotalSeconds:0} seconds";

        Console.WriteLine($"{jobKey} will run at {firstFireTime.LocalDateTime:HH:mm:ss} ({trigger.Key}), then {repetition}");
    }
}
