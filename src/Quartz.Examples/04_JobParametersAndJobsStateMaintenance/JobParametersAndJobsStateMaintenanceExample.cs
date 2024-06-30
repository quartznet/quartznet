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

using Quartz.Impl;

namespace Quartz.Examples.Example04;

/// <summary>
/// This example will demonstrate how job parameters can be
/// passed into jobs and how state can be maintained.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class JobParametersAndJobsStateMaintenanceExample : IExample
{
    public virtual async Task Run()
    {
        Console.WriteLine("------- Initializing -------------------");

        // First we must get a reference to a scheduler
        StdSchedulerFactory sf = new StdSchedulerFactory();
        IScheduler sched = await sf.GetScheduler();

        Console.WriteLine("------- Initialization Complete --------");

        Console.WriteLine("------- Scheduling Jobs ----------------");

        // get a "nice round" time a few seconds in the future....
        DateTimeOffset startTime = DateBuilder.NextGivenSecondDate(null, 10);

        // job1 will only run 5 times (at start time, plus 4 repeats), every 10 seconds
        IJobDetail job1 = JobBuilder.Create<ColorJob>()
            .WithIdentity("job1", "group1")
            .Build();

        ISimpleTrigger trigger1 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).WithRepeatCount(4))
            .Build();

        // pass initialization parameters into the job
        job1.JobDataMap.Put(ColorJob.FavoriteColor, "Green");
        job1.JobDataMap.Put(ColorJob.ExecutionCount, 1);

        // schedule the job to run
        DateTimeOffset scheduleTime1 = await sched.ScheduleJob(job1, trigger1);
        Console.WriteLine($"{job1.Key} will run at: {scheduleTime1:r} and repeat: {trigger1.RepeatCount} times, every {trigger1.RepeatInterval.TotalSeconds} seconds");

        // job2 will also run 5 times, every 10 seconds

        IJobDetail job2 = JobBuilder.Create<ColorJob>()
            .WithIdentity("job2", "group1")
            .Build();

        ISimpleTrigger trigger2 = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger2", "group1")
            .StartAt(startTime)
            .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).WithRepeatCount(4))
            .Build();

        // pass initialization parameters into the job
        // this job has a different favorite color!
        job2.JobDataMap.Put(ColorJob.FavoriteColor, "Red");
        job2.JobDataMap.Put(ColorJob.ExecutionCount, 1);

        // schedule the job to run
        DateTimeOffset scheduleTime2 = await sched.ScheduleJob(job2, trigger2);
        Console.WriteLine($"{job2.Key} will run at: {scheduleTime2:r} and repeat: {trigger2.RepeatCount} times, every {trigger2.RepeatInterval.TotalSeconds} seconds");

        Console.WriteLine("------- Starting Scheduler ----------------");

        // All of the jobs have been added to the scheduler, but none of the jobs
        // will run until the scheduler has been started
        await sched.Start();

        Console.WriteLine("------- Started Scheduler -----------------");

        Console.WriteLine("------- Waiting 60 seconds... -------------");

        // wait five minutes to show jobs
        await Task.Delay(TimeSpan.FromMinutes(5));
        // executing...

        Console.WriteLine("------- Shutting Down ---------------------");

        await sched.Shutdown(true);

        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetaData metaData = await sched.GetMetaData();
        Console.WriteLine($"Executed {metaData.NumberOfJobsExecuted} jobs.");
    }
}