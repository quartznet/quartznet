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

namespace Quartz.Examples.Example12;

/// <summary>
/// This example will demonstrate how configuration can be
/// done using an XML file.
/// </summary>
/// <remarks>
/// The scheduler is handed no jobs and no triggers in code. It is handed a plugin that reads them out
/// of <c>quartz_jobs.xml</c>, and rescans that file while it runs - so the schedule can be changed by
/// editing a file, without stopping anything. Edit the file the example prints, and watch it happen.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class ConfigureJobSchedulingByUsingXmlConfigurationsExample : IExample
{
    public async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("------- Initializing ----------------------");

        // This example configures its own scheduler rather than using the shared one, because the
        // plugins are the thing it is about
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        builder.ConfigureScheduler(options => options.InstanceName = "XmlConfiguredInstance")
            .UseDefaultThreadPool(maxConcurrency: 5)
            // the job initialization plugin handles our xml reading; without it, defaults are used
            .UseXmlSchedulingConfiguration(x =>
            {
                x.Files.Add("~/quartz_jobs.xml");
                // this is the default
                x.FailOnFileNotFound = true;
                // this is not the default
                x.FailOnSchedulingError = true;
                // and neither is this: zero means "read it once", anything else means "keep looking"
                x.ScanInterval = TimeSpan.FromSeconds(10);
            })
            // every job about to run and every job that finished, logged by a plugin rather than by
            // anything the jobs themselves do
            .UseJobHistoryLogging();

        IScheduler scheduler = await builder.BuildScheduler(cancellationToken);

        // calendars are not part of the XML schedule, so this one is added in code
        DailyCalendar dailyCalendar = new DailyCalendar(new TimeOnly(0, 1), new TimeOnly(23, 59));
        dailyCalendar.InvertTimeRange = true;
        await scheduler.AddCalendar("cal1", dailyCalendar, cancellationToken: cancellationToken);

        Console.WriteLine("------- Initialization Complete -----------");

        // all jobs and triggers are now in the scheduler, having come out of the file
        string file = Path.Combine(AppContext.BaseDirectory, "quartz_jobs.xml");
        Console.WriteLine($"------- Watching {file}");
        Console.WriteLine("------- Edit it while this runs - change repeat-interval, add a trigger - and the schedule follows");

        // Start up the scheduler (nothing can actually run until the
        // scheduler has been started)
        await scheduler.Start(cancellationToken);
        Console.WriteLine("------- Started Scheduler -----------------");

        await Watching.For(TimeSpan.FromSeconds(60), "jobName1 running on the schedule the file gave it, and the history plugin logging every firing", cancellationToken);

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetadata metadata = await scheduler.GetMetadata(CancellationToken.None);
        Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
    }
}
