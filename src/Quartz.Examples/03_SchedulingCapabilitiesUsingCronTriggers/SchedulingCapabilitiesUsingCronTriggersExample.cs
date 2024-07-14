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

namespace Quartz.Examples.Example03;

/// <summary>
/// This example will demonstrate all of the basics of scheduling capabilities of
/// Quartz using Cron Triggers <see cref="ICronTrigger"/>.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SchedulingCapabilitiesUsingCronTriggersExample : IExample
{
    public virtual async Task Run()
    {
        Console.WriteLine("------- Initializing -------------------");

        // First we must get a reference to a scheduler
        StdSchedulerFactory sf = new StdSchedulerFactory();
        IScheduler sched = await sf.GetScheduler();

        Console.WriteLine("------- Initialization Complete --------");

        Console.WriteLine("------- Scheduling Jobs ----------------");

        // jobs can be scheduled before sched.start() has been called

        // job 1 will run every 20 seconds

        IJobDetail job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job1", "group1")
            .Build();

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithCronSchedule("0/20 * * * * ?")
            .Build();

        DateTimeOffset ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " has been scheduled to run at: " + ft
                          + " and repeat based on expression: "
                          + trigger.CronExpressionString);

        // job 2 will run every other minute (at 15 seconds past the minute)
        job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job2", "group1")
            .Build();

        trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger2", "group1")
            .WithCronSchedule("15 0/2 * * * ?")
            .Build();

        ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " has been scheduled to run at: " + ft
                          + " and repeat based on expression: "
                          + trigger.CronExpressionString);

        // job 3 will run every other minute but only between 8am and 5pm
        job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job3", "group1")
            .Build();

        trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .WithCronSchedule("0 0/2 8-17 * * ?")
            .Build();

        ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " has been scheduled to run at: " + ft
                          + " and repeat based on expression: "
                          + trigger.CronExpressionString);

        // job 4 will run every three minutes but only between 5pm and 11pm
        job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job4", "group1")
            .Build();

        trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger4", "group1")
            .WithCronSchedule("0 0/3 17-23 * * ?")
            .Build();

        ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " has been scheduled to run at: " + ft
                          + " and repeat based on expression: "
                          + trigger.CronExpressionString);

        // job 5 will run at 10am on the 1st and 15th days of the month
        job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job5", "group1")
            .Build();

        trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger5", "group1")
            .WithCronSchedule("0 0 10am 1,15 * ?")
            .Build();

        ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " has been scheduled to run at: " + ft
                          + " and repeat based on expression: "
                          + trigger.CronExpressionString);

        // job 6 will run every 30 seconds but only on Weekdays (Monday through Friday)
        job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job6", "group1")
            .Build();

        trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger6", "group1")
            .WithCronSchedule("0,30 * * ? * MON-FRI")
            .Build();

        ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " has been scheduled to run at: " + ft
                          + " and repeat based on expression: "
                          + trigger.CronExpressionString);

        // job 7 will run every 30 seconds but only on Weekends (Saturday and Sunday)
        job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job7", "group1")
            .Build();

        trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger7", "group1")
            .WithCronSchedule("0,30 * * ? * SAT,SUN")
            .Build();

        ft = await sched.ScheduleJob(job, trigger);
        Console.WriteLine(job.Key + " has been scheduled to run at: " + ft
                          + " and repeat based on expression: "
                          + trigger.CronExpressionString);

        Console.WriteLine("------- Starting Scheduler ----------------");

        // All of the jobs have been added to the scheduler, but none of the
        // jobs
        // will run until the scheduler has been started
        await sched.Start();

        Console.WriteLine("------- Started Scheduler -----------------");

        Console.WriteLine("------- Waiting five minutes... ------------");

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