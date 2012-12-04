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
using System.Threading;

namespace Quartz.Examples.Example5
{
    /// <summary>
    /// A dumb implementation of Job, for unit testing purposes.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [PersistJobDataAfterExecution]
    [DisallowConcurrentExecution]
    public class StatefulDumbJob : IJob
    {
        public const string NumExecutions = "NumExecutions";
        public const string ExecutionDelay = "ExecutionDelay";

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// fires that is associated with the <see cref="IJob" />.
        /// </summary>
        public virtual void Execute(IJobExecutionContext context)
        {
            Console.Error.WriteLine("---{0} executing.[{1}]", context.JobDetail.Key, DateTime.Now.ToString("r"));

            JobDataMap map = context.JobDetail.JobDataMap;

            int executeCount = 0;
            if (map.ContainsKey(NumExecutions))
            {
                executeCount = map.GetInt(NumExecutions);
            }

            executeCount++;

            map.Put(NumExecutions, executeCount);

            int delay = 5;
            if (map.ContainsKey(ExecutionDelay))
            {
                delay = map.GetInt(ExecutionDelay);
            }

            try
            {
                Thread.Sleep(delay);
            }
            catch (ThreadInterruptedException)
            {
            }

            Console.Error.WriteLine("  -{0} complete ({1}).", context.JobDetail.Key, executeCount);
        }
    }
}