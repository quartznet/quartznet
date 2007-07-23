/* 
 * Copyright 2006 OpenSymphony 
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
using Common.Logging;

namespace Quartz.Examples.Example14
{
    /// <summary>
    /// This is just a simple job that echos the name of the Trigger
    /// that fired it.
    /// </summary>
    public class TriggerEchoJob : IJob
    {
        private static readonly ILog LOG = LogManager.GetLogger(typeof (TriggerEchoJob));

        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="Trigger"/>
        /// fires that is associated with the <see cref="IJob"/>.
        /// <p>
        /// The implementation may wish to set a  result object on the
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to
        /// <see cref="IJobListener"/>s or
        /// <see cref="ITriggerListener"/>s that are watching the job's
        /// execution.
        /// </p>
        /// 	<param name="context">The execution context.</param>
        /// </summary>
        /// <param name="context"></param>
        public void Execute(JobExecutionContext context)
        {
            LOG.Info("TRIGGER: " + context.Trigger.Name);
        }

    }
}