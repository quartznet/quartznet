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

using Quartz.Impl.Calendar;

namespace Quartz.Examples.Example08;

/// <summary>
/// This example will demonstrate how calendars can be used
/// to exclude periods of time when scheduling should not
/// take place.
/// </summary>
/// <remarks>
/// A calendar never schedules anything. It only says which times a trigger may not fire at, and the
/// trigger skips to its next allowed time. Two of them here: one that blocks whole days, whose effect
/// shows in the fire time printed before anything runs, and one that blocks half of every minute,
/// whose effect is a job going quiet and coming back while the example runs.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class ExcludeTimePeriodsUsingCalendarsExample : IExample
{
    /// <summary>
    /// The group both jobs and both triggers live in.
    /// </summary>
    private const string Group = "group1";

    public virtual async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        IScheduler scheduler = await ExampleScheduler.Create(cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Excluding whole days --------------");

        // an AnnualCalendar blocks the same days every year: the holidays this business does not run on
        AnnualCalendar holidays = new AnnualCalendar();
        holidays.AddExcludedDay(new MonthDay(7, 4)); // fourth of July
        holidays.AddExcludedDay(new MonthDay(10, 31)); // halloween
        holidays.AddExcludedDay(new MonthDay(12, 25)); // christmas

        await scheduler.AddCalendar("holidays", holidays, cancellationToken: cancellationToken);

        // an hourly job whose first firing would be on halloween at 10am, were halloween allowed
        DateTimeOffset halloween = NextOccurrenceOf(month: 10, day: 31, hour: 10);

        IJobDetail holidayJob = JobBuilder.Create<SimpleJob>()
            .WithIdentity("holidayJob", Group)
            .Build();

        ITrigger holidayTrigger = TriggerBuilder.Create()
            .WithIdentity("holidayTrigger", Group)
            .StartAt(halloween)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithCalendarName("holidays")
            .Build();

        DateTimeOffset firstRunTime = await scheduler.ScheduleJob(holidayJob, holidayTrigger, cancellationToken);

        Console.WriteLine($"{holidayJob.Key} was scheduled to start at {halloween.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"{holidayJob.Key} will actually start at {firstRunTime.LocalDateTime:yyyy-MM-dd HH:mm:ss} - the calendar pushed it past halloween");

        Console.WriteLine("------- Excluding part of every minute ----");

        // a CronCalendar blocks every time its expression matches - here, the first half of every minute
        CronCalendar quietHalfMinute = new CronCalendar("0-29 * * * * ?");
        await scheduler.AddCalendar("quiet-half-minute", quietHalfMinute, cancellationToken: cancellationToken);

        IJobDetail chattyJob = JobBuilder.Create<SimpleJob>()
            .WithIdentity("chattyJob", Group)
            .Build();

        ITrigger chattyTrigger = TriggerBuilder.Create()
            .WithIdentity("chattyTrigger", Group)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
            .WithCalendarName("quiet-half-minute")
            .Build();

        await scheduler.ScheduleJob(chattyJob, chattyTrigger, cancellationToken);
        Console.WriteLine($"{chattyJob.Key} asks to run every five seconds, and may only run when the seconds hand is past 30");

        Console.WriteLine("------- Starting Scheduler ----------------");
        await scheduler.Start(cancellationToken);

        await Watching.For(TimeSpan.FromSeconds(90), "chattyJob firing in the second half of each minute and nowhere else", cancellationToken);

        Console.WriteLine("------- Shutting Down ---------------------");
        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }

    /// <summary>
    /// The next time the given day of the year comes round, this year or the next.
    /// </summary>
    private static DateTimeOffset NextOccurrenceOf(int month, int day, int hour)
    {
        // TimeProvider rather than DateTimeOffset.Now, which is banned in this repository and worth
        // avoiding in your own jobs for the same reason: a fake clock cannot reach it
        DateTimeOffset now = TimeProvider.System.GetLocalNow();

        DateTimeOffset thisYear = AtLocalTime(now.Year, month, day, hour);
        return thisYear > now ? thisYear : AtLocalTime(now.Year + 1, month, day, hour);
    }

    /// <summary>
    /// A wall-clock time in the local zone, carrying the offset in force on that date rather than the
    /// one in force today - which are not the same either side of a daylight saving change.
    /// </summary>
    private static DateTimeOffset AtLocalTime(int year, int month, int day, int hour)
    {
        DateTime wallClock = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, TimeZoneInfo.Local.GetUtcOffset(wallClock));
    }
}
