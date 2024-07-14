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

using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz.Examples.Example10;

/// <summary>
/// This example will demonstrate how to plugin xml configuration
/// to the Quartz job scheduler to execute jobs that comes from
/// the specified configuration file.
/// </summary>
/// <author>James House, Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class RunningJobsByPlugInXmlConfigurationExample : IExample
{
    public virtual async Task Run()
    {
        // our configuration that enables XML configuration plugin
        // and makes it watch for changes every two minutes (120 seconds)
        var properties = new NameValueCollection
        {
            ["quartz.plugin.triggHistory.type"] = "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins",
            ["quartz.plugin.jobInitializer.type"] = "Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin, Quartz.Plugins",
            ["quartz.plugin.jobInitializer.fileNames"] = "quartz_jobs.xml",
            ["quartz.plugin.jobInitializer.failOnFileNotFound"] = "true",
            ["quartz.plugin.jobInitializer.scanInterval"] = "120"
        };

        // First we must get a reference to a scheduler
        StdSchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler sched = await sf.GetScheduler();

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Not Scheduling any Jobs - relying on XML definitions --");

        Console.WriteLine("------- Starting Scheduler ----------------");

        // start the schedule
        await sched.Start();

        Console.WriteLine("------- Started Scheduler -----------------");

        Console.WriteLine("------- Waiting five minutes... -----------");

        // wait five minutes to give our jobs a chance to run
        await Task.Delay(TimeSpan.FromMinutes(5));

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await sched.Shutdown(true);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetaData metaData = await sched.GetMetaData();
        Console.WriteLine("Executed " + metaData.NumberOfJobsExecuted + " jobs.");
    }
}