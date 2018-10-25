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
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;

namespace Quartz.Examples.Example16
{
    /// <summary>
    /// This is a job that is meant to run using async/await pattern in .NET CLR thread pool.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class AsyncJob : IJob
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (AsyncJob));

        /// <summary> 
        /// Called by the <see cref="IScheduler" /> when a
        /// <see cref="ITrigger" /> fires that is associated with
        /// the <see cref="IJob" />.
        /// </summary>
        public virtual async Task Execute(IJobExecutionContext context)
        {
            // This job simply prints out its job name and the
            // date and time that it is running
            JobKey jobKey = context.JobDetail.Key;

            log.InfoFormat("Job initially executing on thread {0}", Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);

            log.InfoFormat("Job continuing executing on thread {0} after first await", Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);

            log.InfoFormat("Job continuing executing on thread {0} after second await", Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
            log.InfoFormat("Cancellation requested: {0}", context.CancellationToken.IsCancellationRequested);
   
            context.CancellationToken.ThrowIfCancellationRequested();

            log.InfoFormat("Finished Executing job: {0} at {1}", jobKey, DateTime.Now.ToString("r"));
        }
    }
}