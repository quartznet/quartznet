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
 using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Quartz.Impl;
 using Quartz.Logging;

namespace Quartz.Examples.Example12
{
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
            ILog log = LogProvider.GetLogger(typeof(RemoteServerJobSchedulingExample));

            // set remoting exporter
            // reject non-local requests
            NameValueCollection properties = new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = "RemoteServer",
                ["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz",
                ["quartz.threadPool.threadCount"] = "5",
                ["quartz.serializer.type"] = "json",
                ["quartz.scheduler.exporter.type"] = "Quartz.Simpl.RemotingSchedulerExporter, Quartz",
                ["quartz.scheduler.exporter.port"] = "555",
                ["quartz.scheduler.exporter.bindName"] = "QuartzScheduler",
                ["quartz.scheduler.exporter.channelType"] = "tcp",
                ["quartz.scheduler.exporter.channelName"] = "httpQuartz",
                ["quartz.scheduler.exporter.rejectRemoteRequests"] = "true"
            };

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            log.Info("------- Initialization Complete -----------");

            log.Info("------- Not scheduling any Jobs - relying on a remote client to schedule jobs --");

            log.Info("------- Starting Scheduler ----------------");

            // start the schedule
            await sched.Start();

            log.Info("------- Started Scheduler -----------------");

            log.Info("------- Waiting 5 minutes... ------------");

            // wait to give our jobs a chance to run
            await Task.Delay(TimeSpan.FromMinutes(5));

            // shut down the scheduler
            log.Info("------- Shutting Down ---------------------");
            await sched.Shutdown(true);
            log.Info("------- Shutdown Complete -----------------");

            SchedulerMetaData metaData = await sched.GetMetaData();
            log.Info("Executed " + metaData.NumberOfJobsExecuted + " jobs.");
        }
    }
}
 #endif