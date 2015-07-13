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

        public Task JobAdded(IJobDetail jobDetail)
        {
            return Task.WhenAll(listeners.Select(l => l.JobAdded(jobDetail)));
        }

        public Task JobDeleted(JobKey jobKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobDeleted(jobKey)));
        }

        public Task JobScheduled(ITrigger trigger)
        {
            return Task.WhenAll(listeners.Select(l => l.JobScheduled(trigger)));
        }

        public Task JobUnscheduled(TriggerKey triggerKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobUnscheduled(triggerKey)));
        }

        public Task TriggerFinalized(ITrigger trigger)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerFinalized(trigger)));
        }

        public Task TriggersPaused(string triggerGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggersPaused(triggerGroup)));
        }

        public Task TriggerPaused(TriggerKey triggerKey)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerPaused(triggerKey)));
        }

        public Task TriggersResumed(string triggerGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggersResumed(triggerGroup)));
        }

        public Task SchedulingDataCleared()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulingDataCleared()));
        }

        public Task TriggerResumed(TriggerKey triggerKey)
        {
            return Task.WhenAll(listeners.Select(l => l.TriggerResumed(triggerKey)));
        }

        public Task JobsPaused(string jobGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.JobsPaused(jobGroup)));
        }

        public Task JobPaused(JobKey jobKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobPaused(jobKey)));
        }

        public Task JobsResumed(string jobGroup)
        {
            return Task.WhenAll(listeners.Select(l => l.JobsResumed(jobGroup)));
        }

        public Task JobResumed(JobKey jobKey)
        {
            return Task.WhenAll(listeners.Select(l => l.JobResumed(jobKey)));
        }

        public Task SchedulerError(string msg, SchedulerException cause)
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerError(msg, cause)));
        }

        public Task SchedulerStarted()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerStarted()));
        }

        public Task SchedulerStarting()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerStarting()));
        }

        public Task SchedulerInStandbyMode()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerInStandbyMode()));
        }

        public Task SchedulerShutdown()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerShutdown()));
        }

        public Task SchedulerShuttingdown()
        {
            return Task.WhenAll(listeners.Select(l => l.SchedulerShuttingdown()));
        }
    }
}