using System.Diagnostics.CodeAnalysis;

namespace Quartz.Core;

/// <summary>
/// Default concrete implementation of <see cref="IListenerManager" />.
/// </summary>
internal sealed class ListenerManagerImpl : IListenerManager
{
    private readonly Lock globalJobListenerLock = new();
    private OrderedDictionary<string, IJobListener>? globalJobListeners;
    private Dictionary<string, List<IMatcher<JobKey>>>? globalJobListenersMatchers;

    private readonly Lock globalTriggerListenerLock = new();
    private OrderedDictionary<string, ITriggerListener>? globalTriggerListeners;
    private Dictionary<string, List<IMatcher<TriggerKey>>>? globalTriggerListenersMatchers;

    private readonly Lock schedulerListenerLock = new();
    private OrderedDictionary<string, ISchedulerListener>? schedulerListeners;

    public void AddJobListener(IJobListener jobListener, params IReadOnlyCollection<IMatcher<JobKey>> matchers)
    {
        if (jobListener is null)
        {
            Throw.ArgumentNullException(nameof(jobListener));
        }

        VerifyShape(jobListener, typeof(IJobListener));

        if (string.IsNullOrEmpty(jobListener.Name))
        {
            Throw.ArgumentException($"{nameof(jobListener.Name)} cannot be null or empty.", nameof(jobListener));
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
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            Throw.ArgumentNullException(nameof(matcher));
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
                matchers = [];
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
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            Throw.ArgumentNullException(nameof(matcher));
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

    public IReadOnlyList<IMatcher<JobKey>> GetJobListenerMatchers(string listenerName)
    {
        if (listenerName is null)
        {
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (globalJobListenersMatchers is null)
        {
            return [];
        }

        lock (globalJobListenerLock)
        {
            if (globalJobListenersMatchers is null || !globalJobListenersMatchers.TryGetValue(listenerName, out var matchers))
            {
                return [];
            }

            return matchers.ToArray();
        }
    }

    public bool SetJobListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<JobKey>> matchers)
    {
        if (listenerName is null)
        {
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (matchers is null)
        {
            Throw.ArgumentNullException(nameof(matchers));
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
            Throw.ArgumentNullException(nameof(name));
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

    public IReadOnlyList<IJobListener> GetJobListeners()
    {
        if (globalJobListeners is null)
        {
            return [];
        }

        lock (globalJobListenerLock)
        {
            return globalJobListeners is not null ? globalJobListeners.Values.ToArray()
                : [];
        }
    }

    public IJobListener? GetJobListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name));
        }

        lock (globalJobListenerLock)
        {
            // Avoid initializing globalJobListeners when no job listeners have been added
            if (globalJobListeners is null || !globalJobListeners.TryGetValue(name, out var jobListener))
            {
                return null;
            }

            return jobListener;
        }
    }

    public void AddTriggerListener(ITriggerListener triggerListener, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers)
    {
        if (triggerListener is null)
        {
            Throw.ArgumentNullException(nameof(triggerListener));
        }

        VerifyShape(triggerListener, typeof(ITriggerListener));

        if (string.IsNullOrEmpty(triggerListener.Name))
        {
            Throw.ArgumentException($"{nameof(triggerListener.Name)} cannot be empty.", nameof(triggerListener));
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

    public bool AddTriggerListenerMatcher(string listenerName, IMatcher<TriggerKey> matcher)
    {
        if (listenerName is null)
        {
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            Throw.ArgumentNullException(nameof(matcher));
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
                matchers = [];
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
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (matcher is null)
        {
            Throw.ArgumentNullException(nameof(matcher));
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

    public IReadOnlyList<IMatcher<TriggerKey>> GetTriggerListenerMatchers(string listenerName)
    {
        if (listenerName is null)
        {
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (globalTriggerListenersMatchers is null)
        {
            return [];
        }

        lock (globalTriggerListenerLock)
        {
            if (globalTriggerListenersMatchers is null || !globalTriggerListenersMatchers.TryGetValue(listenerName, out var matchers))
            {
                return [];
            }

            return matchers.ToArray();
        }
    }

    public bool SetTriggerListenerMatchers(string listenerName, IReadOnlyCollection<IMatcher<TriggerKey>> matchers)
    {
        if (listenerName is null)
        {
            Throw.ArgumentNullException(nameof(listenerName));
        }

        if (matchers is null)
        {
            Throw.ArgumentNullException(nameof(matchers));
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
            Throw.ArgumentNullException(nameof(name));
        }

        if (globalTriggerListeners is null)
        {
            return false;
        }

        lock (globalTriggerListenerLock)
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

    public IReadOnlyList<ITriggerListener> GetTriggerListeners()
    {
        if (globalTriggerListeners is null)
        {
            return [];
        }

        lock (globalTriggerListenerLock)
        {
            return globalTriggerListeners is not null
                ? [.. globalTriggerListeners.Values]
                : [];
        }
    }

    public ITriggerListener? GetTriggerListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name));
        }

        lock (globalTriggerListenerLock)
        {
            // Avoid initializing globalTriggerListeners when no trigger listeners have been added
            if (globalTriggerListeners is null || !globalTriggerListeners.TryGetValue(name, out var triggerListener))
            {
                return null;
            }

            return triggerListener;
        }
    }

    public void AddSchedulerListener(ISchedulerListener schedulerListener)
    {
        if (schedulerListener is null)
        {
            Throw.ArgumentNullException(nameof(schedulerListener));
        }

        VerifyShape(schedulerListener, typeof(ISchedulerListener));

        if (string.IsNullOrEmpty(schedulerListener.Name))
        {
            Throw.ArgumentException($"{nameof(schedulerListener.Name)} cannot be null or empty.", nameof(schedulerListener));
        }

        lock (schedulerListenerLock)
        {
            schedulerListeners ??= new OrderedDictionary<string, ISchedulerListener>();
            schedulerListeners[schedulerListener.Name] = schedulerListener;
        }
    }

    public bool RemoveSchedulerListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name));
        }

        if (schedulerListeners is null)
        {
            return false;
        }

        lock (schedulerListenerLock)
        {
            if (schedulerListeners is null)
            {
                return false;
            }

            bool removed = schedulerListeners.Remove(name);

            if (removed && schedulerListeners.Count == 0)
            {
                schedulerListeners = null;
            }

            return removed;
        }
    }

    public IReadOnlyList<ISchedulerListener> GetSchedulerListeners()
    {
        if (schedulerListeners is null)
        {
            return [];
        }

        lock (schedulerListenerLock)
        {
            return schedulerListeners is not null
                ? [.. schedulerListeners.Values]
                : [];
        }
    }

    public ISchedulerListener? GetSchedulerListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name));
        }

        lock (schedulerListenerLock)
        {
            if (schedulerListeners is null || !schedulerListeners.TryGetValue(name, out ISchedulerListener? schedulerListener))
            {
                return null;
            }

            return schedulerListener;
        }
    }

    /// <summary>
    /// Refuses a listener whose public methods say it implements a notification it does not.
    /// </summary>
    /// <remarks>
    /// This is the last gate every listener passes through, whatever registered it: the builder, a plain
    /// service registration, a <c>quartz.*Listener.*</c> key, a plugin, or an application calling
    /// <see cref="IListenerManager" /> itself. A listener the builder registered by type or by instance
    /// was already refused at registration, which is the better moment to hear about it;
    /// <see cref="ListenerShape" /> remembers the types it has passed, so arriving here twice costs a
    /// dictionary lookup.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The methods read are the public methods of a listener the application constructed and handed over, so the type is rooted. Trimming can only take away a member nothing calls, which is exactly the stale member being looked for: the check can then find nothing, never the wrong thing. The registration paths that do know the type statically annotate it and keep those members.")]
    private static void VerifyShape(
        object listener,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type listenerInterface)
    {
        ListenerShape.Verify(listener.GetType(), listenerInterface);
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
