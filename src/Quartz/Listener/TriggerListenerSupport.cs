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

namespace Quartz.Listener
{
    /// <summary>
    ///  A helpful abstract base class for implementors of 
    /// <see cref="ITriggerListener" />.
    ///  </summary>
    /// <remarks>
    /// <para>
    /// The methods in this class are empty so you only need to override the  
    /// subset for the <see cref="ITriggerListener" /> events
    /// you care about.
    /// </para>
    /// 
    /// <para>
    /// You are required to implement <see cref="ITriggerListener.Name" /> 
    /// to return the unique name of your <see cref="ITriggerListener" />.  
    /// </para>
    ///</remarks>
    /// <author>Marko Lahma (.NET)</author>
    /// <seealso cref="ITriggerListener" />
    public abstract class TriggerListenerSupport : ITriggerListener
    {
        private readonly ILog log;

        protected TriggerListenerSupport()
        {
            log = LogManager.GetLogger(GetType());
        }

        /// <summary>
        /// Get the <see cref="ILog" /> for this
        /// class's category.  This should be used by subclasses for logging.
        /// </summary>
        protected ILog Log
        {
            get { return log; }
        }

        /// <summary>
        /// Get the name of the <see cref="ITriggerListener"/>.
        /// </summary>
        /// <value></value>
        public abstract string Name { get; }

        public virtual void TriggerFired(ITrigger trigger, IJobExecutionContext context)
        {
        }

        public virtual bool VetoJobExecution(ITrigger trigger, IJobExecutionContext context)
        {
            return false;
        }

        public virtual void TriggerMisfired(ITrigger trigger)
        {
        }

        public virtual void TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode)
        {
        }

    }
}