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

        public IList<ISchedulerListener> GetListeners()
        {
            return listeners.AsReadOnly();
        }
        
        public void JobAdded(IJobDetail jobDetail)
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.JobAdded(jobDetail);
            }
        }

        public void JobDeleted(JobKey jobKey)
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.JobDeleted(jobKey);
            }
        }

        public void JobScheduled(ITrigger trigger)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobScheduled(trigger);
            }
        }

        public void JobUnscheduled(TriggerKey triggerKey)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobUnscheduled(triggerKey);
            }
        }

        public void TriggerFinalized(ITrigger trigger)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggerFinalized(trigger);
            }
        }

        public void TriggersPaused(string triggerGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggersPaused(triggerGroup);
            }
        }

        public void TriggerPaused(TriggerKey triggerKey)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggerPaused(triggerKey);
            }
        }

        public void TriggersResumed(string triggerGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggersResumed(triggerGroup);
            }
        }

        public void SchedulingDataCleared()
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.SchedulingDataCleared();
            }
        }

        public void TriggerResumed(TriggerKey triggerKey)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggerResumed(triggerKey);
            }
        }

        public void JobsPaused(string jobGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobsPaused(jobGroup);
            }
        }

        public void JobPaused(JobKey jobKey)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobPaused(jobKey);
            }
        }

        public void JobsResumed(string jobGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobsResumed(jobGroup);
            }
        }

        public void JobResumed(JobKey jobKey)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobResumed(jobKey);
            }
        }

        public void SchedulerError(string msg, SchedulerException cause)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.SchedulerError(msg, cause);
            }
        }

        public void SchedulerStarted()
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.SchedulerStarted();
            }
        }

        public void SchedulerStarting()
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.SchedulerStarting();
            }
        }

        public void SchedulerInStandbyMode()
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.SchedulerInStandbyMode();
            }
        }

        public void SchedulerShutdown()
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.SchedulerShutdown();
            }
        }

        public void SchedulerShuttingdown()
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.SchedulerShuttingdown();
            }
        }
    }
}