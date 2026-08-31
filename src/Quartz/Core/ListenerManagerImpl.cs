using System.Diagnostics.CodeAnalysis;

namespace Quartz.Core;

/// <summary>
/// Default concrete implementation of <see cref="IListenerManager" />.
/// </summary>
/// <remarks>
/// A job or trigger listener is held as an <see cref="AttachedListener{TListener,TKey}" />, so the
/// matchers it was attached with travel with it. Matchers are settled when the listener is attached
/// and are not editable afterwards: a listener that has to hear about something else is attached
/// again, under the same name, with the matchers it needs.
/// </remarks>
internal sealed class ListenerManagerImpl : IListenerManager
{
    private readonly Lock globalJobListenerLock = new();
    private OrderedDictionary<string, AttachedListener<IJobListener, JobKey>>? globalJobListeners;

    private readonly Lock globalTriggerListenerLock = new();
    private OrderedDictionary<string, AttachedListener<ITriggerListener, TriggerKey>>? globalTriggerListeners;

    private readonly Lock schedulerListenerLock = new();
    private OrderedDictionary<string, ISchedulerListener>? schedulerListeners;

    public void AddJobListener(IJobListener jobListener, params IReadOnlyCollection<IMatcher<JobKey>> matchers)
    {
        if (jobListener is null)
        {
            Throw.ArgumentNullException(nameof(jobListener));
        }

        VerifyShape(jobListener, typeof(IJobListener));

        string name = jobListener.Name;
        if (string.IsNullOrEmpty(name))
        {
            Throw.ArgumentException($"{nameof(jobListener.Name)} cannot be null or empty.", nameof(jobListener));
        }

        lock (globalJobListenerLock)
        {
            // Add or replace the job listener, together with the matchers it is to be selected by
            globalJobListeners ??= new OrderedDictionary<string, AttachedListener<IJobListener, JobKey>>();
            globalJobListeners[name] = new AttachedListener<IJobListener, JobKey>(name, jobListener, Copy(matchers));
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

            bool removed = globalJobListeners.Remove(name);

            if (removed && globalJobListeners.Count == 0)
            {
                globalJobListeners = null;
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
            if (globalJobListeners is null)
            {
                return [];
            }

            IJobListener[] listeners = new IJobListener[globalJobListeners.Count];
            int index = 0;
            foreach (AttachedListener<IJobListener, JobKey> attached in globalJobListeners.Values)
            {
                listeners[index++] = attached.Listener;
            }

            return listeners;
        }
    }

    /// <summary>
    /// The job listeners with the matchers each of them was attached with, which is what the
    /// notification path needs and the only place the pairing is read.
    /// </summary>
    internal AttachedListener<IJobListener, JobKey>[] GetAttachedJobListeners()
    {
        if (globalJobListeners is null)
        {
            return [];
        }

        lock (globalJobListenerLock)
        {
            return globalJobListeners is not null ? [.. globalJobListeners.Values] : [];
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
            if (globalJobListeners is null || !globalJobListeners.TryGetValue(name, out AttachedListener<IJobListener, JobKey> attached))
            {
                return null;
            }

            return attached.Listener;
        }
    }

    public void AddTriggerListener(ITriggerListener triggerListener, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers)
    {
        if (triggerListener is null)
        {
            Throw.ArgumentNullException(nameof(triggerListener));
        }

        VerifyShape(triggerListener, typeof(ITriggerListener));

        string name = triggerListener.Name;
        if (string.IsNullOrEmpty(name))
        {
            Throw.ArgumentException($"{nameof(triggerListener.Name)} cannot be empty.", nameof(triggerListener));
        }

        lock (globalTriggerListenerLock)
        {
            // Add or replace the trigger listener, together with the matchers it is to be selected by
            globalTriggerListeners ??= new OrderedDictionary<string, AttachedListener<ITriggerListener, TriggerKey>>();
            globalTriggerListeners[name] = new AttachedListener<ITriggerListener, TriggerKey>(name, triggerListener, Copy(matchers));
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

            bool removed = globalTriggerListeners.Remove(name);

            if (removed && globalTriggerListeners.Count == 0)
            {
                globalTriggerListeners = null;
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
            if (globalTriggerListeners is null)
            {
                return [];
            }

            ITriggerListener[] listeners = new ITriggerListener[globalTriggerListeners.Count];
            int index = 0;
            foreach (AttachedListener<ITriggerListener, TriggerKey> attached in globalTriggerListeners.Values)
            {
                listeners[index++] = attached.Listener;
            }

            return listeners;
        }
    }

    /// <summary>
    /// The trigger listeners with the matchers each of them was attached with, which is what the
    /// notification path needs and the only place the pairing is read.
    /// </summary>
    internal AttachedListener<ITriggerListener, TriggerKey>[] GetAttachedTriggerListeners()
    {
        if (globalTriggerListeners is null)
        {
            return [];
        }

        lock (globalTriggerListenerLock)
        {
            return globalTriggerListeners is not null ? [.. globalTriggerListeners.Values] : [];
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
            if (globalTriggerListeners is null || !globalTriggerListeners.TryGetValue(name, out AttachedListener<ITriggerListener, TriggerKey> attached))
            {
                return null;
            }

            return attached.Listener;
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
    /// The matchers a listener is attached with, as an array the attachment owns.
    /// </summary>
    /// <remarks>
    /// The caller's collection is copied because it is the caller's to keep changing, and an empty
    /// one is the same as none at all: both mean the listener hears everything.
    /// </remarks>
    private static IMatcher<TKey>[] Copy<TKey>(IReadOnlyCollection<IMatcher<TKey>>? matchers) where TKey : Key<TKey>
    {
        return matchers is null || matchers.Count == 0 ? [] : [.. matchers];
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
}
