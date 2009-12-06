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

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Quartz.Listener
{
    /// <summary>
    /// Holds a List of references to TriggerListener instances and broadcasts all
    /// events to them (in order) - if the event is not excluded via filtering
    /// (read on).
    /// </summary>
    ///<remarks>
    /// <p>
    /// The broadcasting behavior of this listener to delegate listeners may be
    /// more convenient than registering all of the listeners directly with the
    /// Trigger, and provides the flexibility of easily changing which listeners
    /// get notified.
    /// </p>
    ///
    /// <p>
    /// You may also register a number of Regular Expression patterns to match
    /// the events against. If one or more patterns are registered, the broadcast
    /// will only take place if the event applies to a trigger who's name/group
    /// matches one or more of the patterns.
    /// </p>
    ///</remarks>
    /// <seealso cref="AddListener" />
    /// <seealso cref="RemoveListener(ITriggerListener)" />
    /// <seealso cref="RemoveListener(string)" />
    /// <seealso cref="AddTriggerNamePattern" />
    /// <seealso cref="AddTriggerGroupPattern" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class FilterAndBroadcastTriggerListener : ITriggerListener
    {
        private readonly string name;
        private readonly List<ITriggerListener> listeners;
        private readonly List<string> namePatterns = new List<string>();
        private readonly List<string> groupPatterns = new List<string>();


        /// <summary>
        /// Construct an instance with the given name.
        ///
        /// (Remember to add some delegate listeners!)
        /// </summary>
        /// <param name="name">the name of this instance</param>
        public FilterAndBroadcastTriggerListener(string name)
        {
            if (name == null)
            {
                throw new ArgumentException("Listener name cannot be null!");
            }
            this.name = name;
            listeners = new List<ITriggerListener>();
        }

        /// <summary>
        /// Construct an instance with the given name, and List of listeners.
        /// </summary>
        /// <param name="name">the name of this instance</param>
        /// <param name="listeners">the initial List of TriggerListeners to broadcast to</param>
        public FilterAndBroadcastTriggerListener(string name, IEnumerable<ITriggerListener> listeners) : this(name)
        {
            this.listeners.AddRange(listeners);
        }

        public string Name
        {
            get { return name; }
        }

        public void AddListener(ITriggerListener listener)
        {
            listeners.Add(listener);
        }

        public bool RemoveListener(ITriggerListener listener)
        {
            if (listeners.Contains(listener))
            {
                listeners.Remove(listener);
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool RemoveListener(string listenerName)
        {
            for (int i = 0; i < listeners.Count; ++i)
            {
                ITriggerListener l = (ITriggerListener) listeners[i];
                if (l.Name.Equals(listenerName))
                {
                    listeners.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public IList<ITriggerListener> GetListeners()
        {
            return listeners.AsReadOnly();
        }

        /// <summary>
        /// If one or more name patterns are specified, only events relating to
        /// triggers who's name matches the given regular expression pattern
        /// will be dispatched to the delegate listeners.
        /// </summary>
        /// <param name="regularExpression"></param>
        public void AddTriggerNamePattern(string regularExpression)
        {
            if (regularExpression == null)
            {
                throw new ArgumentException("Expression cannot be null!");
            }

            namePatterns.Add(regularExpression);
        }

        public IList<string> TriggerNamePatterns
        {
            get { return namePatterns; }
        }

        /// <summary>
        /// If one or more group patterns are specified, only events relating to
        /// triggers who's group matches the given regular expression pattern
        /// will be dispatched to the delegate listeners.
        /// </summary>
        /// <param name="regularExpression"></param>
        public void AddTriggerGroupPattern(string regularExpression)
        {
            if (regularExpression == null)
            {
                throw new ArgumentException("Expression cannot be null!");
            }

            groupPatterns.Add(regularExpression);
        }

        public IList<string> TriggerGroupPatterns
        {
            get { return namePatterns; }
        }

        protected virtual bool ShouldDispatch(Trigger trigger)
        {
            if (namePatterns.Count == 0 && groupPatterns.Count == 0)
            {
                return true;
            }

            foreach (string pat in groupPatterns)
            {
                Regex rex = new Regex(pat);
                if (rex.IsMatch(trigger.Group))
                {
                    return true;
                }
            }

            foreach (string pat in namePatterns)
            {
                Regex rex = new Regex(pat);
                if (rex.IsMatch(trigger.Name))
                {
                    return true;
                }
            }

            return false;
        }

        public void TriggerFired(Trigger trigger, JobExecutionContext context)
        {
            if (!ShouldDispatch(trigger))
            {
                return;
            }

            foreach (ITriggerListener l in listeners)
            {
                l.TriggerFired(trigger, context);
            }
        }

        public bool VetoJobExecution(Trigger trigger, JobExecutionContext context)
        {
            if (!ShouldDispatch(trigger))
            {
                return false;
            }

            foreach (ITriggerListener l in listeners)
            {
                if (l.VetoJobExecution(trigger, context))
                {
                    return true;
                }
            }
            return false;
        }

        public void TriggerMisfired(Trigger trigger)
        {
            if (!ShouldDispatch(trigger))
            {
                return;
            }

            foreach (ITriggerListener l in listeners)
            {
                l.TriggerMisfired(trigger);
            }
        }

        public void TriggerComplete(Trigger trigger, JobExecutionContext context, SchedulerInstruction triggerInstructionCode)
        {
            if (!ShouldDispatch(trigger))
            {
                return;
            }

            foreach (ITriggerListener l in listeners)
            {
                l.TriggerComplete(trigger, context, triggerInstructionCode);
            }
        }
    }
}
