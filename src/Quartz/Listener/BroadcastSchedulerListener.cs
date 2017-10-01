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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Listener
{
    /// <summary>
    /// Holds a List of references to SchedulerListener instances and broadcasts all
    ///  events to them (in order).
    ///</summary>
    /// <remarks>
    /// This may be more convenient than registering all of the listeners
    /// directly with the Scheduler, and provides the flexibility of easily changing
    /// which listeners get notified.
    /// </remarks>
    /// <see cref="AddListener" />
    /// <see cref="RemoveListener" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class BroadcastSchedulerListener : ISchedulerListener
    {
        private readonly List<ISchedulerListener> listeners;

        public BroadcastSchedulerListener()
        {
            listeners = new List<ISchedulerListener>();
        }

        /// <summary>
        /// Construct an instance with the given List of listeners.
        /// </summary>
        /// <param name="listeners">The initial List of SchedulerListeners to broadcast to.</param>
        public BroadcastSchedulerListener(IEnumerable<ISchedulerListener> listeners)
        {
            this.listeners.AddRange(listeners);
        }

        public void AddListener(ISchedulerListener listener)
        {
            listeners.Add(listener);
        }

        public bool RemoveListener(ISchedulerListener listener)
        {
            return listeners.Remove(listener);
        }

        public IReadOnlyList<ISchedulerListener> GetListeners()
        {
            return listeners;
        }

        public Task JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobAdded(jobDetail, cancellationToken)));
        }

        public Task JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobDeleted(jobKey, cancellationToken)));
        }

        public Task JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobScheduled(trigger, cancellationToken)));
        }

        public Task JobUnscheduled(
            TriggerKey triggerKey, 
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobUnscheduled(triggerKey, cancellationToken)));
        }

        public Task TriggerFinalized(
            ITrigger trigger, 
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerFinalized(trigger, cancellationToken)));
        }

        public Task TriggersPaused(
            string triggerGroup,
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggersPaused(triggerGroup, cancellationToken)));
        }

        public Task TriggerPaused(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerPaused(triggerKey, cancellationToken)));
        }

        public Task TriggersResumed(
            string triggerGroup,
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggersResumed(triggerGroup, cancellationToken)));
        }

        public Task SchedulingDataCleared(CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulingDataCleared(cancellationToken)));
        }

        public Task TriggerResumed(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerResumed(triggerKey, cancellationToken)));
        }

        public Task JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.WhenAll(listeners.Select(l => l.JobInterrupted(jobKey, cancellationToken)));
        }

        public Task JobsPaused(
            string jobGroup,
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobsPaused(jobGroup, cancellationToken)));
        }

        public Task JobPaused(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobPaused(jobKey, cancellationToken)));
        }

        public Task JobsResumed(string jobGroup, CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobsResumed(jobGroup, cancellationToken)));
        }

        public Task JobResumed(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.JobResumed(jobKey, cancellationToken)));
        }

        public Task SchedulerError(
            string msg, 
            SchedulerException cause, 
            CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerError(msg, cause, cancellationToken)));
        }

        public Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerStarted(cancellationToken)));
        }

        public Task SchedulerStarting(CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerStarting(cancellationToken)));
        }

        public Task SchedulerInStandbyMode(CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerInStandbyMode(cancellationToken)));
        }

        public Task SchedulerShutdown(CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerShutdown(cancellationToken)));
        }

        public Task SchedulerShuttingdown(CancellationToken cancellationToken = default)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerShuttingdown(cancellationToken)));
        }
    }
}