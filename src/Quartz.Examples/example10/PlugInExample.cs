#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System;
using System.Collections.Specialized;
using System.Threading;

using Common.Logging;

using Quartz.Impl;

namespace Quartz.Examples.Example10
{
    /// <summary> 
    /// Plugin example.
    /// </summary>
    /// <author>James House, Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class PlugInExample : IExample
    {
        public string Name
        {
            get { throw new NotImplementedException(); }
        }

        public virtual void Run()
        {
            ILog log = LogManager.GetLogger(typeof (PlugInExample));

            // our properties that enable XML configuration plugin
            // and makes it watch for changes every two minutes (120 seconds)
            var properties = new NameValueCollection();
            properties["quartz.plugin.triggHistory.type"] = "Quartz.Plugin.History.LoggingJobHistoryPlugin";

            properties["quartz.plugin.jobInitializer.type"] = "Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin";
            properties["quartz.plugin.jobInitializer.fileNames"] = "quartz_jobs.xml";
            properties["quartz.plugin.jobInitializer.failOnFileNotFound"] = "true";
            properties["quartz.plugin.jobInitializer.scanInterval"] = "120";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();

            log.Info("------- Initialization Complete -----------");

            log.Info("------- Not Scheduling any Jobs - relying on XML definitions --");

            log.Info("------- Starting Scheduler ----------------");

            // start the schedule 
            sched.Start();

            log.Info("------- Started Scheduler -----------------");

            log.Info("------- Waiting five minutes... -----------");

            // wait five minutes to give our jobs a chance to run
            try
            {
                Thread.Sleep(TimeSpan.FromMinutes(5));
            }
            catch (ThreadInterruptedException)
            {
            }

            // shut down the scheduler
            log.Info("------- Shutting Down ---------------------");
            sched.Shutdown(true);
            log.Info("------- Shutdown Complete -----------------");

            SchedulerMetaData metaData = sched.GetMetaData();
            log.Info("Executed " + metaData.NumberOfJobsExecuted + " jobs.");
        }
    }
}