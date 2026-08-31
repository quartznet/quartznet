using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Diagnostics;

namespace Quartz;

/// <summary>
/// Builds a scheduler without an application-supplied dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// Console applications, tests and anything else without a host use this instead of registering Quartz
/// into their own container. It is not a second configuration API: <see cref="Create"/> hands the
/// callback an <see cref="IQuartzBuilder"/>, the very one <c>AddQuartz(q =&gt; …)</c> hands out, over a
/// container it creates itself. The two paths are the same call written around a different receiver, so
/// whatever works under a host works here and they cannot drift apart — there is one set of members to
/// keep in step, and this type re-declares none of them.
/// </para>
/// <para>
/// What it adds is what a standalone caller needs and a host already has: the terminal methods
/// <see cref="Build"/> and <see cref="BuildScheduler"/>, and the two ways to say where configuration
/// comes from when it does not come from code — <see cref="UseProperties(NameValueCollection)"/> and
/// <see cref="UseConfiguration"/>.
/// </para>
/// <para>
/// The builder owns the <see cref="IServiceProvider"/> it creates and disposes it when the returned
/// factory is disposed, so callers that never dispose behave exactly as they did with the old
/// process-lifetime scheduler.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// IScheduler scheduler = await QuartzSchedulerBuilder
///     .Create(q => q
///         .ConfigureScheduler(options => options.InstanceName = "reporting")
///         .UseDefaultThreadPool(maxConcurrency: 20)
///         .UseInMemoryStore())
///     .BuildScheduler();
/// </code>
/// </example>
public sealed class QuartzSchedulerBuilder
{
    private readonly ServiceCollection services = [];
    private NameValueCollection? properties;
    private IConfiguration? configuration;

    private QuartzSchedulerBuilder(Action<IQuartzBuilder>? configure)
    {
        configure?.Invoke(new QuartzBuilder(services, schedulerKey: null));
    }

    /// <summary>
    /// Creates a new builder and configures the scheduler it will build.
    /// </summary>
    /// <remarks>
    /// The callback is the same one <c>AddQuartz</c> takes, and it runs immediately, as it does there.
    /// Leaving it out describes a scheduler that is configured entirely by
    /// <see cref="UseProperties(NameValueCollection)"/> or <see cref="UseConfiguration"/> — again as
    /// <c>AddQuartz()</c> does — and the defaults answer for anything neither of those mentions.
    /// </remarks>
    /// <param name="configure">Configures the scheduler.</param>
    public static QuartzSchedulerBuilder Create(Action<IQuartzBuilder>? configure = null)
    {
        return new QuartzSchedulerBuilder(configure);
    }

    /// <summary>
    /// Configures the scheduler from flat <c>quartz.*</c> property keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the code-free path a properties file or an environment-derived bag takes, and the
    /// standalone counterpart of <c>AddQuartz(properties)</c>. The keys are translated into the same
    /// typed options and registrations everything else produces, so a scheduler configured this way is
    /// the same scheduler.
    /// </para>
    /// <para>
    /// The parameter is the shape every dictionary already has, so a
    /// <see cref="Dictionary{TKey,TValue}"/>, an <see cref="IReadOnlyDictionary{TKey,TValue}"/> and
    /// <see cref="QuartzOptions.Properties"/> all go in without a conversion step.
    /// </para>
    /// <para>
    /// Configuration written in code wins, whichever order the two are applied in: values from the
    /// properties are applied before anything <see cref="Create"/> was told, and implementations they
    /// name are registered after — registration being first-wins and configuration last-wins.
    /// </para>
    /// <para>
    /// Keys are checked against the ones Quartz reads, so a misspelling is reported rather than
    /// silently ignored. Set <c>quartz.checkConfiguration</c> to <see langword="false"/> to allow keys
    /// of your own.
    /// </para>
    /// </remarks>
    /// <param name="properties">The flat <c>quartz.*</c> properties.</param>
    public QuartzSchedulerBuilder UseProperties(IEnumerable<KeyValuePair<string, string?>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return UsePropertyBag(QuartzConfigurationHelper.ToNameValueCollection(properties));
    }

    /// <summary>
    /// Configures the scheduler from flat <c>quartz.*</c> property keys held in a
    /// <see cref="NameValueCollection"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="NameValueCollection"/> is what a caller migrating from 3.x already holds — it is what
    /// <c>StdSchedulerFactory</c> took — so it stays a single call. Everything else about it is
    /// <see cref="UseProperties(IEnumerable{KeyValuePair{string, string}})"/>, which this forwards to.
    /// </remarks>
    /// <param name="properties">The flat <c>quartz.*</c> properties.</param>
    public QuartzSchedulerBuilder UseProperties(NameValueCollection properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        // Copied, so a caller that goes on to reuse its collection cannot change what this scheduler
        // was configured with after the fact.
        return UsePropertyBag(new NameValueCollection(properties));
    }

    private QuartzSchedulerBuilder UsePropertyBag(NameValueCollection properties)
    {
        LegacyPropertyKeys.Validate(properties);
        this.properties = properties;
        return this;
    }

    /// <summary>
    /// Configures the scheduler from a configuration section, the standalone counterpart of
    /// <c>AddQuartz(configuration)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The section is read exactly as it is under a host: hierarchical sections such as
    /// <c>Scheduler</c> and <c>ThreadPool</c> bind onto the typed options, a <c>Schedule</c> section
    /// becomes jobs and triggers, and flat <c>quartz.*</c> keys still mean what they always did. There
    /// is no flattening step for a caller to write.
    /// </para>
    /// <para>
    /// Configuration written in code wins, as it does everywhere else: the section is applied before
    /// anything <see cref="Create"/> was told.
    /// </para>
    /// </remarks>
    /// <param name="configuration">
    /// The Quartz configuration section, typically <c>configuration.GetSection("Quartz")</c>.
    /// </param>
    public QuartzSchedulerBuilder UseConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this.configuration = configuration;
        return this;
    }

    /// <summary>
    /// Builds the scheduler factory, along with the container backing it.
    /// </summary>
    /// <remarks>
    /// The returned factory owns that container, so disposing it shuts the scheduler down and disposes
    /// everything the container built. See <see cref="StandaloneSchedulerFactory"/>.
    /// </remarks>
    public StandaloneSchedulerFactory Build()
    {
        ApplyProperties();

        BridgeLoggingToLogProvider();

        // Defaults last, so anything configured above replaces rather than loses to them.
        services.AddQuartzScheduler();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        return new StandaloneSchedulerFactory(provider);
    }

    /// <summary>
    /// Builds the scheduler.
    /// </summary>
    /// <remarks>
    /// The factory that owns the container is dropped, so the container outlives every reference to it:
    /// shutting this scheduler down is still <see cref="IScheduler.Shutdown"/>, but nothing will ever
    /// dispose what built it. That is the right trade for a scheduler that lives as long as the process
    /// and the wrong one for anything shorter-lived — use <see cref="Build"/> and keep the factory there.
    /// </remarks>
    public ValueTask<IScheduler> BuildScheduler(CancellationToken cancellationToken = default)
    {
        return Build().GetScheduler(cancellationToken);
    }

    /// <summary>
    /// Applies the flat properties, ahead of the configuration written in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves go in at opposite ends because their precedence rules are opposites. Options are
    /// last-wins, so the property-derived ones are inserted at the front of the collection and anything
    /// configured in code is applied over them. Registrations are first-wins, so the implementations the
    /// properties name are appended, and an implementation chosen in code beats one named by a string.
    /// Applying them where <c>UseProperties</c> was called would instead make precedence depend
    /// on the order the builder happened to be told things in.
    /// </para>
    /// <para>
    /// This runs even when no properties were given, because keys can also arrive by configuring
    /// <see cref="QuartzOptions.Properties"/> on <see cref="IQuartzBuilder.Services"/> — which is only
    /// readable once the container exists, and is what <c>ApplyFromQuartzOptions</c> is for.
    /// </para>
    /// </remarks>
    private void ApplyProperties()
    {
        NameValueCollection configured = [];
        ServiceCollection seed = [];

        if (configuration is not null)
        {
            // The typed binder first, then everything the section says in flat form — the same pair, in
            // the same order, that AddQuartz(configuration) applies. Every section is flattened,
            // including the ones that also bind, so a setting that has no options type of its own is
            // still read.
            seed.BindQuartzOptions(configuration);
            JsonSchedulingHelper.ConfigureOptionsFromConfiguration(seed, configuration, Options.DefaultName);
            QuartzConfigurationHelper.PopulateProperties(configuration, configured);
        }

        foreach (var key in properties?.AllKeys ?? [])
        {
            if (key is not null)
            {
                // A bag handed in by hand is the more specific of the two, so it wins where both speak.
                configured[key] = properties![key];
            }
        }

        // Plugins, execution limits and scheduler content are read from QuartzOptions, so the property
        // bag has to be there as well as bound onto the typed options.
        seed.Configure<QuartzOptions>(options =>
        {
            foreach (var key in configured.AllKeys)
            {
                if (key is not null)
                {
                    options.Properties[key] = configured[key];
                }
            }
        });

        QuartzPropertyBridge.ApplyOptions(seed, configured);
        QuartzPropertyBridge.ApplyFromQuartzOptions(seed);

        for (var i = 0; i < seed.Count; i++)
        {
            services.Insert(i, seed[i]);
        }

        QuartzPropertyBridge.ApplyRegistrations(services, configured);
    }

    /// <summary>
    /// Points this container's logging at <see cref="LogProvider" /> when the caller configured none of
    /// its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything Quartz builds is injected an <see cref="ILogger" /> from the container. That is what a
    /// host is for, and a host's container has the application's logging providers in it. This container
    /// is one the builder created, and a standalone application says where its logging goes by calling
    /// <see cref="LogProvider.SetLogProvider" /> — so without this, every injected logger here would be
    /// a real logger writing to a factory with nothing behind it.
    /// </para>
    /// <para>
    /// Skipped as soon as a logging provider has been registered on
    /// <see cref="IQuartzBuilder.Services" />, because then the caller has said where logging goes and
    /// the container's own factory is the answer.
    /// </para>
    /// </remarks>
    private void BridgeLoggingToLogProvider()
    {
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(ILoggerProvider)))
        {
            return;
        }

        // Replace rather than TryAdd: a caller who called AddLogging() without adding a provider has
        // already registered the factory this stands in for.
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(LogProviderLoggerFactory.Instance));
    }
}
