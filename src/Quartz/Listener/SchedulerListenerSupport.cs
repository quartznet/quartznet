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

using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Util;

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
            log = LogProvider.GetLogger(GetType());
        }

        /// <summary>
        /// Get the <see cref="ILog" /> for this
        /// type's category.  This should be used by subclasses for logging.
        /// </summary>
        protected ILog Log
        {
            get { return log; }
        }

        public virtual Task JobScheduled(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobUnscheduled(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerFinalized(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggersPaused(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerPaused(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggersResumed(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerResumed(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobAdded(IJobDetail jobDetail)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobDeleted(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobsPaused(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobPaused(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobsResumed(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobResumed(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerError(string msg, SchedulerException cause)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerInStandbyMode()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerStarted()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerStarting()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerShutdown()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerShuttingdown()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulingDataCleared()
        {
            return TaskUtil.CompletedTask;
        }
    }
}