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

using System;
using System.Threading.Tasks;

using Quartz.Logging;

namespace Quartz.Examples.Example05
{
    /// <summary>
    /// A dumb implementation of Job, for unit testing purposes.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [PersistJobDataAfterExecution]
    [DisallowConcurrentExecution]
    public class MisfireJob : IJob
    {
        // Logging
        private static readonly ILog log = LogProvider.GetLogger(typeof (MisfireJob));

        // Constants
        public const string NumExecutions = "NumExecutions";
        public const string ExecutionDelay = "ExecutionDelay";

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// fires that is associated with the <see cref="IJob" />.
        /// </summary>
        public virtual async Task Execute(IJobExecutionContext context)
        {
            JobKey jobKey = context.JobDetail.Key;
            log.Info($"---{jobKey} executing at {DateTime.Now:r}");

            // default delay to five seconds
            int delay = 5;

            // use the delay passed in as a job parameter (if it exists)
            JobDataMap map = context.JobDetail.JobDataMap;
            if (map.ContainsKey(ExecutionDelay))
            {
                delay = map.GetInt(ExecutionDelay);
            }

            await Task.Delay(TimeSpan.FromSeconds(delay));

            log.Info($"---{jobKey} completed at {DateTime.Now:r}");
        }
    }
}