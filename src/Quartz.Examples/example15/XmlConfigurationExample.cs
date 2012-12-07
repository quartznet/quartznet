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

using System.Collections.Specialized;
using System.Threading;

using Common.Logging;

using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Examples.Example15
{
    /// <summary>
    /// This example will demonstrate how configuration can be
    /// done using an XML file.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public class XmlConfigurationExample : IExample
    {
        public string Name
        {
            get { return GetType().Name; }
        }

        public void Run()
        {
            ILog log = LogManager.GetLogger(typeof(XmlConfigurationExample));

            log.Info("------- Initializing ----------------------");

            // First we must get a reference to a scheduler
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.scheduler.instanceName"] = "XmlConfiguredInstance";
            
            // set thread pool info
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "5";
            properties["quartz.threadPool.threadPriority"] = "Normal";

            // job initialization plugin handles our xml reading, without it defaults are used
            properties["quartz.plugin.xml.type"] = "Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin, Quartz";
            properties["quartz.plugin.xml.fileNames"] = "~/quartz_jobs.xml";


            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();
            
            // we need to add calendars manually, lets create a silly sample calendar
            var dailyCalendar = new DailyCalendar("00:01", "23:59");
            dailyCalendar.InvertTimeRange = true;
            sched.AddCalendar("cal1", dailyCalendar, false, false);

            log.Info("------- Initialization Complete -----------");

            // all jobs and triggers are now in scheduler


            // Start up the scheduler (nothing can actually run until the 
            // scheduler has been started)
            sched.Start();
            log.Info("------- Started Scheduler -----------------");

            // wait long enough so that the scheduler as an opportunity to 
            // fire the triggers
            log.Info("------- Waiting 30 seconds... -------------");

            try
            {
                Thread.Sleep(30*1000);
            }
            catch (ThreadInterruptedException)
            {
            }

            // shut down the scheduler
            log.Info("------- Shutting Down ---------------------");
            sched.Shutdown(true);
            log.Info("------- Shutdown Complete -----------------");
        }
    }
}