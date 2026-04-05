using System.Collections;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Configuration;

/// <summary>
/// An <see cref="IServiceCollection"/> wrapper used during deferred Quartz configuration.
/// Intercepts service registrations that would otherwise be lost (since the
/// <see cref="IServiceProvider"/> is already built) and applies them directly
/// to the <see cref="QuartzOptions"/> being configured.
/// </summary>
internal sealed class DeferredServiceCollection : IServiceCollection
{
    private readonly IServiceCollection inner;
    private readonly IServiceProvider serviceProvider;
    private readonly QuartzOptions options;
    private readonly string optionsName;

    public DeferredServiceCollection(
        IServiceCollection inner,
        IServiceProvider serviceProvider,
        QuartzOptions options,
        string optionsName)
    {
        this.inner = inner;
        this.serviceProvider = serviceProvider;
        this.options = options;
        this.optionsName = optionsName;
    }

    public void Add(ServiceDescriptor item)
    {
        if (TryHandleDeferred(item))
        {
            return;
        }

        // Silently discard — the service provider is already built, so adding
        // to the original IServiceCollection would have no effect and could
        // cause confusion if the collection is inspected later.
        // Registrations that matter (jobs, triggers, listeners, calendars)
        // are all intercepted above.
    }

    private bool TryHandleDeferred(ServiceDescriptor descriptor)
    {
        // Intercept IConfigureOptions<QuartzOptions> factory registrations.
        // These come from AddJob/AddTrigger/ScheduleJob/AddCalendar extension methods
        // which register IConfigureOptions<QuartzOptions> to add job details and triggers.
        if (descriptor.ServiceType == typeof(IConfigureOptions<QuartzOptions>))
        {
            var instance = ResolveInstance(descriptor);

            if (instance is IConfigureNamedOptions<QuartzOptions> namedOpts)
            {
                namedOpts.Configure(optionsName, options);
                return true;
            }

            if (instance is IConfigureOptions<QuartzOptions> opts)
            {
                opts.Configure(options);
                return true;
            }
        }

        // Intercept SchedulerListenerConfiguration registrations (named scheduler path)
        if (descriptor.ServiceType == typeof(SchedulerListenerConfiguration))
        {
            if (ResolveInstance(descriptor) is SchedulerListenerConfiguration config)
            {
                options._deferredSchedulerListeners.Add(config);
                return true;
            }
        }

        // Intercept JobListenerConfiguration registrations
        if (descriptor.ServiceType == typeof(JobListenerConfiguration))
        {
            if (ResolveInstance(descriptor) is JobListenerConfiguration config)
            {
                options._deferredJobListeners.Add(config);
                return true;
            }
        }

        // Intercept TriggerListenerConfiguration registrations
        if (descriptor.ServiceType == typeof(TriggerListenerConfiguration))
        {
            if (ResolveInstance(descriptor) is TriggerListenerConfiguration config)
            {
                options._deferredTriggerListeners.Add(config);
                return true;
            }
        }

        // Intercept CalendarConfiguration registrations
        if (descriptor.ServiceType == typeof(CalendarConfiguration))
        {
            if (ResolveInstance(descriptor) is CalendarConfiguration config)
            {
                options._deferredCalendars.Add(config);
                return true;
            }
        }

        // Intercept direct ISchedulerListener registrations (default scheduler path)
        if (descriptor.ServiceType == typeof(ISchedulerListener))
        {
            var listenerType = descriptor.ImplementationType ?? descriptor.ServiceType;
            Func<IServiceProvider, ISchedulerListener>? factory = null;
            ISchedulerListener? instance = null;

            if (descriptor.ImplementationFactory is not null)
            {
                factory = sp => (ISchedulerListener) descriptor.ImplementationFactory(sp);
            }
            else if (descriptor.ImplementationInstance is ISchedulerListener inst)
            {
                instance = inst;
            }

            options._deferredSchedulerListeners.Add(
                new SchedulerListenerConfiguration(listenerType, optionsName, factory, instance));
            return true;
        }

        // Intercept direct IJobListener/ITriggerListener registrations (default scheduler path).
        // The configurator always registers BOTH a *Configuration object (with matchers, factory,
        // and the concrete type) AND the bare interface. The *Configuration is already captured
        // above with full metadata. Suppress the interface registration unconditionally to avoid
        // duplicate listener creation during scheduler initialization.
        //
        // This is safe because IServiceCollectionQuartzConfigurator.Services is internal —
        // all listener registrations go through the configurator's AddJobListener/AddTriggerListener
        // methods which always emit the paired *Configuration.
        if (descriptor.ServiceType == typeof(IJobListener) || descriptor.ServiceType == typeof(ITriggerListener))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the service instance from a descriptor's factory or cached instance.
    /// Returns null for type-only descriptors (ImplementationType without factory/instance).
    /// This is safe because all Quartz configurator registrations we intercept use either
    /// ImplementationInstance (e.g. <c>new XxxConfiguration(...)</c>) or ImplementationFactory
    /// (e.g. <c>serviceProvider => ...</c>), never bare ImplementationType.
    /// </summary>
    private object? ResolveInstance(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(serviceProvider);
        }

        return null;
    }

    // --- IList<ServiceDescriptor> delegation ---
    // Read operations delegate to the inner collection so that TryAddSingleton
    // and similar helpers can check for existing registrations.
    // Write operations (other than Add/Insert handled above) are no-ops to
    // prevent accidental mutation of the original IServiceCollection after
    // the IServiceProvider has been built.

    public ServiceDescriptor this[int index]
    {
        get => inner[index];
        set { } // no-op — service provider already built
    }

    public int Count => inner.Count;

    public bool IsReadOnly => true;

    public void Clear() { } // no-op

    public bool Contains(ServiceDescriptor item) => inner.Contains(item);

    public void CopyTo(ServiceDescriptor[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);

    public IEnumerator<ServiceDescriptor> GetEnumerator() => inner.GetEnumerator();

    public int IndexOf(ServiceDescriptor item) => inner.IndexOf(item);

    public void Insert(int index, ServiceDescriptor item)
    {
        if (TryHandleDeferred(item))
        {
            return;
        }

        // Silently discard — same rationale as Add
    }

    public bool Remove(ServiceDescriptor item) => false; // no-op

    public void RemoveAt(int index) { } // no-op

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) inner).GetEnumerator();
}
