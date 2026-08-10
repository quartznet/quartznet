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
using Quartz.Impl.Calendar;

namespace Quartz.Examples.Example08;

/// <summary>
/// This example will demonstrate how calendars can be used
/// to exclude periods of time when scheduling should not
/// take place.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public class ExcludeTimePeriodsUsingCalendarsExample : IExample
{
    public virtual async Task Run()
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create();

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Scheduling Jobs -------------------");

        // Add the holiday calendar to the schedule
        AnnualCalendar holidays = new AnnualCalendar();

        // fourth of July (July 4)
        DateOnly fourthOfJuly = new DateOnly(DateTime.UtcNow.Year, 7, 4);
        holidays.AddExcludedDay(MonthDay.From(fourthOfJuly));

        // halloween (Oct 31)
        DateOnly halloween = new DateOnly(DateTime.UtcNow.Year, 10, 31);
        holidays.AddExcludedDay(MonthDay.From(halloween));

        // christmas (Dec 25)
        DateOnly christmas = new DateOnly(DateTime.UtcNow.Year, 12, 25);
        holidays.AddExcludedDay(MonthDay.From(christmas));

        // tell the schedule about our holiday calendar
        await scheduler.AddCalendar("holidays", holidays);

        // schedule a job to run hourly, starting on halloween
        // at 10 am

        DateTimeOffset runDate = DateBuilder.Create().InMonthOnDay(10, 31).AtHourMinuteAndSecond(0, 0, 10).Build();

        IJobDetail job = JobBuilder.Create<SimpleJob>()
            .WithIdentity("job1", "group1")
            .Build();

        ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(runDate)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithCalendarName("holidays")
            .Build();

        // schedule the job and print the first run date
        DateTimeOffset firstRunTime = await scheduler.ScheduleJob(job, trigger);

        // print out the first execution date.
        // Note:  Since Halloween (Oct 31) is a holiday, then
        // we will not run until the next day! (Nov 1)
        Console.WriteLine($"{job.Key} will run at: {firstRunTime:r} and repeat: {trigger.RepeatCount} times, every {trigger.RepeatInterval.TotalSeconds} seconds");

        // All of the jobs have been added to the scheduler, but none of the jobs
        // will run until the scheduler has been started
        Console.WriteLine("------- Starting Scheduler ----------------");
        await scheduler.Start();

        // wait 30 seconds:
        // note:  nothing will run
        Console.WriteLine("------- Waiting 30 seconds... --------------");

        // wait 30 seconds to show jobs
        await Task.Delay(TimeSpan.FromSeconds(30));
        // executing...

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await scheduler.Shutdown(true);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata();
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}