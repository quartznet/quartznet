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

using Microsoft.Extensions.Logging;

using Quartz.Logging;

namespace Quartz.Listener
{
    /// <summary>
    /// Keeps a collection of mappings of which Job to trigger after the completion
    /// of a given job.  If this listener is notified of a job completing that has a
    /// mapping, then it will then attempt to trigger the follow-up job.  This
    /// achieves "job chaining", or a "poor man's workflow".
    ///</summary>
    /// <remarks>
    /// <para>
    /// Generally an instance of this listener would be registered as a global
    /// job listener, rather than being registered directly to a given job.
    /// </para>
    /// <para>
    /// If for some reason there is a failure creating the trigger for the
    /// follow-up job (which would generally only be caused by a rare serious
    /// failure in the system, or the non-existence of the follow-up job), an error
    /// message is logged, but no other action is taken. If you need more rigorous
    /// handling of the error, consider scheduling the triggering of the flow-up
    /// job within your job itself.
    /// </para>
    ///</remarks>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class JobChainingJobListener : JobListenerSupport
    {
        private readonly Dictionary<JobKey, JobKey> chainLinks;
        private readonly ILogger<JobChainingJobListener> logger;

        /// <summary>
        /// Construct an instance with the given name.
        /// </summary>
        /// <param name="name">The name of this instance.</param>
        public JobChainingJobListener(string name)
        {
            if (name is null)
            {
                ThrowHelper.ThrowArgumentException("Listener name cannot be null!");
            }
            Name = name;
            chainLinks = new Dictionary<JobKey, JobKey>();
            logger = LogProvider.CreateLogger<JobChainingJobListener>();
        }

        public override string Name { get; }

        /// <summary>
        /// Add a chain mapping - when the Job identified by the first key completes
        /// the job identified by the second key will be triggered.
        /// </summary>
        /// <param name="firstJob">a JobKey with the name and group of the first job</param>
        /// <param name="secondJob">a JobKey with the name and group of the follow-up job</param>
        public void AddJobChainLink(JobKey firstJob, JobKey secondJob)
        {
            if (firstJob == null || secondJob == null)
            {
                ThrowHelper.ThrowArgumentException("Key cannot be null!");
            }
            if (firstJob.Name == null || secondJob.Name == null)
            {
                ThrowHelper.ThrowArgumentException("Key cannot have a null name!");
            }

            chainLinks.Add(firstJob, secondJob);
        }

        public override async Task JobWasExecuted(IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            chainLinks.TryGetValue(context.JobDetail.Key, out var sj);

            if (sj == null)
            {
                return;
            }

            logger.LogInformation("Job '{JobKey}' will now chain to Job '{Job}'", context.JobDetail.Key,sj);

            try
            {
                await context.Scheduler.TriggerJob(sj, cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                logger.LogError(se,"Error encountered during chaining to Job '{Job}'", sj);
            }
        }
    }
}
