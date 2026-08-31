using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Registers Quartz schedulers, and the hosted service that runs them, into an application's
/// container.
/// </summary>
/// <remarks>
/// The other half of this class lives beside the hosted service it registers, in
/// <c>Hosting/QuartzServiceCollectionExtensions.cs</c>. What a scheduler <em>carries</em> — jobs,
/// triggers, calendars — is added through <see cref="QuartzBuilderExtensions"/>, which extends
/// <see cref="IQuartzBuilder"/> rather than <see cref="IServiceCollection"/>.
/// </remarks>
public static partial class QuartzServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Quartz scheduler.
    /// </summary>
    /// <inheritdoc cref="AddQuartz(IServiceCollection, IConfiguration, Action{IQuartzBuilder})" path="/remarks" />
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        Action<IQuartzBuilder>? configure = null)
    {
        return AddQuartzScheduler(services, schedulerName: null, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Registers a Quartz scheduler, seeded with flat <c>quartz.*</c> properties.
    /// </summary>
    /// <inheritdoc cref="AddQuartz(IServiceCollection, IConfiguration, Action{IQuartzBuilder})" path="/remarks" />
    /// <param name="services">The service collection to register into.</param>
    /// <param name="properties">
    /// The flat <c>quartz.*</c> properties, in the shape every dictionary already has — a
    /// <see cref="Dictionary{TKey,TValue}"/>, an <see cref="IReadOnlyDictionary{TKey,TValue}"/> and
    /// <see cref="QuartzOptions.Properties"/> all go in without a conversion step. They are checked
    /// against the keys Quartz reads, as they are on the standalone builder, so a misspelling — or a key
    /// 4.0 stopped reading — is reported rather than silently ignored. Set
    /// <c>quartz.checkConfiguration</c> to <see langword="false"/> to allow keys of your own.
    /// </param>
    /// <param name="configure">Configures the scheduler.</param>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        IEnumerable<KeyValuePair<string, string?>> properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return AddQuartzScheduler(services, schedulerName: null, PropertyBag(properties), configure);
    }

    /// <summary>
    /// Registers a Quartz scheduler, seeded with flat <c>quartz.*</c> properties held in a
    /// <see cref="NameValueCollection"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="NameValueCollection"/> is what a caller migrating from 3.x already holds — it is what
    /// <c>StdSchedulerFactory</c> took — so it stays a single call. Everything else about it is
    /// <see cref="AddQuartz(IServiceCollection, IEnumerable{KeyValuePair{string, string}}, Action{IQuartzBuilder})"/>,
    /// which this forwards to.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="properties">The flat <c>quartz.*</c> properties.</param>
    /// <param name="configure">Configures the scheduler.</param>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return AddQuartzScheduler(services, schedulerName: null, PropertyBag(properties), configure);
    }

    /// <summary>
    /// Registers a Quartz scheduler from a configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hierarchical sections such as <c>Scheduler</c> and <c>ThreadPool</c> bind onto the typed options
    /// directly. Flat <c>quartz.*</c> keys are still accepted and mean the same thing. Several
    /// schedulers described by one section are registered with
    /// <see cref="AddQuartzSchedulers(IServiceCollection, IConfiguration, Action{IQuartzBuilder})"/>.
    /// </para>
    /// <para>
    /// A scheduler is described by six things, applied in this order — which is what decides who wins
    /// when two of them say something about the same setting:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// The flat <c>quartz.*</c> properties are recorded in <see cref="QuartzOptions.Properties"/>, so
    /// the parts that read the property bag directly — plugins, execution limits — can see them.
    /// </description></item>
    /// <item><description>
    /// Those properties are translated into typed options.
    /// </description></item>
    /// <item><description>
    /// Properties contributed later by configuring <see cref="QuartzOptions"/> are translated too, which
    /// is what makes <c>services.Configure&lt;QuartzOptions&gt;</c> equivalent to passing them here.
    /// </description></item>
    /// <item><description>
    /// The <paramref name="configure"/> callback runs. Options are <em>last-wins</em>, so anything it
    /// sets is applied over the property-derived values: configuration written in code beats a string.
    /// </description></item>
    /// <item><description>
    /// The implementations the properties name — a job store type, a thread pool type, a serializer —
    /// are registered. Registration is <em>first-wins</em>, so this comes after the callback: a store
    /// chosen by <c>UsePersistentStore</c> has to beat a leftover <c>quartz.jobStore.type</c> from an
    /// old configuration file. Code beats strings in both directions, opposite orders notwithstanding.
    /// </description></item>
    /// <item><description>
    /// Quartz's own defaults are registered last, so an explicitly configured job store, thread pool or
    /// serializer is never beaten to the registration by the fallback it was meant to replace.
    /// </description></item>
    /// </list>
    /// <para>
    /// The scheduler this registers is the container's unkeyed <see cref="IScheduler"/>, which is what
    /// <c>GetRequiredService&lt;IScheduler&gt;()</c> answers with. Registering it when something else
    /// already owns that slot throws an <see cref="InvalidOperationException"/> rather than quietly
    /// leaving "the scheduler" meaning the other one; <c>AddQuartzHttpClient</c> is the one in the box
    /// that takes it. The other order is fine and needs no thought: <c>AddQuartz()</c> followed by
    /// <c>AddQuartzHttpClient(…)</c> leaves the local default scheduler unkeyed and the remote one
    /// reachable as <c>GetRequiredKeyedService&lt;IScheduler&gt;(schedulerName)</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.GetSection("Schedulers").Exists())
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section contains a 'Schedulers' sub-section, which describes " +
                "several named schedulers rather than one. Call AddQuartzSchedulers(configuration) to " +
                "register them, or AddQuartz(name, configuration) to register one of them.");
        }

        // Bound before the callback runs, so configuration is the starting point and code overrides it.
        services.BindQuartzOptions(configuration);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, configuration, Options.DefaultName);
        AddQuartzScheduler(services, schedulerName: null, LegacyProperties(configuration), configure);
        return services;
    }

    /// <summary>
    /// Registers one named Quartz scheduler per child of the section's <c>Schedulers</c> sub-section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each child's key is the scheduler's name, and its contents are that scheduler's configuration —
    /// exactly what <c>AddQuartz(name, section)</c> would be given. The fan-out is its own method
    /// because registering several schedulers is a different act from registering one, and reading it
    /// out of the shape of a configuration file made <c>AddQuartz</c> mean two things depending on data
    /// it was handed.
    /// </para>
    /// <para>
    /// The section is a parameter because this receiver has no configuration of its own, which is the
    /// rule every method here follows: a service collection is handed its configuration and never goes
    /// looking for one, so <c>AddQuartz(configure)</c> means a scheduler configured entirely in code
    /// rather than one that quietly reads whatever <see cref="IConfiguration"/> the container holds. On
    /// a host application builder, which does hold configuration, this is
    /// <c>builder.AddQuartzSchedulers(…)</c> and takes no section — the same difference
    /// <c>AddQuartz</c> has between the two receivers.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The Quartz configuration section, containing a <c>Schedulers</c> sub-section.
    /// </param>
    /// <param name="configure">Applied to every scheduler described by the section.</param>
    public static IServiceCollection AddQuartzSchedulers(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var schedulers = configuration.GetSection("Schedulers");
        if (!schedulers.Exists())
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section has no 'Schedulers' sub-section, so it describes one " +
                "scheduler rather than several. Call AddQuartz(configuration) instead.");
        }

        if (HasDirectSchedulerConfiguration(configuration))
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section contains both a 'Schedulers' sub-section and direct " +
                "scheduler configuration. Use one or the other.");
        }

        if (configuration.GetSection("Schedule").Exists() || configuration.GetSection("Scheduling").Exists())
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section contains a 'Schedulers' sub-section and a top-level " +
                "'Schedule' section. Jobs and triggers belong to a specific scheduler, so move them " +
                "under the scheduler they belong to.");
        }

        foreach (var scheduler in schedulers.GetChildren())
        {
            AddQuartz(services, scheduler.Key, scheduler, configure);
        }

        return services;
    }

    /// <summary>
    /// Configures every Quartz scheduler in the container, whenever it was registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the options pattern's <c>ConfigureAll</c>, for schedulers. <paramref name="configure"/> is
    /// applied to every scheduler <c>AddQuartz()</c>, <c>AddQuartz(name, …)</c> or
    /// <c>AddQuartzSchedulers(…)</c> registers in this container: the ones already registered when this
    /// is called, and the ones registered after it. <strong>The order of the calls does not matter</strong>
    /// — which is the point, since a package that adds something to every scheduler cannot know whether
    /// the application registers its schedulers before or after calling it.
    /// </para>
    /// <para>
    /// The delegate is given a builder <em>per scheduler</em>, so what it registers lands under that
    /// scheduler's own service key, exactly as if it had been written inside that scheduler's
    /// <c>AddQuartz(name, q =&gt; …)</c> callback. A plugin or listener added here is therefore one
    /// instance per scheduler, each initialized with the name of the scheduler it belongs to — not one
    /// instance shared between them, which would leave each scheduler but the last with a component
    /// pointing at somebody else's.
    /// </para>
    /// <para>
    /// It runs after each scheduler's own configuration callback, which is what makes the order of the two
    /// calls immaterial: a scheduler registered later is configured then, and one registered earlier is
    /// configured here, and both are after its own callback either way. The usual precedence follows —
    /// registration is first-wins, so a component a scheduler chose for itself is not replaced by one
    /// chosen here; options are last-wins, so a value set here overrides the same option set on one
    /// scheduler, exactly as <c>ConfigureAll&lt;TOptions&gt;</c> overrides an earlier named
    /// <c>Configure</c>.
    /// </para>
    /// <para>
    /// Remote schedulers registered with <c>AddQuartzHttpClient</c> are not built by a builder and are
    /// skipped. Calling this when no scheduler is registered at all is not an error: the delegate simply
    /// applies to nothing.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Applied to every scheduler in the container.</param>
    public static IServiceCollection ConfigureAllQuartzSchedulers(
        this IServiceCollection services,
        Action<IQuartzBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        SchedulerNameRegistry registry = SchedulerNameRegistry.For(services);

        // Recorded first, so that a scheduler this delegate goes on to register is covered by it too.
        registry.AddConfigureAll(configure);

        // The schedulers already registered: their AddQuartz call has been and gone, so nothing else
        // will carry this to them.
        if (registry.HasDefaultScheduler)
        {
            registry.Apply(configure, services, schedulerName: null);
        }

        // Indexed, because applying this to one scheduler may register another.
        for (int i = 0; i < registry.Names.Count; i++)
        {
            registry.Apply(configure, services, registry.Names[i]);
        }

        return services;
    }

    /// <summary>
    /// Registers a named Quartz scheduler, so several independent schedulers can share a container.
    /// </summary>
    /// <remarks>
    /// The name is the scheduler's instance name, the key its components are registered under, and the
    /// name of its options, so its registrations and its configuration always agree.
    /// </remarks>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddQuartzScheduler(services, name, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Registers a named Quartz scheduler, seeded with flat <c>quartz.*</c> properties.
    /// </summary>
    /// <inheritdoc cref="AddQuartz(IServiceCollection, IEnumerable{KeyValuePair{string, string}}, Action{IQuartzBuilder})" path="/param[@name='properties']" />
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        IEnumerable<KeyValuePair<string, string?>> properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(properties);
        return AddQuartzScheduler(services, name, PropertyBag(properties), configure);
    }

    /// <summary>
    /// Registers a named Quartz scheduler, seeded with flat <c>quartz.*</c> properties held in a
    /// <see cref="NameValueCollection"/>.
    /// </summary>
    /// <inheritdoc cref="AddQuartz(IServiceCollection, NameValueCollection, Action{IQuartzBuilder})" path="/remarks" />
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(properties);
        return AddQuartzScheduler(services, name, PropertyBag(properties), configure);
    }

    /// <summary>
    /// Registers a named Quartz scheduler from a configuration section.
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configuration);

        // Callers may pass either the scheduler's own section or the root section containing
        // "Schedulers:{name}", so resolve to whichever actually holds this scheduler's settings.
        var own = configuration.GetSection("Schedulers").GetSection(name);
        var effective = own.Exists() ? own : configuration;

        services.BindQuartzOptions(effective, name);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, effective, name);
        AddQuartzScheduler(services, name, LegacyProperties(effective), configure);
        return services;
    }

    /// <summary>
    /// Takes the caller's own property bag as the flat collection the readers use, and checks its keys.
    /// </summary>
    /// <remarks>
    /// Copied rather than captured. The registration phases read the bag from closures that run later —
    /// some of them only when the container resolves options — so a caller that went on to change its own
    /// collection would change what the scheduler was configured with, long after <c>AddQuartz</c>
    /// returned. The standalone builder has always copied for this reason, and these doors now agree.
    /// </remarks>
    private static NameValueCollection PropertyBag(IEnumerable<KeyValuePair<string, string?>> properties)
    {
        return Checked(QuartzConfigurationHelper.ToNameValueCollection(properties));
    }

    /// <inheritdoc cref="PropertyBag(IEnumerable{KeyValuePair{string, string}})" />
    private static NameValueCollection PropertyBag(NameValueCollection properties)
    {
        return Checked(new NameValueCollection(properties));
    }

    private static NameValueCollection Checked(NameValueCollection properties)
    {
        LegacyPropertyKeys.Validate(properties);
        return properties;
    }

    /// <summary>
    /// Returns everything a configuration section says, in the legacy flat form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every section is flattened, including the ones that also bind onto typed options. Sending the
    /// same value through both readers is deliberate: the typed binder covers the settings that have an
    /// option of their own, and the legacy adapter covers the rest — the type names that select an
    /// implementation, and the knobs of components that have no options type at all. Splitting the
    /// sections between the two readers instead leaves anything that falls between them read by nobody,
    /// which is how a documented <c>JobStore:Type</c> came to be accepted and then ignored.
    /// </para>
    /// <para>
    /// A key both readers would write identically is the exception, and there is one: it is dropped from
    /// the flattened form rather than read twice. The list, and why nothing else is on it, is in
    /// <see cref="QuartzConfigurationHelper" />.
    /// </para>
    /// <para>
    /// The two readers do disagree about the shape of a duration — <c>00:00:30</c> to the binder, a count
    /// of milliseconds to the legacy format. That is settled in the adapter, which accepts both
    /// spellings, and by ordering: the adapter is applied after the binder, so its reading stands.
    /// </para>
    /// </remarks>
    private static NameValueCollection LegacyProperties(IConfiguration configuration)
    {
        var properties = new NameValueCollection();
        QuartzConfigurationHelper.PopulateProperties(configuration, properties);
        return properties;
    }

    private static bool HasDirectSchedulerConfiguration(IConfiguration configuration)
    {
        foreach (var child in configuration.GetChildren())
        {
            if (string.Equals(child.Key, "Schedulers", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.Key, "Schedule", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.Key, "Scheduling", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Registers one scheduler: its services, its configuration, and whatever the caller adds to it.
    /// </summary>
    /// <remarks>
    /// The six phases below, and why they are in this order, are documented on
    /// <see cref="AddQuartz(IServiceCollection, IConfiguration, Action{IQuartzBuilder})"/> — where a
    /// caller can read them.
    /// </remarks>
    private static IServiceCollection AddQuartzScheduler(
        IServiceCollection services,
        string? schedulerName,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure)
    {
        services.AddOptions();

        SchedulerNameRegistry registry = SchedulerNameRegistry.For(services);
        var optionsName = schedulerName ?? Options.DefaultName;

        // Before anything is registered, so a container that cannot hold this scheduler is not left half
        // configured by the attempt.
        if (schedulerName is null && !registry.HasDefaultScheduler)
        {
            ThrowIfTheUnkeyedSchedulerIsTaken(services);
        }

        // Phase 1.
        services.Configure<QuartzOptions>(optionsName, options =>
        {
            foreach (var key in properties.AllKeys)
            {
                if (key is not null)
                {
                    options.Properties[key] = properties[key];
                }
            }
        });

        // Phases 2 and 3: options are last-wins, so the property-derived ones go in before the callback.
        QuartzPropertyBridge.ApplyOptions(services, properties, schedulerName);
        QuartzPropertyBridge.ApplyFromQuartzOptions(services, schedulerName);

        // Phase 4.
        configure?.Invoke(new QuartzBuilder(services, schedulerName));

        // Phase 4, for everyone: what ConfigureAllQuartzSchedulers said about every scheduler in the
        // container. After this scheduler's own callback, which is where a scheduler registered before
        // that call gets it too, so the order the two calls were written in does not decide anything.
        registry.ApplyConfigureAll(services, schedulerName);

        // Phase 5: registration is first-wins, so the implementations named by keys go in after it.
        QuartzPropertyBridge.ApplyRegistrations(services, properties, schedulerName);

        // Phase 6.
        services.AddQuartzScheduler(schedulerName);

        if (schedulerName is not null)
        {
            registry.Add(schedulerName);
        }

        return services;
    }

    /// <summary>
    /// Refuses to register the default scheduler when something else already owns the unkeyed
    /// <see cref="IScheduler"/>.
    /// </summary>
    /// <remarks>
    /// Registration is first-wins, so whichever of the two ran first would answer
    /// <c>GetRequiredService&lt;IScheduler&gt;()</c> and the other would be unreachable without a key.
    /// <c>AddQuartzHttpClient</c> registers the unkeyed slot as well as its own name, so
    /// <c>AddQuartzHttpClient(…)</c> followed by <c>AddQuartz()</c> silently made "the scheduler" the
    /// remote one — a program that then scheduled a job sent it over the wire to somebody else's process.
    /// Said here rather than left to be discovered, because nothing downstream can tell the two apart.
    /// The opposite order needs no report: the local default owns the unkeyed slot, the remote scheduler
    /// is still reachable under its name, and that is what both methods document.
    /// </remarks>
    private static void ThrowIfTheUnkeyedSchedulerIsTaken(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IScheduler) && !descriptor.IsKeyedService)
            {
                throw new InvalidOperationException(
                    "An IScheduler is already registered in this container without a service key, so "
                    + "AddQuartz() has nowhere to put the default scheduler: registration is first-wins, "
                    + "and GetRequiredService<IScheduler>() would answer with the registration that is "
                    + "already there. AddQuartzHttpClient(...) makes one — call it after AddQuartz() "
                    + "instead of before, and its remote scheduler stays reachable either way as "
                    + "GetRequiredKeyedService<IScheduler>(schedulerName). Register this scheduler with "
                    + "AddQuartz(name, ...) if it should have a name of its own.");
            }
        }
    }
}
