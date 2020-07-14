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

namespace Quartz.Examples.Example14
{
    /// <summary>
    /// This is a simple job that echos the name of the Trigger
    /// that fired it.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public class TriggerEchoJob : IJob
    {
        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="ITrigger"/>
        /// fires that is associated with the <see cref="IJob"/>.
        /// <para>
        /// The implementation may wish to set a  result object on the
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to
        /// <see cref="IJobListener"/>s or
        /// <see cref="ITriggerListener"/>s that are watching the job's
        /// execution.
        /// </para>
        /// 	<param name="context">The execution context.</param>
        /// </summary>
        /// <param name="context"></param>
        public Task Execute(IJobExecutionContext context)
        {
            Console.WriteLine("TRIGGER: " + context.Trigger.Key);
            return Task.CompletedTask;
        }
    }
}