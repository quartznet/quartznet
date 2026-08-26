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

namespace Quartz.Examples.Example09;

/// <summary>
/// Demonstrates the behavior of <see cref="IJobListener" />s.  In particular,
/// this example will use a job listener to trigger another job after one
/// job successfully executes.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public class TriggeringAJobUsingJobListenersExample : IExample
{
    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

        // schedule a job to run a few seconds from now
        IJobDetail job = JobBuilder.Create<SimpleJob1>()
            .WithIdentity("job1")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1")
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(5))
            .Build();

        // the matcher decides which jobs the listener hears about; without one it would hear about
        // every job this scheduler runs
        IJobListener listener = new SimpleJob1Listener();
        IMatcher<JobKey> matcher = Matchers.Key(job.Key);
        scheduler.ListenerManager.AddJobListener(listener, matcher);

        Console.WriteLine($"Listener '{listener.Name}' added for {job.Key}");

        // schedule the job to run
        DateTimeOffset firstFireTime = await scheduler.ScheduleJob(job, trigger, cancellationToken);
        Console.WriteLine($"{job.Key} will run at {firstFireTime.LocalDateTime:HH:mm:ss}, once, and job2 follows from the listener");

        // the job has been added to the scheduler, but it will not run
        // until the scheduler has been started
        Console.WriteLine("------- Starting Scheduler ----------------");
        await scheduler.Start(cancellationToken);

        await Watching.For(TimeSpan.FromSeconds(20), "job1 running once, and job2 running straight after it without a schedule of its own", cancellationToken);

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}
