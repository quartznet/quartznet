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
        public virtual ValueTask JobScheduled(
            ITrigger trigger, 
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask JobUnscheduled(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask TriggerFinalized(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask TriggersPaused(
            string? triggerGroup,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask TriggerPaused(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask TriggersResumed(
            string? triggerGroup,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask TriggerResumed(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask JobAdded(
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask JobDeleted(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask JobInterrupted(
            JobKey jobKey,
            CancellationToken cancellationToken = new CancellationToken())
        {
            return default;
        }

        public virtual ValueTask JobsPaused(
            string jobGroup,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask JobPaused(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask JobsResumed(
            string jobGroup,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask JobResumed(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask SchedulerError(
            string msg, 
            SchedulerException cause,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask SchedulerStarting(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask SchedulerShutdown(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public virtual ValueTask SchedulingDataCleared(CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}