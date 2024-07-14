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

namespace Quartz.Examples.Example15;

/// <summary>
/// This example will demonstrate how configuration can be
/// done using an XML file.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public class ConfigureJobSchedulingByUsingXmlConfigurationsExample : IExample
{
    public async Task Run()
    {
        Console.WriteLine("------- Initializing ----------------------");

        // First we must get a reference to a scheduler
        var sched = await SchedulerBuilder.Create()
            .WithName("XmlConfiguredInstance")
            .UseDefaultThreadPool(maxConcurrency: 5)
            // job initialization plugin handles our xml reading, without it defaults are used
            .UseXmlSchedulingConfiguration(x =>
            {
                x.Files = ["~/quartz_jobs.xml"];
                // this is the default
                x.FailOnFileNotFound = true;
                // this is not the default
                x.FailOnSchedulingError = true;
            })
            .BuildScheduler();

        // we need to add calendars manually, lets create a silly sample calendar
        var dailyCalendar = new DailyCalendar("00:01", "23:59");
        dailyCalendar.InvertTimeRange = true;
        await sched.AddCalendar("cal1", dailyCalendar, false, false);

        Console.WriteLine("------- Initialization Complete -----------");

        // all jobs and triggers are now in scheduler

        // Start up the scheduler (nothing can actually run until the
        // scheduler has been started)
        await sched.Start();
        Console.WriteLine("------- Started Scheduler -----------------");

        // wait long enough so that the scheduler as an opportunity to
        // fire the triggers
        Console.WriteLine("------- Waiting 30 seconds... -------------");

        await Task.Delay(30 * 1000);

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await sched.Shutdown(true);
        Console.WriteLine("------- Shutdown Complete -----------------");
    }
}