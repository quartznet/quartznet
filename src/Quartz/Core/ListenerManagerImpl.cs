using System.Diagnostics.CodeAnalysis;

namespace Quartz.Core;

/// <summary>
/// Default concrete implementation of <see cref="IListenerManager" />.
/// </summary>
/// <remarks>
/// <para>
/// A job or trigger listener is held as an <see cref="AttachedListener{TListener,TKey}" />, so the
/// matchers it was attached with travel with it. Matchers are settled when the listener is attached
/// and are not editable afterwards: a listener that has to hear about something else is attached
/// again, under the same name, with the matchers it needs.
/// </para>
/// <para>
/// <b>Reads are snapshots built when the registrations change</b> (#3865). Every notification starts by
/// asking for the listeners registered right now, four times a firing and twice a schedule, while the
/// registrations change a handful of times in a process's life. So each change, under its kind's lock,
/// builds a new <see cref="Registrations{TEntry,TListener}" /> and publishes it whole, and a read takes
/// the published one without a lock and without a copy. A reader racing a change holds the set from
/// before it or the set from after it, never a mixture, and the set it holds never changes under it.
/// </para>
/// </remarks>
internal sealed class ListenerManagerImpl : IListenerManager
{
    private readonly Lock globalJobListenerLock = new();
    private OrderedDictionary<string, AttachedListener<IJobListener, JobKey>>? globalJobListeners;
    private Registrations<AttachedListener<IJobListener, JobKey>, IJobListener> jobListenerSnapshot =
        Registrations<AttachedListener<IJobListener, JobKey>, IJobListener>.None;

    private readonly Lock globalTriggerListenerLock = new();
    private OrderedDictionary<string, AttachedListener<ITriggerListener, TriggerKey>>? globalTriggerListeners;
    private Registrations<AttachedListener<ITriggerListener, TriggerKey>, ITriggerListener> triggerListenerSnapshot =
        Registrations<AttachedListener<ITriggerListener, TriggerKey>, ITriggerListener>.None;

    private readonly Lock schedulerListenerLock = new();
    private OrderedDictionary<string, ISchedulerListener>? schedulerListeners;
    private Registrations<ISchedulerListener, ISchedulerListener> schedulerListenerSnapshot =
        Registrations<ISchedulerListener, ISchedulerListener>.None;

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
            Volatile.Write(ref jobListenerSnapshot, Snapshot(globalJobListeners));
        }
    }

    public bool RemoveJobListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name));
        }

        if (Volatile.Read(ref jobListenerSnapshot).Entries.Length == 0)
        {
            return false;
        }

        lock (globalJobListenerLock)
        {
            if (globalJobListeners is null || !globalJobListeners.Remove(name))
            {
                return false;
            }

            if (globalJobListeners.Count == 0)
            {
                globalJobListeners = null;
            }

            Volatile.Write(ref jobListenerSnapshot, Snapshot(globalJobListeners));
            return true;
        }
    }

    /// <remarks>
    /// The same read-only list until the registrations next change, not a copy per call.
    /// </remarks>
    public IReadOnlyList<IJobListener> GetJobListeners()
    {
        return Volatile.Read(ref jobListenerSnapshot).Listeners;
    }

    /// <summary>
    /// The job listeners with the matchers each of them was attached with, which is what the
    /// notification path needs and the only place the pairing is read.
    /// </summary>
    /// <remarks>
    /// The published snapshot itself, which nothing writes to once it is published: a caller iterates
    /// it and never changes it.
    /// </remarks>
    internal AttachedListener<IJobListener, JobKey>[] GetAttachedJobListeners()
    {
        return Volatile.Read(ref jobListenerSnapshot).Entries;
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
            Volatile.Write(ref triggerListenerSnapshot, Snapshot(globalTriggerListeners));
        }
    }

    public bool RemoveTriggerListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name));
        }

        if (Volatile.Read(ref triggerListenerSnapshot).Entries.Length == 0)
        {
            return false;
        }

        lock (globalTriggerListenerLock)
        {
            if (globalTriggerListeners is null || !globalTriggerListeners.Remove(name))
            {
                return false;
            }

            if (globalTriggerListeners.Count == 0)
            {
                globalTriggerListeners = null;
            }

            Volatile.Write(ref triggerListenerSnapshot, Snapshot(globalTriggerListeners));
            return true;
        }
    }

    /// <remarks>
    /// The same read-only list until the registrations next change, not a copy per call.
    /// </remarks>
    public IReadOnlyList<ITriggerListener> GetTriggerListeners()
    {
        return Volatile.Read(ref triggerListenerSnapshot).Listeners;
    }

    /// <summary>
    /// The trigger listeners with the matchers each of them was attached with, which is what the
    /// notification path needs and the only place the pairing is read.
    /// </summary>
    /// <remarks>
    /// The published snapshot itself, which nothing writes to once it is published: a caller iterates
    /// it and never changes it.
    /// </remarks>
    internal AttachedListener<ITriggerListener, TriggerKey>[] GetAttachedTriggerListeners()
    {
        return Volatile.Read(ref triggerListenerSnapshot).Entries;
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
            Volatile.Write(ref schedulerListenerSnapshot, Snapshot(schedulerListeners));
        }
    }

    public bool RemoveSchedulerListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentNullException(nameof(name));
        }

        if (Volatile.Read(ref schedulerListenerSnapshot).Entries.Length == 0)
        {
            return false;
        }

        lock (schedulerListenerLock)
        {
            if (schedulerListeners is null || !schedulerListeners.Remove(name))
            {
                return false;
            }

            if (schedulerListeners.Count == 0)
            {
                schedulerListeners = null;
            }

            Volatile.Write(ref schedulerListenerSnapshot, Snapshot(schedulerListeners));
            return true;
        }
    }

    /// <remarks>
    /// The same read-only list until the registrations next change, not a copy per call.
    /// </remarks>
    public IReadOnlyList<ISchedulerListener> GetSchedulerListeners()
    {
        return Volatile.Read(ref schedulerListenerSnapshot).Listeners;
    }

    /// <summary>
    /// The scheduler listeners as an array, which is what the notification path iterates.
    /// </summary>
    /// <remarks>
    /// The published snapshot itself, which nothing writes to once it is published: a caller iterates
    /// it and never changes it.
    /// </remarks>
    internal ISchedulerListener[] GetSchedulerListenerArray()
    {
        return Volatile.Read(ref schedulerListenerSnapshot).Entries;
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
    /// What readers of one kind of attached listener see once a change has been made, built from the
    /// registrations as the change left them.
    /// </summary>
    private static Registrations<AttachedListener<TListener, TKey>, TListener> Snapshot<TListener, TKey>(
        OrderedDictionary<string, AttachedListener<TListener, TKey>>? registered) where TKey : Key<TKey>
    {
        if (registered is null || registered.Count == 0)
        {
            return Registrations<AttachedListener<TListener, TKey>, TListener>.None;
        }

        AttachedListener<TListener, TKey>[] entries = [.. registered.Values];
        TListener[] listeners = new TListener[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            listeners[i] = entries[i].Listener;
        }

        return new Registrations<AttachedListener<TListener, TKey>, TListener>(entries, listeners);
    }

    /// <inheritdoc cref="Snapshot{TListener,TKey}" />
    private static Registrations<ISchedulerListener, ISchedulerListener> Snapshot(OrderedDictionary<string, ISchedulerListener>? registered)
    {
        if (registered is null || registered.Count == 0)
        {
            return Registrations<ISchedulerListener, ISchedulerListener>.None;
        }

        ISchedulerListener[] entries = [.. registered.Values];
        return new Registrations<ISchedulerListener, ISchedulerListener>(entries, entries);
    }

    /// <summary>
    /// One kind of listener as every reader sees it until the next change: built whole by the change
    /// and never written to afterwards.
    /// </summary>
    /// <typeparam name="TEntry">What the notification path iterates.</typeparam>
    /// <typeparam name="TListener">What <see cref="IListenerManager" /> hands out.</typeparam>
    private sealed class Registrations<TEntry, TListener>
    {
        /// <summary>No listeners of this kind, which is where every manager starts.</summary>
        public static readonly Registrations<TEntry, TListener> None = new([], []);

        public Registrations(TEntry[] entries, TListener[] listeners)
        {
            Entries = entries;

            // Read-only rather than the array itself, which is shared by every caller until the next
            // change: an array handed out could be cast back and written to.
            Listeners = listeners.Length == 0 ? Array.Empty<TListener>() : Array.AsReadOnly(listeners);
        }

        /// <summary>The entries, in registration order, for the notification path.</summary>
        public TEntry[] Entries { get; }

        /// <summary>The listeners, in registration order, for the public reads.</summary>
        public IReadOnlyList<TListener> Listeners { get; }
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
