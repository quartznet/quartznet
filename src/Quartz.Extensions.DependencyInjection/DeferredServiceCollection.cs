using System;
using System.Collections;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz;

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

        // Fall through to inner collection — harmless since the service provider
        // is already built and won't pick up new registrations.
        inner.Add(item);
    }

    private bool TryHandleDeferred(ServiceDescriptor descriptor)
    {
        // Intercept IConfigureOptions<QuartzOptions> factory registrations.
        // These come from AddJob/AddTrigger/ScheduleJob/AddCalendar extension methods
        // which register IConfigureOptions<QuartzOptions> to add job details and triggers.
        if (descriptor.ServiceType == typeof(IConfigureOptions<QuartzOptions>))
        {
            object? instance = ResolveInstance(descriptor);

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
                options.deferredSchedulerListeners.Add(config);
                return true;
            }
        }

        // Intercept JobListenerConfiguration registrations
        if (descriptor.ServiceType == typeof(JobListenerConfiguration))
        {
            if (ResolveInstance(descriptor) is JobListenerConfiguration config)
            {
                options.deferredJobListeners.Add(config);
                return true;
            }
        }

        // Intercept TriggerListenerConfiguration registrations
        if (descriptor.ServiceType == typeof(TriggerListenerConfiguration))
        {
            if (ResolveInstance(descriptor) is TriggerListenerConfiguration config)
            {
                options.deferredTriggerListeners.Add(config);
                return true;
            }
        }

        // Intercept CalendarConfiguration registrations
        if (descriptor.ServiceType == typeof(CalendarConfiguration))
        {
            if (ResolveInstance(descriptor) is CalendarConfiguration config)
            {
                options.deferredCalendars.Add(config);
                return true;
            }
        }

        // Intercept direct ISchedulerListener registrations (default scheduler path)
        if (descriptor.ServiceType == typeof(ISchedulerListener))
        {
            Type? listenerType = descriptor.ImplementationType ?? descriptor.ServiceType;
            Func<IServiceProvider, ISchedulerListener>? factory = null;
            ISchedulerListener? instance = null;

            if (descriptor.ImplementationFactory != null)
            {
                factory = sp => (ISchedulerListener) descriptor.ImplementationFactory(sp);
            }
            else if (descriptor.ImplementationInstance is ISchedulerListener inst)
            {
                instance = inst;
            }

            options.deferredSchedulerListeners.Add(
                new SchedulerListenerConfiguration(listenerType, optionsName, factory, instance));
            return true;
        }

        // Intercept direct IJobListener registrations (default scheduler path)
        if (descriptor.ServiceType == typeof(IJobListener))
        {
            Type? listenerType = descriptor.ImplementationType ?? descriptor.ServiceType;
            Func<IServiceProvider, IJobListener>? factory = null;
            IJobListener? instance = null;

            if (descriptor.ImplementationFactory != null)
            {
                factory = sp => (IJobListener) descriptor.ImplementationFactory(sp);
            }
            else if (descriptor.ImplementationInstance is IJobListener inst)
            {
                instance = inst;
            }

            // No matchers available from a bare IJobListener registration
            options.deferredJobListeners.Add(
                new JobListenerConfiguration(listenerType, Array.Empty<IMatcher<JobKey>>(), optionsName, factory, instance));
            return true;
        }

        // Intercept direct ITriggerListener registrations (default scheduler path)
        if (descriptor.ServiceType == typeof(ITriggerListener))
        {
            Type? listenerType = descriptor.ImplementationType ?? descriptor.ServiceType;
            Func<IServiceProvider, ITriggerListener>? factory = null;
            ITriggerListener? instance = null;

            if (descriptor.ImplementationFactory != null)
            {
                factory = sp => (ITriggerListener) descriptor.ImplementationFactory(sp);
            }
            else if (descriptor.ImplementationInstance is ITriggerListener inst)
            {
                instance = inst;
            }

            options.deferredTriggerListeners.Add(
                new TriggerListenerConfiguration(listenerType, Array.Empty<IMatcher<TriggerKey>>(), optionsName, factory, instance));
            return true;
        }

        return false;
    }

    private object? ResolveInstance(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance != null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory != null)
        {
            return descriptor.ImplementationFactory(serviceProvider);
        }

        return null;
    }

    // --- IList<ServiceDescriptor> delegation ---

    public ServiceDescriptor this[int index]
    {
        get => inner[index];
        set => inner[index] = value;
    }

    public int Count => inner.Count;

    public bool IsReadOnly => inner.IsReadOnly;

    public void Clear() => inner.Clear();

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

        inner.Insert(index, item);
    }

    public bool Remove(ServiceDescriptor item) => inner.Remove(item);

    public void RemoveAt(int index) => inner.RemoveAt(index);

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) inner).GetEnumerator();
}
