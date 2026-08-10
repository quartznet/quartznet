using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;

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
        var configured = new List<ISchedulerListener>();
        foreach (var registration in Registrations<SchedulerListenerRegistration>())
        {
            configured.Add(registration.CreateListener(serviceProvider));
        }

        foreach (var listener in configured)
        {
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Listeners the application registered as plain services, which carry nothing of their own.
        // A scheduler listener manager keeps these in a plain list with no notion of identity, so a
        // listener that was both configured and registered as a service would be notified twice.
        foreach (var listener in ListenerServices<ISchedulerListener>())
        {
            if (AlreadyConfigured(configured, listener))
            {
                continue;
            }

            scheduler.ListenerManager.AddSchedulerListener(listener);
        }
    }

    private void AddJobListeners(IScheduler scheduler)
    {
        var configured = new List<IJobListener>();
        var listeners = new List<(IJobListener Listener, IMatcher<JobKey>[] Matchers)>();

        foreach (var registration in Registrations<JobListenerRegistration>())
        {
            var listener = registration.CreateListener(serviceProvider);
            configured.Add(listener);
            listeners.Add((listener, registration.Matchers));
        }

        // Two builder registrations answering to one name is the genuinely ambiguous case: both asked for
        // matchers, and the listener manager can only keep one of them.
        RejectDuplicateNames(scheduler, "job", configured);

        // Listeners the application registered as plain services, which carry no matchers and so listen
        // to everything.
        foreach (var listener in ListenerServices<IJobListener>())
        {
            if (AlreadyConfigured(configured, listener))
            {
                continue;
            }

            listeners.Add((listener, []));
        }

        // Listeners named by quartz.jobListener.* properties, which also carry no matchers, as that
        // format has always meant.
        foreach (var listener in PropertyListenerFactory.Create<IJobListener>(
                     serviceProvider, properties, LegacyPropertyKeys.JobListenerPrefix))
        {
            if (AlreadyConfigured(configured, listener))
            {
                continue;
            }

            listeners.Add((listener, [Matchers.AllJobs()]));
        }

        foreach (var (listener, matchers) in listeners)
        {
            scheduler.ListenerManager.AddJobListener(listener, matchers);
        }
    }

    private void AddTriggerListeners(IScheduler scheduler)
    {
        var configured = new List<ITriggerListener>();
        var listeners = new List<(ITriggerListener Listener, IMatcher<TriggerKey>[] Matchers)>();

        foreach (var registration in Registrations<TriggerListenerRegistration>())
        {
            var listener = registration.CreateListener(serviceProvider);
            configured.Add(listener);
            listeners.Add((listener, registration.Matchers));
        }

        RejectDuplicateNames(scheduler, "trigger", configured);

        foreach (var listener in ListenerServices<ITriggerListener>())
        {
            if (AlreadyConfigured(configured, listener))
            {
                continue;
            }

            listeners.Add((listener, []));
        }

        foreach (var listener in PropertyListenerFactory.Create<ITriggerListener>(
                     serviceProvider, properties, LegacyPropertyKeys.TriggerListenerPrefix))
        {
            if (AlreadyConfigured(configured, listener))
            {
                continue;
            }

            listeners.Add((listener, [Matchers.AllTriggers()]));
        }

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
                configuration.Name,
                configuration.Calendar,
                configuration.Options,
                cancellationToken)
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
    /// Whether a listener contributed by a plain service registration or by a
    /// <c>quartz.*Listener.*</c> key is one the builder already contributed.
    /// </summary>
    /// <remarks>
    /// Registering a listener through the builder and as a service is a normal thing to do — the builder
    /// registration is how it gets its matchers, the service registration is how its dependencies get
    /// injected — and it used to be recognised, by comparing the declared listener type. That comparison
    /// went away with the type-keyed configurations, which left the same listener contributed twice: for
    /// job and trigger listeners the second copy replaces the first and drops its matchers, and for
    /// scheduler listeners both are notified. The builder registration wins because it is the one that
    /// carries matchers.
    /// </remarks>
    private static bool AlreadyConfigured<TListener>(List<TListener> configured, TListener listener)
        where TListener : class
    {
        foreach (var candidate in configured)
        {
            if (ReferenceEquals(candidate, listener) || candidate.GetType() == listener.GetType())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refuses two builder registrations of the same kind that answer to the same name.
    /// </summary>
    /// <remarks>
    /// A listener manager holds one listener per name and replaces on collision, and a replacement that
    /// carries no matchers drops the matchers of the listener it replaced. Two registrations that happen
    /// to produce the same name therefore quietly become one — the very ambiguity that carrying the
    /// pairing in the registration removes. Say so instead of applying only one of them.
    /// </remarks>
    /// <remarks>
    /// Only builder registrations are checked. A listener that also arrives as a service or by property is
    /// recognised as the same listener by <see cref="AlreadyConfigured{TListener}"/> and never reaches
    /// here, because that is a configuration that has always worked rather than an ambiguous one.
    /// </remarks>
    private static void RejectDuplicateNames<TListener>(IScheduler scheduler, string kind, List<TListener> configured)
        where TListener : class
    {
        var seen = new Dictionary<string, TListener>(StringComparer.Ordinal);
        foreach (var listener in configured)
        {
            var name = listener switch
            {
                IJobListener job => job.Name,
                ITriggerListener trigger => trigger.Name,
                _ => null
            };

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
