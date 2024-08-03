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

namespace Quartz.Examples.Example13;

/// <summary>
/// This example will demonstrate the clustering features of AdoJobStore.
/// </summary>
/// <remarks>
///
/// <para>
/// All instances MUST use a different properties file, because their instance
/// Ids must be different, however all other properties should be the same.
/// </para>
///
/// <para>
/// If you want it to clear out existing jobs & triggers, pass a command-line
/// argument called "clearJobs".
/// </para>
///
/// <para>
/// You should probably start with a "fresh" set of tables (assuming you may
/// have some data lingering in it from other tests), since mixing data from a
/// non-clustered setup with a clustered one can be bad.
/// </para>
///
/// <para>
/// Try killing one of the cluster instances while they are running, and see
/// that the remaining instance(s) recover the in-progress jobs. Note that
/// detection of the failure may take up to 15 or so seconds with the default
/// settings.
/// </para>
///
/// <para>
/// Also try running it with/without the shutdown-hook plugin registered with
/// the scheduler. (quartz.plugins.management.ShutdownHookPlugin).
/// </para>
///
/// <para>
/// <i>Note:</i> Never run clustering on separate machines, unless their
/// clocks are synchronized using some form of time-sync service (daemon).
/// </para>
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public class ClusteringJobsExecutionExample : IExample
{
    public virtual async Task Run(bool inClearJobs, bool inScheduleJobs)
    {
        // First we must get a reference to a scheduler
        IScheduler sched = await SchedulerBuilder.Create()
            .WithId("instance_one")
            .WithName("TestScheduler")
            .UseDefaultThreadPool(x => x.MaxConcurrency = 5)
            .WithMisfireThreshold(TimeSpan.FromSeconds(60))
            .UsePersistentStore(x =>
            {
                x.UseProperties = true;
                x.UseClustering();
                x.UseSqlServer("sql-server-01", TestConstants.SqlServerConnectionString);
                x.UseSystemTextJsonSerializer();
            })
            .BuildScheduler();

        // if running SQLite we need this
        // properties["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore, Quartz";

        if (inClearJobs)
        {
            Console.WriteLine("***** Deleting existing jobs/triggers *****");
            await sched.Clear();
        }

        Console.WriteLine("------- Initialization Complete -----------");

        if (inScheduleJobs)
        {
            Console.WriteLine("------- Scheduling Jobs ------------------");

            string schedId = sched.SchedulerInstanceId;

            int count = 1;

            IJobDetail job = JobBuilder.Create<SimpleRecoveryJob>()
                .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                .Build();

            ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
                .WithIdentity("triger_" + count, schedId)
                .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(5)))
                .Build();

            Console.WriteLine("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.Key, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds);

            count++;

            job = JobBuilder.Create<SimpleRecoveryJob>()
                .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                .WithIdentity("triger_" + count, schedId)
                .StartAt(DateBuilder.FutureDate(2, IntervalUnit.Second))
                .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(5)))
                .Build();

            Console.WriteLine($"{job.Key} will run at: {trigger.GetNextFireTimeUtc()} and repeat: {trigger.RepeatCount} times, every {trigger.RepeatInterval.TotalSeconds} seconds");
            await sched.ScheduleJob(job, trigger);

            count++;

            job = JobBuilder.Create<SimpleRecoveryStatefulJob>()
                .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                .WithIdentity("triger_" + count, schedId)
                .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(3)))
                .Build();

            Console.WriteLine($"{job.Key} will run at: {trigger.GetNextFireTimeUtc()} and repeat: {trigger.RepeatCount} times, every {trigger.RepeatInterval.TotalSeconds} seconds");
            await sched.ScheduleJob(job, trigger);

            count++;

            job = JobBuilder.Create<SimpleRecoveryJob>()
                .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                .WithIdentity("triger_" + count, schedId)
                .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(4)))
                .Build();

            Console.WriteLine($"{job.Key} will run at: {trigger.GetNextFireTimeUtc()} & repeat: {trigger.RepeatCount}/{trigger.RepeatInterval}");
            await sched.ScheduleJob(job, trigger);

            count++;

            job = JobBuilder.Create<SimpleRecoveryJob>()
                .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                .Build();

            trigger = (ISimpleTrigger) TriggerBuilder.Create()
                .WithIdentity("triger_" + count, schedId)
                .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromMilliseconds(4500)))
                .Build();

            Console.WriteLine($"{job.Key} will run at: {trigger.GetNextFireTimeUtc()} & repeat: {trigger.RepeatCount}/{trigger.RepeatInterval}");
            await sched.ScheduleJob(job, trigger);
        }

        // jobs don't start firing until start() has been called...
        Console.WriteLine("------- Starting Scheduler ---------------");
        await sched.Start();
        Console.WriteLine("------- Started Scheduler ----------------");

        Console.WriteLine("------- Waiting for one hour... ----------");

        await Task.Delay(TimeSpan.FromHours(1));

        Console.WriteLine("------- Shutting Down --------------------");
        await sched.Shutdown();
        Console.WriteLine("------- Shutdown Complete ----------------");
    }

    public Task Run()
    {
        bool clearJobs = true;
        bool scheduleJobs = true;
        /* TODO
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].ToUpper().Equals("clearJobs".ToUpper()))
            {
                clearJobs = true;
            }
            else if (args[i].ToUpper().Equals("dontScheduleJobs".ToUpper()))
            {
                scheduleJobs = false;
            }
        }
        */
        ClusteringJobsExecutionExample example = new ClusteringJobsExecutionExample();
        return example.Run(clearJobs, scheduleJobs);
    }
}