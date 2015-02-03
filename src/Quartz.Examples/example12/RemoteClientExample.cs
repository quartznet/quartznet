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

using Common.Logging;

using Quartz.Impl;

namespace Quartz.Examples.Example12
{
    /// <summary> 
    /// This example is a client program that will remotely 
    /// talk to the scheduler to schedule a job.   In this 
    /// example, we will need to use the JDBC Job Store.  The 
    /// client will connect to the JDBC Job Store remotely to 
    /// schedule the job.
    /// </summary>
    /// <author>James House</author>
    /// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class RemoteClientExample : IExample
    {
        public virtual void Run()
        {
            ILog log = LogManager.GetLogger(typeof (RemoteClientExample));

            NameValueCollection properties = new NameValueCollection();
            properties["quartz.scheduler.instanceName"] = "RemoteClient";

            // set thread pool info
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "5";
            properties["quartz.threadPool.threadPriority"] = "Normal";

            // set remoting exporter
            properties["quartz.scheduler.proxy"] = "true";
            properties["quartz.scheduler.proxy.address"] = "tcp://127.0.0.1:555/QuartzScheduler";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();

            // define the job and ask it to run

            IJobDetail job = JobBuilder.Create<SimpleJob>()
                .WithIdentity("remotelyAddedJob", "default")
                .Build();

            JobDataMap map = job.JobDataMap;
            map.Put("msg", "Your remotely added job has executed!");

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("remotelyAddedTrigger", "default")
                .ForJob(job.Key)
                .WithCronSchedule("/5 * * ? * *")
                .Build();

            // schedule the job
            sched.ScheduleJob(job, trigger);

            log.Info("Remote job scheduled.");
        }

        public string Name
        {
            get { return null; }
        }
    }
}