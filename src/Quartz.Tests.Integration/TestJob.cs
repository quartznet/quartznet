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

namespace Quartz.Tests.Integration
{
    /// <author>Marko Lahma (.NET)</author>
    public class TestJob : IJob
    {
        private static bool jobFired;

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// fires that is associated with the <see cref="IJob" />.
        /// </summary>
        /// <remarks>
        /// The implementation may wish to set a  result object on the 
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to 
        /// <see cref="IJobListener" />s or 
        /// <see cref="ITriggerListener" />s that are watching the job's 
        /// execution.
        /// </remarks>
        /// <param name="context">The execution context.</param>
        public void Execute(IJobExecutionContext context)
        {
            JobHasFired = true;
        }

        /// <summary>
        /// Gets or sets a value indicating whether job has fired.
        /// </summary>
        /// <value><c>true</c> if job has fired; otherwise, <c>false</c>.</value>
        public static bool JobHasFired
        {
            get { return jobFired; }
            set { jobFired = value; }
        }
    }
}