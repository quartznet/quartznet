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

namespace Quartz.Examples.Example13
{
    /// <summary>
    /// A dumb implementation of Job, for unit testing purposes.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class SimpleRecoveryJob : IJob
    {
        private const string Count = "count";

        /// <summary> 
        /// Called by the <see cref="IScheduler" /> when a
        /// <see cref="ITrigger" /> fires that is associated with
        /// the <see cref="IJob" />.
        /// </summary>
        public virtual async Task Execute(IJobExecutionContext context)
        {
            JobKey jobKey = context.JobDetail.Key;

            // if the job is recovering print a message
            if (context.Recovering)
            {
                Console.WriteLine("SimpleRecoveryJob: {0} RECOVERING at {1:r}", jobKey, DateTime.Now);
            }
            else
            {
                Console.WriteLine("SimpleRecoveryJob: {0} starting at {1:r}", jobKey, DateTime.Now);
            }

            // delay for ten seconds
            await Task.Delay(TimeSpan.FromSeconds(10));

            JobDataMap data = context.JobDetail.JobDataMap;
            int count;
            if (data.ContainsKey(Count))
            {
                count = data.GetInt(Count);
            }
            else
            {
                count = 0;
            }
            count++;
            data.Put(Count, count);

            Console.WriteLine("SimpleRecoveryJob: {0} done at {1:r}\n Execution #{2}", jobKey, DateTime.Now, count);
        }
    }
}