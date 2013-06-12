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

namespace Quartz.Examples.Example12
{
	
	/// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class RemoteServerExample : IExample
	{
		public string Name
		{
			get { return GetType().Name; }
		}

		/// <summary>
		/// This example will start a server that will allow clients to remotely schedule jobs.
		/// </summary>
		/// <author>  James House, Bill Kratzer
		/// </author>
		public virtual void Run()
		{
			ILog log = LogManager.GetLogger(typeof(RemoteServerExample));
			
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.scheduler.instanceName"] = "RemoteServer";

            // set thread pool info
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "5";
            properties["quartz.threadPool.threadPriority"] = "Normal";

            // set remoting exporter
            properties["quartz.scheduler.exporter.type"] = "Quartz.Simpl.RemotingSchedulerExporter, Quartz";
            properties["quartz.scheduler.exporter.port"] = "555";
            properties["quartz.scheduler.exporter.bindName"] = "QuartzScheduler";
            properties["quartz.scheduler.exporter.channelType"] = "tcp";
            properties["quartz.scheduler.exporter.channelName"] = "httpQuartz";
            // reject non-local requests
            properties["quartz.scheduler.exporter.rejectRemoteRequests"] = "true";

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();
			
			log.Info("------- Initialization Complete -----------");
			
			log.Info("------- Not scheduling any Jobs - relying on a remote client to schedule jobs --");
			
			log.Info("------- Starting Scheduler ----------------");
			
			// start the schedule
			sched.Start();
			
			log.Info("------- Started Scheduler -----------------");
			
			log.Info("------- Waiting 5 minutes... ------------");
			
			// wait to give our jobs a chance to run
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