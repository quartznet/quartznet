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

namespace Quartz.Examples.Example07
{
    /// <summary>
    /// A dumb implementation of an InterruptableJob, for unit testing purposes.
    /// </summary>
    /// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
    /// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class DumbInterruptableJob : IJob
    {
        // job name
        private JobKey? jobKey;

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// fires that is associated with the <see cref="IJob" />.
        /// </summary>
        public virtual async Task Execute(IJobExecutionContext context)
        {
            jobKey = context.JobDetail.Key;
            Console.WriteLine("---- {0} executing at {1:r}", jobKey, DateTime.Now);

            try
            {
                // main job loop...
                // do some work... in this example we are 'simulating' work by sleeping...

                for (int i = 0; i < 4; i++)
                {
                    await Task.Delay(10*1000);

                    // periodically check if we've been interrupted...
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("--- {0}  -- Interrupted... bailing out!", jobKey);
                        return; // could also choose to throw a JobExecutionException
                        // if that made for sense based on the particular
                        // job's responsibilities/behaviors
                    }
                }
            }
            finally
            {
                Console.WriteLine("---- {0} completed at {1:r}", jobKey, DateTime.Now);
            }
        }
    }
}