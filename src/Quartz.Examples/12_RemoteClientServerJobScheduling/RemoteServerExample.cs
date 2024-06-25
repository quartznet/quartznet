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

#if REMOTING
using System.Collections.Specialized;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Examples.Example12;

/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class RemoteServerJobSchedulingExample : IExample
{
    /// <summary>
    /// This example will start a server that will allow clients to remotely schedule jobs.
    /// </summary>
    /// <author>  James House, Bill Kratzer
    /// </author>
    public virtual async Task Run()
    {
        // set remoting exporter
        // reject non-local requests
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "RemoteServer",
            ["quartz.threadPool.type"] = typeof(DefaultThreadPool).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.threadPool.threadCount"] = "5",
            ["quartz.serializer.type"] = "stj",
            ["quartz.scheduler.exporter.type"] = "Quartz.Simpl.RemotingSchedulerExporter, Quartz",
            ["quartz.scheduler.exporter.port"] = "555",
            ["quartz.scheduler.exporter.bindName"] = "QuartzScheduler",
            ["quartz.scheduler.exporter.channelType"] = "tcp",
            ["quartz.scheduler.exporter.channelName"] = "httpQuartz",
            ["quartz.scheduler.exporter.rejectRemoteRequests"] = "true"
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler sched = await sf.GetScheduler();

        Console.WriteLine("------- Initialization Complete -----------");

        Console.WriteLine("------- Not scheduling any Jobs - relying on a remote client to schedule jobs --");

        Console.WriteLine("------- Starting Scheduler ----------------");

        // start the schedule
        await sched.Start();

        Console.WriteLine("------- Started Scheduler -----------------");

        Console.WriteLine("------- Waiting 5 minutes... ------------");

        // wait to give our jobs a chance to run
        await Task.Delay(TimeSpan.FromMinutes(5));

        // shut down the scheduler
        Console.WriteLine("------- Shutting Down ---------------------");
        await sched.Shutdown(true);
        Console.WriteLine("------- Shutdown Complete -----------------");

        SchedulerMetaData metaData = await sched.GetMetaData();
        Console.WriteLine("Executed " + metaData.NumberOfJobsExecuted + " jobs.");
    }
}
#endif