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

        public virtual Task JobScheduledAsync(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobUnscheduledAsync(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerFinalizedAsync(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggersPausedAsync(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerPausedAsync(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggersResumedAsync(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerResumedAsync(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobAddedAsync(IJobDetail jobDetail)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobDeletedAsync(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobsPausedAsync(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobPausedAsync(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobsResumedAsync(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobResumedAsync(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerErrorAsync(string msg, SchedulerException cause)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerInStandbyModeAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerStartedAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerStartingAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerShutdownAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerShuttingdownAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulingDataClearedAsync()
        {
            return TaskUtil.CompletedTask;
        }
    }
}