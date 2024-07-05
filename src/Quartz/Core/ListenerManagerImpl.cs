using Quartz.Collections;

namespace Quartz.Core;

/// <summary>
/// Default concrete implementation of <see cref="IListenerManager" />.
/// </summary>
internal sealed class ListenerManagerImpl : IListenerManager
{
    private readonly object globalJobListenerLock = new object();
    private OrderedDictionary<string, IJobListener>? globalJobListeners;
    private Dictionary<string, List<IMatcher<JobKey>>>? globalJobListenersMatchers;

    private readonly object globalTriggerListenerLock = new object();
    private OrderedDictionary<string, ITriggerListener>? globalTriggerListeners;
    private Dictionary<string, List<IMatcher<TriggerKey>>>? globalTriggerListenersMatchers;

    private readonly List<ISchedulerListener> schedulerListeners = new List<ISchedulerListener>(10);

    public void AddJobListener(IJobListener jobListener, params IMatcher<JobKey>[] matchers)
    {
        IReadOnlyCollection<IMatcher<JobKey>> matchersCollection = matchers;

        AddJobListener(jobListener, matchersCollection);
    }

    public void AddJobListener(IJobListener jobListener, IReadOnlyCollection<IMatcher<JobKey>> matchers)
    {
        if (jobListener is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(jobListener));
        }

        if (string.IsNullOrEmpty(jobListener.Name))
        {
            ThrowHelper.ThrowArgumentException($"{nameof(jobListener.Name)} cannot be null or empty.", nameof(jobListener));
        }

        lock (globalJobListenerLock)
        {
            // Add or replace the job listener
            globalJobListeners ??= new OrderedDictionary<string, IJobListener>();
            globalJobListeners[jobListener.Name] = jobListener;

            if (matchers is not null && matchers.Count > 0)
            {
                // Add or replace the matchers for the job listener
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
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(matcher));
        }

        lock (globalJobListenerLock)
        {
            if (globalJobListenersMatchers is null || !globalJobListenersMatchers.TryGetValue(listenerName, out var matchers))
            {
                // Return false if no job listener is registered with the specified name
                if (globalJobListeners is null || !globalJobListeners.ContainsKey(listenerName))
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
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(matcher));
        }

        if (globalJobListenersMatchers is null)
        {
            return false;
        }

        lock (globalJobListenerLock)
        {
            if (globalJobListenersMatchers is null || !globalJobListenersMatchers.TryGetValue(listenerName, out var matchers))
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
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (globalJobListenersMatchers is null)
        {
            return null;
        }

        lock (globalJobListenerLock)
        {
            if (globalJobListenersMatchers is null || !globalJobListenersMatchers.TryGetValue(listenerName, out var matchers))
            {
                return null;
            }

            return matchers.AsReadOnly();
        }
    }

    public bool SetJobListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<JobKey>> matchers)
    {
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (matchers is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(matchers));
        }

        lock (globalJobListenerLock)
        {
            if (globalJobListeners is null || !globalJobListeners.ContainsKey(listenerName))
            {
                return false;
            }

            if (matchers.Count == 0)
            {
                RemoveJobListenerMatchers(listenerName);
            }
            else
            {
                // Add or replace the matchers for the job listener
                globalJobListenersMatchers ??= new Dictionary<string, List<IMatcher<JobKey>>>();
                globalJobListenersMatchers[listenerName] = new List<IMatcher<JobKey>>(matchers);
            }

            return true;
        }
    }

    public bool RemoveJobListener(string name)
    {
        if (name is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(name));
        }

        if (globalJobListeners is null)
        {
            return false;
        }

        lock (globalJobListenerLock)
        {
            if (globalJobListeners is null)
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
        if (globalJobListeners is null)
        {
            return Array.Empty<IJobListener>();
        }

        lock (globalJobListenerLock)
        {
            return globalJobListeners is not null ? globalJobListeners.Values.ToArray()
                : Array.Empty<IJobListener>();
        }
    }

    public IJobListener GetJobListener(string name)
    {
        if (name is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(name));
        }

        lock (globalJobListenerLock)
        {
            // Avoid initializing globalJobListeners when no job listeners have been added
            if (globalJobListeners is null || !globalJobListeners.TryGetValue(name, out var jobListener))
            {
                ThrowHelper.ThrowKeyNotFoundException();
                return default;
            }

            return jobListener;
        }
    }

    public void AddTriggerListener(ITriggerListener triggerListener, params IMatcher<TriggerKey>[] matchers)
    {
        IReadOnlyCollection<IMatcher<TriggerKey>> matchersCollection = matchers;

        AddTriggerListener(triggerListener, matchersCollection);
    }

    public void AddTriggerListener(ITriggerListener triggerListener, IReadOnlyCollection<IMatcher<TriggerKey>> matchers)
    {
        if (triggerListener is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(triggerListener));
        }

        if (string.IsNullOrEmpty(triggerListener.Name))
        {
            ThrowHelper.ThrowArgumentException($"{nameof(triggerListener.Name)} cannot be empty.", nameof(triggerListener));
        }

        lock (globalTriggerListenerLock)
        {
            // Add or replace the trigger listener
            globalTriggerListeners ??= new OrderedDictionary<string, ITriggerListener>();
            globalTriggerListeners[triggerListener.Name] = triggerListener;

            if (matchers is not null && matchers.Count > 0)
            {
                // Add or replace the matchers for the trigger listener
                globalTriggerListenersMatchers ??= new Dictionary<string, List<IMatcher<TriggerKey>>>();
                globalTriggerListenersMatchers[triggerListener.Name] = new List<IMatcher<TriggerKey>>(matchers);
            }
            else
            {
                // Remove any registered matchers for the trigger listener
                RemoveTriggerListenerMatchers(triggerListener.Name);
            }
        }
    }

    public void AddTriggerListener(ITriggerListener triggerListener, IMatcher<TriggerKey> matcher)
    {
        if (triggerListener is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(triggerListener));
        }

        if (matcher is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(matcher));
        }

        if (string.IsNullOrEmpty(triggerListener.Name))
        {
            ThrowHelper.ThrowArgumentException($"{nameof(triggerListener.Name)} cannot be null or empty.", nameof(triggerListener));
        }

        lock (globalTriggerListenerLock)
        {
            // Add or replace the trigger listener
            globalTriggerListeners ??= new OrderedDictionary<string, ITriggerListener>();
            globalTriggerListeners[triggerListener.Name] = triggerListener;

            // Add or replace the matchers for the trigger listener
            globalTriggerListenersMatchers ??= new Dictionary<string, List<IMatcher<TriggerKey>>>();
            globalTriggerListenersMatchers[triggerListener.Name] = new List<IMatcher<TriggerKey>> { matcher };
        }
    }

    public bool AddTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher)
    {
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(matcher));
        }

        lock (globalTriggerListenerLock)
        {
            if (globalTriggerListenersMatchers is null || !globalTriggerListenersMatchers.TryGetValue(listenerName, out var matchers))
            {
                // Return false if no trigger listener is registered with the specified name
                if (globalTriggerListeners is null || !globalTriggerListeners.ContainsKey(listenerName))
                {
                    return false;
                }

                // We may be adding the first matcher for any job listener, so make sure globalJobListenersMatchers
                // is initialized
                globalTriggerListenersMatchers ??= new Dictionary<string, List<IMatcher<TriggerKey>>>();

                // We're adding the first matcher for the specified job listener
                matchers = new List<IMatcher<TriggerKey>>();
                globalTriggerListenersMatchers.Add(listenerName, matchers);
            }

            matchers.Add(matcher);
            return true;
        }
    }

    public bool RemoveTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher)
    {
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(matcher));
        }

        if (globalTriggerListenersMatchers is null)
        {
            return false;
        }

        lock (globalTriggerListenerLock)
        {
            if (globalTriggerListenersMatchers is null || !globalTriggerListenersMatchers.TryGetValue(listenerName, out var matchers))
            {
                return false;
            }

            var removed = matchers.Remove(matcher);

            if (removed && matchers.Count == 0)
            {
                RemoveTriggerListenerMatchers(listenerName);
            }

            return removed;
        }
    }

    public IReadOnlyCollection<IMatcher<TriggerKey>>? GetTriggerListenerMatchers(string listenerName)
    {
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (globalTriggerListenersMatchers is null)
        {
            return null;
        }

        lock (globalTriggerListenerLock)
        {
            if (globalTriggerListenersMatchers is null || !globalTriggerListenersMatchers.TryGetValue(listenerName, out var matchers))
            {
                return null;
            }

            return matchers.AsReadOnly();
        }
    }

    public bool SetTriggerListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<TriggerKey>> matchers)
    {
        if (listenerName is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(listenerName));
        }

        if (matchers is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(matchers));
        }

        lock (globalTriggerListenerLock)
        {
            if (globalTriggerListeners is null || !globalTriggerListeners.ContainsKey(listenerName))
            {
                return false;
            }

            if (matchers.Count == 0)
            {
                RemoveTriggerListenerMatchers(listenerName);
            }
            else
            {
                // Add or replace the matchers for the job listener
                globalTriggerListenersMatchers ??= new Dictionary<string, List<IMatcher<TriggerKey>>>();
                globalTriggerListenersMatchers[listenerName] = new List<IMatcher<TriggerKey>>(matchers);
            }

            return true;
        }
    }

    public bool RemoveTriggerListener(string name)
    {
        if (name is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(name));
        }

        if (globalTriggerListeners is null)
        {
            return false;
        }

        lock (globalTriggerListeners)
        {
            if (globalTriggerListeners is null)
            {
                return false;
            }

            var removed = globalTriggerListeners.Remove(name);

            // When we've removed a job listener, make sure to also remove associated matchers
            if (removed)
            {
                RemoveTriggerListenerMatchers(name);

                if (globalTriggerListeners.Count == 0)
                {
                    globalTriggerListeners = null;
                }
            }

            return removed;

        }
    }

    public ITriggerListener[] GetTriggerListeners()
    {
        if (globalTriggerListeners is null)
        {
            return Array.Empty<ITriggerListener>();
        }

        lock (globalTriggerListenerLock)
        {
            return globalTriggerListeners is not null ? globalTriggerListeners.Values.ToArray()
                : Array.Empty<ITriggerListener>();
        }
    }

    public ITriggerListener GetTriggerListener(string name)
    {
        if (name is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(name));
        }

        lock (globalTriggerListenerLock)
        {
            // Avoid initializing globalTriggerListeners when no trigger listeners have been added
            if (globalTriggerListeners is null || !globalTriggerListeners.TryGetValue(name, out var triggerListener))
            {
                ThrowHelper.ThrowKeyNotFoundException();
                return default;
            }

            return triggerListener;
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
        if (globalJobListenersMatchers is null)
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

    private void RemoveTriggerListenerMatchers(string listenerName)
    {
        if (globalTriggerListenersMatchers is null)
        {
            return;
        }

        // If we're removing the last matcher of the only trigger listener with matchers, then
        // reset globalTriggerListenersMatchers to null to avoid having to lock in subsequent calls
        // to GetTriggerListenerMatchers(string listenerName)
        if (globalTriggerListenersMatchers.Remove(listenerName) && globalTriggerListenersMatchers.Count == 0)
        {
            globalTriggerListenersMatchers = null;
        }
    }

}