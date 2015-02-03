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

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Listener
{
    /// <summary>
    /// A helpful abstract base class for implementors of <see cref="IJobListener" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The methods in this class are empty so you only need to override the  
    /// subset for the <see cref="IJobListener" /> events you care about.
    /// </para>
    /// 
    /// <para>
    /// You are required to implement <see cref="IJobListener.Name" /> 
    /// to return the unique name of your <see cref="IJobListener" />.  
    /// </para>
    /// </remarks>
    /// <author>Marko Lahma (.NET)</author>
    /// <seealso cref="IJobListener" />
    public abstract class JobListenerSupport : IJobListener
    {
        private readonly ILog log;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobListenerSupport"/> class.
        /// </summary>
        protected JobListenerSupport()
        {
            log = LogManager.GetLogger(GetType());
        }

        /// <summary>
        /// Get the <see cref="ILog" /> for this  class's category.  
        /// This should be used by subclasses for logging.
        /// </summary>
        protected ILog Log
        {
            get { return log; }
        }

        /// <summary>
        /// Get the name of the <see cref="IJobListener"/>.
        /// </summary>
        /// <value></value>
        public abstract string Name { get; }

        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/>
        /// is about to be executed (an associated <see cref="ITrigger"/>
        /// has occurred).
        /// <para>
        /// This method will not be invoked if the execution of the Job was vetoed
        /// by a <see cref="ITriggerListener"/>.
        /// </para>
        /// </summary>
        /// <param name="context"></param>
        /// <seealso cref="JobExecutionVetoed(IJobExecutionContext)"/>
        public virtual void JobToBeExecuted(IJobExecutionContext context)
        {
        }

        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/>
        /// was about to be executed (an associated <see cref="ITrigger"/>
        /// has occurred), but a <see cref="ITriggerListener"/> vetoed it's
        /// execution.
        /// </summary>
        /// <param name="context"></param>
        /// <seealso cref="JobToBeExecuted(IJobExecutionContext)"/>
        public virtual void JobExecutionVetoed(IJobExecutionContext context)
        {
        }

        /// <summary>
        /// Called by the <see cref="IScheduler"/> after a <see cref="IJobDetail"/>
        /// has been executed, and be for the associated <see cref="ITrigger"/>'s
        /// <see cref="IOperableTrigger.Triggered"/> method has been called.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="jobException"></param>
        public virtual void JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException)
        {
        }
    }
}