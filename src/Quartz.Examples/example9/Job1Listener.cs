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

using Common.Logging;

namespace Quartz.Examples.Example9
{
    /// <author>wkratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class Job1Listener : IJobListener
    {
        private static readonly ILog log = LogManager.GetLogger(typeof (Job1Listener));

        public virtual string Name
        {
            get { return "job1_to_job2"; }
        }

        public virtual void JobToBeExecuted(IJobExecutionContext inContext)
        {
            log.Info("Job1Listener says: Job Is about to be executed.");
        }

        public virtual void JobExecutionVetoed(IJobExecutionContext inContext)
        {
            log.Info("Job1Listener says: Job Execution was vetoed.");
        }

        public virtual void JobWasExecuted(IJobExecutionContext inContext, JobExecutionException inException)
        {
            log.Info("Job1Listener says: Job was executed.");

            // Simple job #2
            IJobDetail job2 = JobBuilder.Create<SimpleJob2>()
                .WithIdentity("job2")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("job2Trigger")
                .StartNow()
                .Build();

            try
            {
                // schedule the job to run!
                inContext.Scheduler.ScheduleJob(job2, trigger);
            }
            catch (SchedulerException e)
            {
                log.Warn("Unable to schedule job2!");
                Console.Error.WriteLine(e.StackTrace);
            }
        }
    }
}