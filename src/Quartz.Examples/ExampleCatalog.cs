#region License

/*
 * Copyright 2009- Marko Lahma
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

namespace Quartz.Examples;

/// <summary>
/// The tour, in order.
/// </summary>
/// <remarks>
/// Written out rather than discovered by reflection. The number beside an example is the number on its
/// directory, and a reader who picks <c>7</c> off the menu expects to find the code in
/// <c>07_…</c> — a listing built by scanning the assembly and sorting on whatever the namespaces
/// happened to sort as could not promise that. It also means retiring an example is one deleted line
/// here rather than a hole nobody notices.
/// </remarks>
internal static class ExampleCatalog
{
    /// <summary>
    /// Every example, in tour order. The position in this list is the number the tour shows, and it
    /// matches the prefix on the example's directory.
    /// </summary>
    public static readonly List<ExampleEntry> All =
    [
        new("Simple job scheduler", "Schedules one job to run at a given time, and watches it fire.",
            () => new Example01.SimpleJobSchedulerExample()),
        new("Simple triggers", "The vocabulary of ISimpleTrigger: run once, repeat n times, repeat forever, retrigger by hand.",
            () => new Example02.SchedulingCapabilitiesUsingSimpleTriggersExample()),
        new("Cron triggers", "The vocabulary of ICronTrigger, with the next fire times each expression produces.",
            () => new Example03.SchedulingCapabilitiesUsingCronTriggersExample()),
        new("Job parameters and job state", "Passing data into a job, and keeping state across firings with [PersistJobDataAfterExecution].",
            () => new Example04.JobParametersAndJobsStateMaintenanceExample()),
        new("Misfire instructions", "Two identical triggers whose jobs run too long, and the two different things their misfire instructions do about it.",
            () => new Example05.SchedulingJobsSettingMisfireInstructionsExample()),
        new("Job execution exceptions", "Throwing JobExecutionException to refire immediately, and to unschedule the job for good.",
            () => new Example06.JobExecutionExceptionsExample()),
        new("Interrupting a job in progress", "IScheduler.Interrupt, and the cancellation token a running job receives from it.",
            () => new Example07.InterruptingJobsInProgressExample()),
        new("Excluding time with calendars", "Calendars that block a day of the year and a window of every minute, and the fires they suppress.",
            () => new Example08.ExcludeTimePeriodsUsingCalendarsExample()),
        new("Job listeners", "A job listener that schedules a second job when the first one finishes.",
            () => new Example09.TriggeringAJobUsingJobListenersExample()),
        new("A large number of jobs", "Five hundred jobs scheduled at once, and the thread pool working through them.",
            () => new Example10.RunningLargeNumberOfJobsExample()),
        new("Trigger priority", "Three triggers due at the same instant, and one worker thread deciding which goes first.",
            () => new Example11.RunJobsByPriorityWithTriggersPriorityExample()),
        new("Scheduling from an XML file", "Jobs and triggers read from quartz_jobs.xml, rescanned while the scheduler runs.",
            () => new Example12.ConfigureJobSchedulingByUsingXmlConfigurationsExample()),
        new("Clustering", "Several scheduler instances sharing one database, load-balancing the work and recovering each other's. Needs SQL Server.",
            () => new Example13.ClusteringJobsExecutionExample()),
    ];

    /// <summary>
    /// Finds the example the user named, by tour number.
    /// </summary>
    public static bool TryFind(string choice, out ExampleEntry entry)
    {
        if (int.TryParse(choice, out int number) && number >= 1 && number <= All.Count)
        {
            entry = All[number - 1];
            return true;
        }

        entry = default!;
        return false;
    }
}

/// <summary>
/// One line of the menu: what the example is called, what it shows, and how to make one.
/// </summary>
internal sealed record ExampleEntry(string Title, string Summary, Func<IExample> Create);
