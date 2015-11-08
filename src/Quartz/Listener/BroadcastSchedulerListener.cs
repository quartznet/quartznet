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

using System.Collections.Generic;
using System.Linq;
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

        public Task JobAddedAsync(IJobDetail jobDetail)
        {
            return Task.WhenAll(listeners.Select(l => l.JobAddedAsync(jobDetail)));
        }

        public Task JobDeletedAsync(JobKey jobKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobDeletedAsync(jobKey)));
        }

        public Task JobScheduledAsync(ITrigger trigger)
        {
            return Task.WhenAll(listeners.Select(l => l.JobScheduledAsync(trigger)));
        }

        public Task JobUnscheduledAsync(TriggerKey triggerKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobUnscheduledAsync(triggerKey)));
        }

        public Task TriggerFinalizedAsync(ITrigger trigger)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerFinalizedAsync(trigger)));
        }

        public Task TriggersPausedAsync(string triggerGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggersPausedAsync(triggerGroup)));
        }

        public Task TriggerPausedAsync(TriggerKey triggerKey)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerPausedAsync(triggerKey)));
        }

        public Task TriggersResumedAsync(string triggerGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggersResumedAsync(triggerGroup)));
        }

        public Task SchedulingDataClearedAsync()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulingDataClearedAsync()));
        }

        public Task TriggerResumedAsync(TriggerKey triggerKey)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerResumedAsync(triggerKey)));
        }

        public Task JobsPausedAsync(string jobGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.JobsPausedAsync(jobGroup)));
        }

        public Task JobPausedAsync(JobKey jobKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobPausedAsync(jobKey)));
        }

        public Task JobsResumedAsync(string jobGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.JobsResumedAsync(jobGroup)));
        }

        public Task JobResumedAsync(JobKey jobKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobResumedAsync(jobKey)));
        }

        public Task SchedulerErrorAsync(string msg, SchedulerException cause)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerErrorAsync(msg, cause)));
        }

        public Task SchedulerStartedAsync()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerStartedAsync()));
        }

        public Task SchedulerStartingAsync()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerStartingAsync()));
        }

        public Task SchedulerInStandbyModeAsync()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerInStandbyModeAsync()));
        }

        public Task SchedulerShutdownAsync()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerShutdownAsync()));
        }

        public Task SchedulerShuttingdownAsync()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerShuttingdownAsync()));
        }
    }
}