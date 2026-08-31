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

namespace Quartz.Examples.Example06;

/// <summary>
/// This example demonstrates how Quartz can handle <see cref="JobExecutionException"/> that are
/// thrown by jobs.
/// </summary>
/// <remarks>
/// Two jobs fail on their first firing, and each asks Quartz for something different afterwards:
/// <c>badJob1</c> fixes its own data and asks to be refired at once, <c>badJob2</c> asks for every
/// trigger it has to be unscheduled. By the end of the run one of them is still going and the other
/// has stopped for good.
/// </remarks>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class JobExecutionExceptionsExample : IExample
{
    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete ------------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

        // a few seconds in the future, so both start together
        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddSeconds(5);

        // badJob1 runs every ten seconds, and starts with a denominator it cannot divide by
        IJobDetail job1 = JobBuilder.Create<BadJob1>()
            .WithIdentity("badJob1", "group1")
            .UsingJobData(BadJob1.Denominator, 0)
            .Build();

        ISimpleTrigger trigger1 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
            .Build();

        DateTimeOffset firstFireTime1 = await scheduler.ScheduleJob(job1, trigger1, cancellationToken: cancellationToken);
        Console.WriteLine($"{job1.Key} will run at {firstFireTime1.LocalDateTime:HH:mm:ss}, every {trigger1.RepeatInterval.TotalSeconds:0} seconds, refiring immediately when it fails");

        // badJob2 runs every five seconds, and fails in a way it cannot fix
        IJobDetail job2 = JobBuilder.Create<BadJob2>()
            .WithIdentity("badJob2", "group1")
            .Build();

        ISimpleTrigger trigger2 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger2", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
            .Build();

        DateTimeOffset firstFireTime2 = await scheduler.ScheduleJob(job2, trigger2, cancellationToken: cancellationToken);
        Console.WriteLine($"{job2.Key} will run at {firstFireTime2.LocalDateTime:HH:mm:ss}, every {trigger2.RepeatInterval.TotalSeconds:0} seconds, unscheduling itself when it fails");

        Console.WriteLine("------- Starting Scheduler ----------------");

        // jobs don't start firing until Start() has been called...
        await scheduler.Start(cancellationToken);

        Console.WriteLine("------- Started Scheduler -----------------");

        await Watching.For(TimeSpan.FromSeconds(40), "badJob1 failing once, fixing itself and carrying on; badJob2 failing once and never returning", cancellationToken);

        Console.WriteLine("------- Shutting Down ---------------------");

        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);

        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}
