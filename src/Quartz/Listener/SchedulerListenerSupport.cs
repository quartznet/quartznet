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

using System.Threading;
using System.Threading.Tasks;

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
        public virtual Task JobScheduled(
            ITrigger trigger, 
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobUnscheduled(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerFinalized(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggersPaused(
            string triggerGroup,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerPaused(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggersResumed(
            string triggerGroup,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task TriggerResumed(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobAdded(
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobDeleted(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobInterrupted(
            JobKey jobKey,
            CancellationToken cancellationToken = new CancellationToken())
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobsPaused(
            string jobGroup,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobPaused(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobsResumed(
            string jobGroup,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobResumed(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerError(
            string msg, 
            SchedulerException cause,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerInStandbyMode(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerStarting(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerShutdown(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulerShuttingdown(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        public virtual Task SchedulingDataCleared(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }
    }
}