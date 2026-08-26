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

namespace Quartz.Examples.Example05;

/// <summary>
/// Demonstrates the behavior of <see cref="PersistJobDataAfterExecutionAttribute" />,
/// as well as how misfire instructions affect the firings of triggers of
/// that have <see cref="DisallowConcurrentExecutionAttribute" /> present -
/// when the jobs take longer to execute that the frequency of the trigger's
/// repetition.
/// </summary>
/// <remarks>
/// <para>
/// Two triggers, identical schedules, identical jobs. Both want to fire every three seconds and both
/// jobs take ten, so both fall behind by seven seconds a cycle until the scheduler calls it a misfire.
/// What they then do about it is the whole point:
/// </para>
/// <para>
/// <c>trigger1</c> keeps the default smart policy, which advances it to its next scheduled time and
/// drops the firings it missed. <c>trigger2</c> asks for
/// <see cref="SimpleTriggerMisfireInstruction.NowWithExistingCount" />, which fires it immediately
/// instead. Watch the two run at different rates from the first misfire onwards.
/// </para>
/// </remarks>
/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
/// <author>Marko Lahma (.NET)</author>
public class SchedulingJobsSettingMisfireInstructionsExample : IExample
{
    /// <summary>
    /// The group both jobs and both triggers live in.
    /// </summary>
    private const string Group = "group1";

    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing -------------------");

        // A trigger is only "misfired" once it is later than the misfire threshold, which defaults to
        // a minute. Five seconds instead, so that a job overrunning by seven counts as one straight
        // away and the example has something to show inside a minute rather than inside ten.
        IScheduler scheduler = await ExampleScheduler.Create(
            misfireThreshold: TimeSpan.FromSeconds(5),
            cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Scheduling Jobs -----------");

        // a few seconds in the future, so both start together
        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddSeconds(5);

        // job1 wants to run every three seconds, and takes ten
        IJobDetail job1 = JobBuilder.Create<SlowJob>()
            .WithIdentity("slowJob1", Group)
            .UsingJobData(SlowJob.ExecutionDelaySeconds, 10)
            .Build();

        ISimpleTrigger trigger1 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", Group)
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(3)).RepeatForever())
            // no misfire instruction: the smart policy, which for a repeat-forever simple trigger
            // means "carry on from now, and forget the firings that were missed"
            .Build();

        DateTimeOffset firstFireTime1 = await scheduler.ScheduleJob(job1, trigger1, cancellationToken);
        Console.WriteLine($"{job1.Key} will run at {firstFireTime1.LocalDateTime:HH:mm:ss}, every {trigger1.RepeatInterval.TotalSeconds:0} seconds, smart misfire policy");

        // job2 is the same in every respect but one
        IJobDetail job2 = JobBuilder.Create<SlowJob>()
            .WithIdentity("slowJob2", Group)
            .UsingJobData(SlowJob.ExecutionDelaySeconds, 10)
            .Build();

        ISimpleTrigger trigger2 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger2", Group)
            .StartAt(startTime)
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromSeconds(3))
                .RepeatForever()
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithExistingCount))
            .Build();

        DateTimeOffset firstFireTime2 = await scheduler.ScheduleJob(job2, trigger2, cancellationToken);
        Console.WriteLine($"{job2.Key} will run at {firstFireTime2.LocalDateTime:HH:mm:ss}, every {trigger2.RepeatInterval.TotalSeconds:0} seconds, misfires fire immediately");

        Console.WriteLine("------- Starting Scheduler ----------------");

        // jobs don't start firing until Start() has been called...
        await scheduler.Start(cancellationToken);

        Console.WriteLine("------- Started Scheduler -----------------");

        await Watching.For(TimeSpan.FromSeconds(75), "the gap between 'due at' and 'started at' opening up, and the two triggers parting company", cancellationToken);

        Console.WriteLine("------- Shutting Down ---------------------");

        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);

        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}
