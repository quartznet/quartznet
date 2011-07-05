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

using System;
using System.Collections.Generic;

namespace Quartz.Listener
{
    /// <summary>
    /// Holds a List of references to JobListener instances and broadcasts all
    /// events to them (in order).
    /// </summary>
    /// <remarks>
    /// <para>The broadcasting behavior of this listener to delegate listeners may be
    /// more convenient than registering all of the listeners directly with the
    /// Scheduler, and provides the flexibility of easily changing which listeners
    /// get notified.</para>
    /// </remarks>
    /// <seealso cref="AddListener(IJobListener)" />
    /// <seealso cref="RemoveListener(IJobListener)" />
    /// <seealso cref="RemoveListener(string)" />
    /// <author>James House (jhouse AT revolition DOT net)</author>
    public class BroadcastJobListener : IJobListener
    {
        private readonly string name;
        private readonly List<IJobListener> listeners;

        /// <summary>
        /// Construct an instance with the given name.
        /// </summary>
        /// <remarks>
        /// (Remember to add some delegate listeners!)
        /// </remarks>
        /// <param name="name">the name of this instance</param>
        public BroadcastJobListener(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name", "Listener name cannot be null!");
            }
            this.name = name;
            listeners = new List<IJobListener>();
        }

        /// <summary>
        /// Construct an instance with the given name, and List of listeners.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="name">the name of this instance</param>
        /// <param name="listeners">the initial List of JobListeners to broadcast to.</param>
        public BroadcastJobListener(string name, List<IJobListener> listeners) : this(name)
        {
            this.listeners.AddRange(listeners);
        }

        public string Name
        {
            get { return name; }
        }

        public void AddListener(IJobListener listener)
        {
            listeners.Add(listener);
        }

        public bool RemoveListener(IJobListener listener)
        {
            return listeners.Remove(listener);
        }

        public bool RemoveListener(string listenerName)
        {
            IJobListener listener = listeners.Find(x => x.Name == listenerName);
            if (listener != null)
            {
                listeners.Remove(listener);
                return true;
            }
            return false;
        }

        public IList<IJobListener> Listeners
        {
            get { return listeners.AsReadOnly(); }
        }

        public void JobToBeExecuted(IJobExecutionContext context)
        {
            foreach (IJobListener jl in listeners)
            {
                jl.JobToBeExecuted(context);
            }
        }

        public void JobExecutionVetoed(IJobExecutionContext context)
        {
            foreach (IJobListener jl in listeners)
            {
                jl.JobExecutionVetoed(context);
            }
        }

        public void JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException)
        {
            foreach (IJobListener jl in listeners)
            {
                jl.JobWasExecuted(context, jobException);
            }
        }
    }
}