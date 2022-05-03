using System;
using System.Collections.Generic;

using Quartz.Collections;
using Quartz.Impl.Matchers;

namespace Quartz.Core
{
    /// <summary>
    /// Default concrete implementation of <see cref="IListenerManager" />.
    /// </summary>
    public class ListenerManagerImpl : IListenerManager
    {
        private readonly object globalJobListenerLock = new object();

        private OrderedDictionary<string, IJobListener>? globalJobListeners;

        private readonly OrderedDictionary<string, ITriggerListener> globalTriggerListeners = new OrderedDictionary<string, ITriggerListener>(10);

        private Dictionary<string, List<IMatcher<JobKey>>>? globalJobListenersMatchers;

        private readonly Dictionary<string, List<IMatcher<TriggerKey>>> globalTriggerListenersMatchers = new Dictionary<string, List<IMatcher<TriggerKey>>>(10);

        private readonly List<ISchedulerListener> schedulerListeners = new List<ISchedulerListener>(10);

        public void AddJobListener(IJobListener jobListener, params IMatcher<JobKey>[] matchers)
        {
            IReadOnlyCollection<IMatcher<JobKey>> matchersCollection = matchers;

            AddJobListener(jobListener, matchersCollection);
        }

        public void AddJobListener(IJobListener jobListener, IReadOnlyCollection<IMatcher<JobKey>> matchers)
        {
            if (string.IsNullOrEmpty(jobListener.Name))
            {
                throw new ArgumentException("JobListener name cannot be empty.");
            }

            lock (globalJobListenerLock)
            {
                globalJobListeners ??= new OrderedDictionary<string, IJobListener>();
                globalJobListeners[jobListener.Name] = jobListener;

                if (matchers != null && matchers.Count > 0)
                {
                    // Add or replace matchers for the job listener
                    globalJobListenersMatchers ??= new Dictionary<string, List<IMatcher<JobKey>>>();
                    globalJobListenersMatchers[jobListener.Name] = new List<IMatcher<JobKey>>(matchers);
                }
                else
                {
                    // Remove any registered matchers for the job listener
                    RemoveJobListenerMatchers(jobListener.Name);
                }
            }
        }

        public bool AddJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher)
        {
            if (listenerName == null)
            {
                throw new ArgumentNullException(nameof(listenerName));
            }

            if (matcher == null)
            {
                throw new ArgumentNullException(nameof(matcher));
            }

            lock (globalJobListenerLock)
            {
                if (globalJobListenersMatchers == null || !globalJobListenersMatchers.TryGetValue(listenerName, out var matchers))
                {
                    // Return false if no job listener is registered with the specified name
                    if (globalJobListeners == null || !globalJobListeners.ContainsKey(listenerName))
                    {
                        return false;
                    }

                    // We may be adding the first matcher for any job listener, so make sure globalJobListenersMatchers
                    // is initialized
                    globalJobListenersMatchers ??= new Dictionary<string, List<IMatcher<JobKey>>>();

                    // We're adding the first matcher for the specified job listener
                    matchers = new List<IMatcher<JobKey>>();
                    globalJobListenersMatchers.Add(listenerName, matchers);
                }

                matchers.Add(matcher);
                return true;
            }
        }

        public bool RemoveJobListenerMatcher(string listenerName, IMatcher<JobKey> matcher)
        {
            if (listenerName == null)
            {
                throw new ArgumentNullException(nameof(listenerName));
            }

            if (matcher == null)
            {
                throw new ArgumentNullException(nameof(matcher));
            }

            if (globalJobListenersMatchers == null)
            {
                return false;
            }

            lock (globalJobListenerLock)
            {
                if (globalJobListenersMatchers == null || !globalJobListenersMatchers.TryGetValue(listenerName, out var matchers))
                {
                    return false;
                }

                var removed = matchers.Remove(matcher);

                if (removed && matchers.Count == 0)
                {
                    RemoveJobListenerMatchers(listenerName);
                }

                return removed;
            }
        }

        public IReadOnlyCollection<IMatcher<JobKey>>? GetJobListenerMatchers(string listenerName)
        {
            if (listenerName == null)
            {
                throw new ArgumentNullException(nameof(listenerName));
            }

            if (globalJobListenersMatchers == null)
            {
                return null;
            }

            lock (globalJobListenerLock)
            {
                if (globalJobListenersMatchers == null || !globalJobListenersMatchers.TryGetValue(listenerName, out var matchers))
                {
                    return null;
                }

                return matchers.AsReadOnly();
            }
        }

        public bool SetJobListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<JobKey>> matchers)
        {
            if (listenerName == null)
            {
                throw new ArgumentNullException(nameof(listenerName));
            }

            if (matchers == null)
            {
                throw new ArgumentNullException(nameof(matchers));
            }

            lock (globalJobListenerLock)
            {
                if (globalJobListeners == null || !globalJobListeners.ContainsKey(listenerName))
                {
                    return false;
                }

                if (matchers.Count == 0)
                {
                    RemoveJobListenerMatchers(listenerName);
                }
                else
                {
                    globalJobListenersMatchers ??= new Dictionary<string, List<IMatcher<JobKey>>>();
                    globalJobListenersMatchers[listenerName] = new List<IMatcher<JobKey>>(matchers);
                }

                return true;
            }
        }

        public bool RemoveJobListener(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (globalJobListeners == null)
            {
                return false;
            }

            lock (globalJobListenerLock)
            {
                if (globalJobListeners == null)
                {
                    return false;
                }

                var removed = globalJobListeners.Remove(name);

                // When we've removed a job listener, make sure to also remove associated matchers
                if (removed)
                {
                    RemoveJobListenerMatchers(name);

                    if (globalJobListeners.Count == 0)
                    {
                        globalJobListeners = null;
                    }
                }

                return removed;
            }
        }

        public IJobListener[] GetJobListeners()
        {
            if (globalJobListeners == null)
            {
                return Array.Empty<IJobListener>();
            }

            lock (globalJobListenerLock)
            {
                return globalJobListeners != null ? globalJobListeners.Values.ToArray()
                                                  : Array.Empty<IJobListener>();
            }
        }

        public IJobListener GetJobListener(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            lock (globalJobListenerLock)
            {
                // Avoid initializing globalJobListeners when no job listeners have been added
                if (globalJobListeners == null || !globalJobListeners.TryGetValue(name, out var jobListener))
                {
                    throw new KeyNotFoundException();
                }

                return jobListener;
            }
        }

        public void AddTriggerListener(ITriggerListener triggerListener, params IMatcher<TriggerKey>[] matchers)
        {
            AddTriggerListener(triggerListener, new List<IMatcher<TriggerKey>>(matchers));
        }

        public void AddTriggerListener(ITriggerListener triggerListener, IReadOnlyCollection<IMatcher<TriggerKey>> matchers)
        {
            if (string.IsNullOrEmpty(triggerListener.Name))
            {
                throw new ArgumentException("TriggerListener name cannot be empty.");
            }

            lock (globalTriggerListeners)
            {
                globalTriggerListeners[triggerListener.Name] = triggerListener;

                List<IMatcher<TriggerKey>> matchersL = new List<IMatcher<TriggerKey>>();
                if (matchers != null && matchers.Count > 0)
                {
                    matchersL.AddRange(matchers);
                }
                else
                {
                    matchersL.Add(EverythingMatcher<TriggerKey>.AllTriggers());
                }

                globalTriggerListenersMatchers[triggerListener.Name] = matchersL;
            }
        }

        public void AddTriggerListener(ITriggerListener triggerListener, IMatcher<TriggerKey> matcher)
        {
            if (matcher == null)
            {
                throw new ArgumentException("Non-null value not acceptable for matcher.");
            }

            if (string.IsNullOrEmpty(triggerListener.Name))
            {
                throw new ArgumentException("TriggerListener name cannot be empty.");
            }

            lock (globalTriggerListeners)
            {
                globalTriggerListeners[triggerListener.Name] = triggerListener;
                var matchers = new List<IMatcher<TriggerKey>> { matcher };
                globalTriggerListenersMatchers[triggerListener.Name] = matchers;
            }
        }

        public bool AddTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher)
        {
            if (matcher == null)
            {
                throw new ArgumentException("Non-null value not acceptable.");
            }

            lock (globalTriggerListeners)
            {
                if (!globalTriggerListenersMatchers.TryGetValue(listenerName, out var matchers))
                {
                    return false;
                }

                matchers.Add(matcher);
                return true;
            }
        }

        public bool RemoveTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher)
        {
            if (matcher == null)
            {
                throw new ArgumentException("Non-null value not acceptable.");
            }

            lock (globalTriggerListeners)
            {
                if (!globalTriggerListenersMatchers.TryGetValue(listenerName, out var matchers))
                {
                    return false;
                }

                return matchers.Remove(matcher);
            }
        }

        public IReadOnlyCollection<IMatcher<TriggerKey>>? GetTriggerListenerMatchers(string listenerName)
        {
            lock (globalTriggerListeners)
            {
                globalTriggerListenersMatchers.TryGetValue(listenerName, out var matchers);
                return matchers;
            }
        }

        public bool SetTriggerListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<TriggerKey>> matchers)
        {
            if (matchers == null)
            {
                throw new ArgumentException("Non-null value not acceptable.");
            }

            lock (globalTriggerListeners)
            {
                if (!globalTriggerListenersMatchers.TryGetValue(listenerName, out _))
                {
                    return false;
                }

                globalTriggerListenersMatchers[listenerName] = new List<IMatcher<TriggerKey>>(matchers);
                return true;
            }
        }

        public bool RemoveTriggerListener(string name)
        {
            lock (globalTriggerListeners)
            {
                return globalTriggerListeners.Remove(name);
            }
        }

        public IReadOnlyCollection<ITriggerListener> GetTriggerListeners()
        {
            lock (globalTriggerListeners)
            {
                return globalTriggerListeners.Count > 0
                    ? new List<ITriggerListener>(globalTriggerListeners.Values)
                    : EmptyReadOnlyCollection<ITriggerListener>.Instance;
            }
        }

        public ITriggerListener GetTriggerListener(string name)
        {
            lock (globalTriggerListeners)
            {
                return globalTriggerListeners[name];
            }
        }

        public void AddSchedulerListener(ISchedulerListener schedulerListener)
        {
            lock (schedulerListeners)
            {
                schedulerListeners.Add(schedulerListener);
            }
        }

        public bool RemoveSchedulerListener(ISchedulerListener schedulerListener)
        {
            lock (schedulerListeners)
            {
                return schedulerListeners.Remove(schedulerListener);
            }
        }

        public IReadOnlyCollection<ISchedulerListener> GetSchedulerListeners()
        {
            lock (schedulerListeners)
            {
                return schedulerListeners.Count > 0
                    ? new List<ISchedulerListener>(schedulerListeners)
                    : EmptyReadOnlyCollection<ISchedulerListener>.Instance;
            }
        }

        private void RemoveJobListenerMatchers(string listenerName)
        {
            if (globalJobListenersMatchers == null)
            {
                return;
            }

            // If we're removing the last matcher of the only job listener with matchers, then
            // reset globalJobListenersMatchers to null to avoid having to lock in subsequent calls
            // to GetJobListenerMatchers(string listenerName)
            if (globalJobListenersMatchers.Remove(listenerName) && globalJobListenersMatchers.Count == 0)
            {
                globalJobListenersMatchers = null;
            }
        }
    }
}