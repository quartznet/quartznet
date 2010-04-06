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


        public void JobAdded(JobDetail jobDetail)
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.JobAdded(jobDetail);
            }
        }

        public void JobDeleted(string jobName, string groupName)
        {
            foreach (ISchedulerListener listener in listeners)
            {
                listener.JobDeleted(jobName, groupName);
            }
        }

        public void JobScheduled(Trigger trigger)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobScheduled(trigger);
            }
        }

        public void JobUnscheduled(string triggerName, string triggerGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobUnscheduled(triggerName, triggerGroup);
            }
        }

        public void TriggerFinalized(Trigger trigger)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggerFinalized(trigger);
            }
        }

        public void TriggersPaused(string triggerName, string triggerGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggersPaused(triggerName, triggerGroup);
            }
        }

        public void TriggersResumed(string triggerName, string triggerGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.TriggersResumed(triggerName, triggerGroup);
            }
        }

        public void JobsPaused(string jobName, string jobGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobsPaused(jobName, jobGroup);
            }
        }

        public void JobsResumed(string jobName, string jobGroup)
        {
            foreach (ISchedulerListener l in listeners)
            {
                l.JobsResumed(jobName, jobGroup);
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
    }
}