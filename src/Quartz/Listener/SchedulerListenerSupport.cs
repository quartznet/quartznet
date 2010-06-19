#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
    /// <see cref="ISchedulerListener" />.
    /// </summary>
    /// <remarks>
    /// The methods in this class are empty so you only need to override the  
    /// subset for the <see cref="ISchedulerListener" /> events you care about.
    /// </remarks>
    /// <author>Marko Lahma (.NET)</author>
    /// <seealso cref="ISchedulerListener" />
    public abstract class SchedulerListenerSupport : ISchedulerListener
    {
        private readonly ILog log;

        protected SchedulerListenerSupport()
        {
            log = LogManager.GetLogger(GetType());
        }

        /// <summary>
        /// Get the <see cref="ILog" /> for this
        /// type's category.  This should be used by subclasses for logging.
        /// </summary>
        protected ILog Log
        {
            get { return log; }
        }

        public virtual void JobScheduled(Trigger trigger)
        {
        }

        public virtual void JobUnscheduled(string triggerName, string triggerGroup)
        {
        }

        public virtual void TriggerFinalized(Trigger trigger)
        {
        }

        public virtual void TriggersPaused(string triggerName, string triggerGroup)
        {
        }

        public virtual void TriggersResumed(string triggerName, string triggerGroup)
        {
        }

        public virtual void JobAdded(JobDetail jobDetail)
        {
        }

        public virtual void JobDeleted(string jobName, string groupName)
        {
        }

        public virtual void JobsPaused(string jobName, string jobGroup)
        {
        }

        public virtual void JobsResumed(string jobName, string jobGroup)
        {
        }

        public virtual void SchedulerError(string msg, SchedulerException cause)
        {
        }

        public virtual void SchedulerInStandbyMode()
        {
        }

        public virtual void SchedulerStarted()
        {
        }

        public virtual void SchedulerShutdown()
        {
        }

        public virtual void SchedulerShuttingdown()
        {
        }
    }
}