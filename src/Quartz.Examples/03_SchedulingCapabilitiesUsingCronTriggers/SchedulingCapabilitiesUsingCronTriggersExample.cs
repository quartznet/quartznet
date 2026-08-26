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

namespace Quartz.Examples.Example03;

/// <summary>
/// This example will demonstrate all of the basics of scheduling capabilities of
/// Quartz using Cron Triggers <see cref="ICronTrigger"/>.
/// </summary>
/// <remarks>
/// Most of these expressions describe something that will not happen for hours or days, which is the
/// nature of cron. The example prints what each one resolves to and then runs long enough to watch
/// the two that fire within the minute, so the vocabulary and the behaviour are both visible.
/// </remarks>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SchedulingCapabilitiesUsingCronTriggersExample : IExample
{
    /// <summary>
    /// The expressions, and what each one says out loud.
    /// </summary>
    private static readonly (string Expression, string Meaning)[] Schedules =
    [
        ("0/20 * * * * ?", "every 20 seconds"),
        ("15 0/2 * * * ?", "every other minute, at 15 seconds past"),
        ("0 0/2 8-17 * * ?", "every other minute, but only between 8am and 5pm"),
        ("0 0/3 17-23 * * ?", "every three minutes, but only between 5pm and 11pm"),
        ("0 0 10 1,15 * ?", "at 10am on the 1st and the 15th of the month"),
        ("0,30 * * ? * MON-FRI", "twice a minute, on weekdays"),
        ("0,30 * * ? * SAT,SUN", "twice a minute, at weekends"),
    ];

    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing -------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete --------");

        Console.WriteLine("------- Scheduling Jobs ----------------");

        for (int i = 0; i < Schedules.Length; i++)
        {
            (string expression, string meaning) = Schedules[i];

            IJobDetail job = JobBuilder.Create<SimpleJob>()
                .WithIdentity($"job{i + 1}", "group1")
                .Build();

            ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
                .WithIdentity($"trigger{i + 1}", "group1")
                .WithCronSchedule(expression)
                .Build();

            // the return value is the first time the expression resolves to, which is the quickest
            // way to check that an expression means what it was meant to mean
            DateTimeOffset firstFireTime = await scheduler.ScheduleJob(job, trigger, cancellationToken);

            Console.WriteLine($"{job.Key,-14} {expression,-24} {meaning}");
            Console.WriteLine($"{"",-14} first fires {firstFireTime.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
        }

        Console.WriteLine("------- Starting Scheduler ----------------");

        // All of the jobs have been added to the scheduler, but none of them
        // will run until the scheduler has been started
        await scheduler.Start(cancellationToken);

        Console.WriteLine("------- Started Scheduler -----------------");

        await Watching.For(TimeSpan.FromSeconds(70), "job1 every 20 seconds, and whichever of job6/job7 matches today", cancellationToken);

        Console.WriteLine("------- Shutting Down ---------------------");

        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);

        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}
