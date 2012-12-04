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

namespace Quartz.Examples.Example12
{
    /// <summary>
    /// A dumb implementation of Job, for unit testing purposes.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class SimpleJob : IJob
    {
        public const string Message = "msg";
        private static readonly ILog log = LogManager.GetLogger(typeof (SimpleJob));


        /// <summary> 
        /// Called by the <see cref="IScheduler" /> when a
        /// <see cref="ITrigger" /> fires that is associated with
        /// the <see cref="IJob" />.
        /// </summary>
        public virtual void Execute(IJobExecutionContext context)
        {
            // This job simply prints out its job name and the
            // date and time that it is running
            JobKey jobKey = context.JobDetail.Key;

            string message = context.JobDetail.JobDataMap.GetString(Message);

            log.InfoFormat("SimpleJob: {0} executing at {1}", jobKey, DateTime.Now.ToString("r"));
            log.InfoFormat("SimpleJob: msg: {0}", message);
        }
    }
}