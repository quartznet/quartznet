/*
 * Copyright 2004-2006 OpenSymphony
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
 */

using System;
using System.Collections;

using Quartz.Util;

namespace Quartz.Listener
{
    /// <summary>
    /// Keeps a collection of mappings of which Job to trigger after the completion
    /// of a given job.  If this listener is notified of a job completing that has a
    /// mapping, then it will then attempt to trigger the follow-up job.  This
    /// achieves "job chaining", or a "poor man's workflow".
    ///</summary>
    /// <remarks>
    /// <p>
    /// Generally an instance of this listener would be registered as a global
    /// job listener, rather than being registered directly to a given job.
    /// </p>
    /// <p>
    /// If for some reason there is a failure creating the trigger for the
    /// follow-up job (which would generally only be caused by a rare serious
    /// failure in the system, or the non-existence of the follow-up job), an error
    /// messsage is logged, but no other action is taken. If you need more rigorous
    /// handling of the error, consider scheduling the triggering of the flow-up
    /// job within your job itself.
    /// </p>
    ///</remarks>
    /// <author>James House</author>
    public class JobChainingJobListener : JobListenerSupport
    {
        private readonly string name;
        private readonly IDictionary chainLinks;

        /// <summary>
        /// Construct an instance with the given name.
        /// </summary>
        /// <param name="name">The name of this instance.</param>
        public JobChainingJobListener(string name)
        {
            if (name == null)
            {
                throw new ArgumentException("Listener name cannot be null!");
            }
            this.name = name;
            chainLinks = new Hashtable();
        }

        public override string Name
        {
            get { return name; }
        }

        /// <summary>
        /// Add a chain mapping - when the Job identified by the first key completes
        /// the job identified by the second key will be triggered.
        /// </summary>
        /// <param name="firstJob">a Key with the name and group of the first job</param>
        /// <param name="secondJob">a Key with the name and group of the follow-up job</param>
        public void AddJobChainLink(Key firstJob, Key secondJob)
        {
            if (firstJob == null || secondJob == null)
            {
                throw new ArgumentException("Key cannot be null!");
            }
            if (firstJob.Name == null || secondJob.Name == null)
            {
                throw new ArgumentException("Key cannot have a null name!");
            }

            chainLinks.Add(firstJob, secondJob);
        }

        public override void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException)
        {
            Key sj = (Key) chainLinks[context.JobDetail.Key];

            if (sj == null)
            {
                return;
            }

            Log.Info(string.Format("Job '{0}' will now chain to Job '{1}'", context.JobDetail.FullName, sj));

            try
            {
                if (context.JobDetail.Volatile || context.Trigger.Volatile)
                {
                    context.Scheduler.TriggerJobWithVolatileTrigger(sj.Name, sj.Group);
                }
                else
                {
                    context.Scheduler.TriggerJob(sj.Name, sj.Group);
                }
            }
            catch (SchedulerException se)
            {
                Log.Error(string.Format("Error encountered during chaining to Job '{0}'", sj), se);
            }
        }
    }
}
