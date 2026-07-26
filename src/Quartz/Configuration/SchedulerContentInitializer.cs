using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Impl.Matchers;

namespace Quartz.Configuration;

/// <summary>
/// Applies the listeners, calendars, jobs and triggers registered for a scheduler once that scheduler
/// exists.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately separate from constructing the scheduler. Construction is what the container
/// does; this is the content the application asked to be present, none of which can be applied until
/// there is a scheduler to apply it to.
/// </para>
/// <para>
/// Content is resolved by service key, like every other part of a scheduler. A registration made for one
/// scheduler is therefore never even seen by another, and a listener arrives together with the matchers
/// it was registered with instead of being re-joined to them by type.
/// </para>
/// </remarks>
internal sealed class SchedulerContentInitializer
{
    /// <summary>
    /// The scheduler context entry through which plugins reach the container.
    /// </summary>
    internal const string ServiceProviderContextKey = "Quartz.ServiceProvider";

    private readonly IServiceProvider serviceProvider;
    private readonly SchedulerKey schedulerKey;
    private readonly NameValueCollection properties;
    private readonly ContainerConfigurationProcessor processor;

    /// <remarks>
    /// Takes this scheduler's flat properties directly rather than its <see cref="QuartzOptions"/>. The
    /// legacy <c>quartz.jobListener.*</c> and <c>quartz.triggerListener.*</c> keys are all this needs from
    /// them, and they are resolved for this scheduler's options name so a named scheduler does not read
    /// the default scheduler's.
    /// </remarks>
    public SchedulerContentInitializer(
        IServiceProvider serviceProvider,
        SchedulerKey schedulerKey,
        NameValueCollection properties,
        ContainerConfigurationProcessor processor)
    {
        this.serviceProvider = serviceProvider;
        this.schedulerKey = schedulerKey;
        this.properties = properties;
        this.processor = processor;
    }

    private object? Key => schedulerKey.Key;

    public async ValueTask Initialize(IScheduler scheduler, CancellationToken cancellationToken)
    {
        // Plugins reach the container through the scheduler context.
        scheduler.Context[ServiceProviderContextKey] = serviceProvider;

        AddSchedulerListeners(scheduler);
        AddJobListeners(scheduler);
        AddTriggerListeners(scheduler);

        await AddCalendars(scheduler, cancellationToken).ConfigureAwait(false);
        await processor.ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    private void AddSchedulerListeners(IScheduler scheduler)
    {
        foreach (var registration in Registrations<SchedulerListenerRegistration>())
        {
            scheduler.ListenerManager.AddSchedulerListener(registration.CreateListener(serviceProvider));
        }

        // Listeners the application registered as plain services, which carry nothing of their own.
        foreach (var listener in ListenerServices<ISchedulerListener>())
        {
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }
    }

    private void AddJobListeners(IScheduler scheduler)
    {
        var listeners = new List<(IJobListener Listener, IMatcher<JobKey>[] Matchers)>();

        foreach (var registration in Registrations<JobListenerRegistration>())
        {
            listeners.Add((registration.CreateListener(serviceProvider), registration.Matchers));
        }

        // Listeners the application registered as plain services, which carry no matchers and so listen
        // to everything.
        foreach (var listener in ListenerServices<IJobListener>())
        {
            listeners.Add((listener, []));
        }

        // Listeners named by quartz.jobListener.* properties, which also carry no matchers, as that
        // format has always meant.
        foreach (var listener in PropertyListenerFactory.Create<IJobListener>(
                     serviceProvider, properties, StdSchedulerFactory.PropertyJobListenerPrefix))
        {
            listeners.Add((listener, [EverythingMatcher<JobKey>.AllJobs()]));
        }

        RejectDuplicateNames(scheduler, "job", listeners.Select(static x => (x.Listener.Name, (object) x.Listener)));

        foreach (var (listener, matchers) in listeners)
        {
            scheduler.ListenerManager.AddJobListener(listener, matchers);
        }
    }

    private void AddTriggerListeners(IScheduler scheduler)
    {
        var listeners = new List<(ITriggerListener Listener, IMatcher<TriggerKey>[] Matchers)>();

        foreach (var registration in Registrations<TriggerListenerRegistration>())
        {
            listeners.Add((registration.CreateListener(serviceProvider), registration.Matchers));
        }

        foreach (var listener in ListenerServices<ITriggerListener>())
        {
            listeners.Add((listener, []));
        }

        foreach (var listener in PropertyListenerFactory.Create<ITriggerListener>(
                     serviceProvider, properties, StdSchedulerFactory.PropertyTriggerListenerPrefix))
        {
            listeners.Add((listener, [EverythingMatcher<TriggerKey>.AllTriggers()]));
        }

        RejectDuplicateNames(scheduler, "trigger", listeners.Select(static x => (x.Listener.Name, (object) x.Listener)));

        foreach (var (listener, matchers) in listeners)
        {
            scheduler.ListenerManager.AddTriggerListener(listener, matchers);
        }
    }

    private async ValueTask AddCalendars(IScheduler scheduler, CancellationToken cancellationToken)
    {
        foreach (var configuration in Registrations<CalendarConfiguration>())
        {
            await scheduler.AddCalendar(
                configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The content registered for this scheduler: keyed by its name, or unkeyed for the default one.
    /// </summary>
    private T[] Registrations<T>()
    {
        return serviceProvider.GetSchedulerServices<T>(Key).ToArray();
    }

    /// <summary>
    /// Listeners registered as plain services rather than through the builder.
    /// </summary>
    /// <remarks>
    /// An unkeyed registration is container-wide, so a listener registered that way belongs to every
    /// scheduler in the container rather than only the default one — which is how it used to look, because
    /// named schedulers skipped this source entirely. A keyed registration belongs to the scheduler whose
    /// name it is keyed with, for an application that wants one scheduler's listener injected as a service.
    /// </remarks>
    private IEnumerable<T> ListenerServices<T>()
    {
        var shared = serviceProvider.GetServices<T>();
        return Key is null ? shared : shared.Concat(serviceProvider.GetKeyedServices<T>(Key));
    }

    /// <summary>
    /// Refuses two listeners of the same kind that answer to the same name.
    /// </summary>
    /// <remarks>
    /// A listener manager holds one listener per name and replaces on collision, and a replacement that
    /// carries no matchers drops the matchers of the listener it replaced. Two registrations that happen
    /// to produce the same name therefore quietly become one — the very ambiguity that carrying the
    /// pairing in the registration removes. Say so instead of applying only one of them.
    /// </remarks>
    private static void RejectDuplicateNames(
        IScheduler scheduler,
        string kind,
        IEnumerable<(string Name, object Listener)> listeners)
    {
        var seen = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, listener) in listeners)
        {
            if (string.IsNullOrEmpty(name))
            {
                // A nameless listener is rejected by the listener manager itself, which says so better.
                continue;
            }

            if (seen.TryGetValue(name, out var existing))
            {
                var cause = ReferenceEquals(existing, listener)
                    ? $"the same {listener.GetType()} is registered twice"
                    : $"{existing.GetType()} and {listener.GetType()} both answer to it";

                Throw.SchedulerConfigException(
                    $"Two {kind} listeners configured for scheduler '{scheduler.SchedulerName}' share the name "
                    + $"'{name}': {cause}. A scheduler knows a listener by its name, so the second would replace the "
                    + "first and take its matchers with it. Register the listener once, or give them distinct names.");
            }

            seen[name] = listener;
        }
    }
}
