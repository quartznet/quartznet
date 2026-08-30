using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Quartz.Diagnostics;

namespace Quartz;

/// <summary>
/// Registers a Quartz persistent job store from an Aspire connection name.
/// </summary>
/// <remarks>
/// <para>
/// This is the page <c>how-tos/aspire.md</c> already documented, turned into one call. An Aspire AppHost
/// declares a database and hands the worker its connection string as <c>ConnectionStrings:&lt;name&gt;</c>;
/// everything after that — which driver delegate speaks that database's SQL, whether connections come
/// from a <c>DbDataSource</c> the container holds or from the string itself, and naming Quartz's two
/// telemetry signals to the pipeline ServiceDefaults built — follows from the name, and is what this
/// does.
/// </para>
/// <para>
/// It is <em>additive</em> and order-independent. The store is contributed through
/// <c>ConfigureAllQuartzSchedulers</c>, so this may be called before or after <c>AddQuartz</c> and the
/// container comes out the same; the scheduler is still configured by <c>AddQuartz</c> and the
/// <c>Quartz</c> configuration section, and nothing here replaces either. What this call decides is only
/// what an Aspire connection is evidence of.
/// </para>
/// <para>
/// There is no <c>AddKeyedQuartzPersistentStore</c>, which every other Aspire client integration has.
/// The keyed form exists so that an application can hold two of a thing, and Quartz already has an axis
/// for that which is not the container's: a second scheduler, registered by name with
/// <c>AddQuartz(name, …)</c>. <see cref="QuartzAspireSettings.SchedulerName"/> is how a second call to
/// this method says which of them it means, so the two databases end up on two schedulers rather than on
/// two keyed copies of one.
/// </para>
/// </remarks>
public static class QuartzAspireHostApplicationBuilderExtensions
{
    /// <summary>
    /// The configuration section these settings are bound from, before the per-connection section
    /// beneath it. The name is Aspire's convention — <c>Aspire:&lt;integration&gt;</c> — and the second
    /// segment is the package's own noun rather than a driver's, because this integration is about
    /// Quartz and not about any one database.
    /// </summary>
    internal const string ConfigurationSectionName = "Aspire:Quartz";

    /// <summary>
    /// What choosing the dialect from a provider name costs a trimmed application, repeated from the
    /// overloads it calls so that the warning arrives where the decision is made.
    /// </summary>
    private const string NamesTheDriversTypes =
        "The database is chosen from the connection string or from QuartzAspireSettings.Provider, and on the "
        + "connection-string path Quartz names the driver's connection, command and parameter types as "
        + "strings, so a trimmed application has no guarantee they survived. Registering a DbDataSource in "
        + "the container removes that: connections come from it, and only the half of the driver description "
        + "that names no type is read. Otherwise configure the store directly, with the overload that takes "
        + "the driver's DbProviderFactory.";

    /// <summary>
    /// Gives every Quartz scheduler in the application a persistent job store on the database Aspire
    /// injected under <paramref name="connectionName"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The settings are read from <c>Aspire:Quartz</c>, then from <c>Aspire:Quartz:&lt;connectionName&gt;</c>
    /// over it, then <c>ConnectionStrings:&lt;connectionName&gt;</c> supplies the connection string when
    /// there is one, and <paramref name="configureSettings"/> has the last word.
    /// </para>
    /// <para>
    /// Where connections come from is decided here, against the services registered so far, so the
    /// answer does not depend on when the per-scheduler configuration happens to run. That makes the
    /// order of <em>this</em> call and the client integration registering the data source significant,
    /// and only that one: register the data source first, as Aspire's own samples do.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">
    /// The name the AppHost gave the database resource, which is the name its connection string arrives
    /// under and the service key a keyed <c>DbDataSource</c> would be registered with.
    /// </param>
    /// <param name="configureSettings">Applied last, over everything configuration said.</param>
    /// <exception cref="SchedulerConfigException">
    /// No provider was named and the connection string's shape matches no database, or more than one.
    /// </exception>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    public static IHostApplicationBuilder AddQuartzPersistentStore(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<QuartzAspireSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        QuartzAspireSettings settings = ReadSettings(builder.Configuration, connectionName, configureSettings);
        string provider = ResolveProvider(settings, connectionName);
        Action<DataSourceOptions> connection = SelectConnection(builder.Services, provider, connectionName, settings);

        string? schedulerName = string.IsNullOrWhiteSpace(settings.SchedulerName) ? null : settings.SchedulerName;
        string? tablePrefix = string.IsNullOrWhiteSpace(settings.TablePrefix) ? null : settings.TablePrefix;
        bool clustered = settings.Clustered;
        bool healthChecks = !settings.DisableHealthChecks;

        builder.Services.ConfigureAllQuartzSchedulers(quartz =>
        {
            // A settings object naming a scheduler is talking about that one. Naming none is the single
            // scheduler an application normally has, said without having to know its name.
            if (schedulerName is not null && !string.Equals(quartz.SchedulerName, schedulerName, StringComparison.Ordinal))
            {
                return;
            }

            quartz.UsePersistentStore(store =>
            {
                UseDialect(store, provider, connection);

                if (tablePrefix is not null)
                {
                    store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                }

                if (clustered)
                {
                    store.UseClustering();
                }
            });

            if (clustered)
            {
                GenerateAnInstanceIdUnlessOneWasChosen(quartz);
            }

            if (healthChecks)
            {
                quartz.AddQuartzHealthChecks();
            }
        });

        AddTelemetry(builder.Services, settings);

        return builder;
    }

    /// <summary>
    /// Makes a clustered scheduler derive an instance id, unless the application chose one itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cluster is a set of nodes that recognise their own check-in row and their own fired triggers by
    /// <c>InstanceId</c>, and every scheduler starts life with the same one —
    /// <see cref="QuartzSchedulerOptions.DefaultInstanceId"/>, <c>NON_CLUSTERED</c>. Under Aspire that is
    /// not a hypothetical: <c>WithReplicas(2)</c> is one call, a replica set has no identity of its own to
    /// borrow, and a cluster whose nodes all carry one id is the worst failure this area has. So
    /// <see cref="QuartzAspireSettings.Clustered"/> supplies the other half of what clustering needs
    /// rather than documenting a trap beside it.
    /// </para>
    /// <para>
    /// It only fills a gap. An application that set <c>GenerateInstanceId</c> itself is already doing
    /// this, and one that set <c>InstanceId</c> has said what its nodes are called — an ordinal deployment
    /// or a hostname it trusts — so neither is touched. Reading those values here is sound because this
    /// runs from <c>ConfigureAllQuartzSchedulers</c>, which <c>AddQuartz</c> applies <em>after</em> the
    /// scheduler's own callback, the property bridge and the configuration binding; options are last-wins,
    /// so what this delegate sees is everything the application said.
    /// </para>
    /// </remarks>
    private static void GenerateAnInstanceIdUnlessOneWasChosen(IQuartzBuilder quartz)
    {
        quartz.ConfigureScheduler(options =>
        {
            if (!options.GenerateInstanceId
                && string.Equals(options.InstanceId, QuartzSchedulerOptions.DefaultInstanceId, StringComparison.Ordinal))
            {
                options.GenerateInstanceId = true;
            }
        });
    }

    /// <summary>
    /// Reads the settings: the shared section, the connection's own section over it, the injected
    /// connection string, and then the caller.
    /// </summary>
    /// <remarks>
    /// The order is the one every first-party Aspire client integration uses, and each step is more
    /// specific than the one before it. The connection string comes after both sections because
    /// <c>ConnectionStrings:&lt;name&gt;</c> is what the AppHost actually injected, and a stale
    /// <c>ConnectionString</c> left in an appsettings file should not beat it.
    /// </remarks>
    private static QuartzAspireSettings ReadSettings(
        IConfiguration configuration,
        string connectionName,
        Action<QuartzAspireSettings>? configureSettings)
    {
        QuartzAspireSettings settings = new();

        IConfigurationSection section = configuration.GetSection(ConfigurationSectionName);
        section.Bind(settings);
        section.GetSection(connectionName).Bind(settings);

        if (configuration.GetConnectionString(connectionName) is { Length: > 0 } connectionString)
        {
            settings.ConnectionString = connectionString;
        }

        configureSettings?.Invoke(settings);

        return settings;
    }

    /// <summary>
    /// Which driver reaches the database: what the settings said, or what the connection string's shape
    /// says.
    /// </summary>
    /// <remarks>
    /// A named provider is canonicalized so that <c>npgsql</c> and <c>Npgsql</c> mean the same thing —
    /// the driver descriptions are looked up by an ordinal comparison, and a case that does not match is
    /// otherwise a failure at the first connection. A name Quartz ships no description for passes
    /// through as written, because that is a description the application registered.
    /// </remarks>
    private static string ResolveProvider(QuartzAspireSettings settings, string connectionName)
    {
        if (!string.IsNullOrWhiteSpace(settings.Provider))
        {
            return Canonical(settings.Provider);
        }

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new SchedulerConfigException(
                $"No connection string named '{connectionName}' was found, and no provider was named, so "
                + "Quartz has nothing to work out which database it is for from. Under Aspire the string "
                + $"arrives from the AppHost's WithReference, as ConnectionStrings:{connectionName}; "
                + "without one, set QuartzAspireSettings.Provider to a name on DataSourceOptions.Providers "
                + "and configure the connection yourself.");
        }

        return ConnectionStringProviderInference.Infer(settings.ConnectionString, connectionName);
    }

    /// <summary>
    /// The names Quartz ships a driver description for, indexed case-insensitively.
    /// </summary>
    private static readonly Dictionary<string, string> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        [DataSourceOptions.Providers.SqlServer] = DataSourceOptions.Providers.SqlServer,
        [DataSourceOptions.Providers.Npgsql] = DataSourceOptions.Providers.Npgsql,
        [DataSourceOptions.Providers.MySql] = DataSourceOptions.Providers.MySql,
        [DataSourceOptions.Providers.MySqlConnector] = DataSourceOptions.Providers.MySqlConnector,
        [DataSourceOptions.Providers.Oracle] = DataSourceOptions.Providers.Oracle,
        [DataSourceOptions.Providers.Sqlite] = DataSourceOptions.Providers.Sqlite,
        [DataSourceOptions.Providers.SystemDataSqlite] = DataSourceOptions.Providers.SystemDataSqlite,
        [DataSourceOptions.Providers.Firebird] = DataSourceOptions.Providers.Firebird,
    };

    private static string Canonical(string provider) => KnownProviders.GetValueOrDefault(provider, provider);

    /// <summary>
    /// Chooses the driver delegate that speaks this database's SQL.
    /// </summary>
    /// <remarks>
    /// A provider name Quartz ships no description for reaches <c>UseGenericDatabase</c>, which selects
    /// the generic dialect and leaves the description to whatever <c>DbMetadataFactory</c> the
    /// application registered — so naming an unknown provider is a configuration this supports rather
    /// than an error it reports.
    /// </remarks>
    [RequiresUnreferencedCode(NamesTheDriversTypes)]
    private static void UseDialect(IPersistentStoreBuilder store, string provider, Action<DataSourceOptions> connection)
    {
        switch (provider)
        {
            case DataSourceOptions.Providers.SqlServer:
                store.UseSqlServer(connection);
                break;
            case DataSourceOptions.Providers.Npgsql:
                store.UsePostgres(connection);
                break;
            case DataSourceOptions.Providers.MySql:
                store.UseMySql(connection);
                break;
            case DataSourceOptions.Providers.MySqlConnector:
                store.UseMySqlConnector(connection);
                break;
            case DataSourceOptions.Providers.Oracle:
                store.UseOracle(connection);
                break;
            case DataSourceOptions.Providers.Sqlite:
                store.UseSqlite(connection);
                break;
            case DataSourceOptions.Providers.SystemDataSqlite:
                store.UseSystemDataSqlite(connection);
                break;
            case DataSourceOptions.Providers.Firebird:
                store.UseFirebird(connection);
                break;
            default:
                store.UseGenericDatabase(provider, connection);
                break;
        }
    }

    /// <summary>
    /// Where the store's connections come from: a keyed <see cref="DbDataSource"/>, the container's one
    /// unkeyed data source, or the connection string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the how-to page's ladder as code. A data source is preferred because whatever it was built
    /// with is then in play for Quartz's own statements — its type mappers, its logging, its connection
    /// multiplexing — since commands are made by the connection rather than from a driver description.
    /// </para>
    /// <para>
    /// SQL Server never takes it. <c>Microsoft.Data.SqlClient</c> ships no <see cref="DbDataSource"/>
    /// implementation and Aspire's SQL Server client integration registers a scoped <c>SqlConnection</c>
    /// instead, so <c>UseRegisteredDataSource</c> would resolve some other database's data source or
    /// nothing at all — and would fail at first use rather than at startup.
    /// </para>
    /// <para>
    /// The probe is against the service collection as it stands at the call site, which is what makes
    /// this deterministic: the alternative — probing inside the per-scheduler callback — would answer
    /// differently depending on whether <c>AddQuartz</c> had already been called, since that is what
    /// decides when the callback runs.
    /// </para>
    /// </remarks>
    private static Action<DataSourceOptions> SelectConnection(
        IServiceCollection services,
        string provider,
        string connectionName,
        QuartzAspireSettings settings)
    {
        string? connectionString = string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? null
            : settings.ConnectionString;

        if (!string.Equals(provider, DataSourceOptions.Providers.SqlServer, StringComparison.Ordinal))
        {
            if (HasDataSource(services, connectionName))
            {
                return options => options.DataSourceServiceKey = connectionName;
            }

            if (HasDataSource(services, serviceKey: null))
            {
                return options => options.UseRegisteredDataSource = true;
            }
        }

        // Both, and in that order: ConnectionString wins over ConnectionStringName when it is set, so
        // naming the connection as well costs nothing and covers the case where the string was not in
        // configuration at the moment this ran.
        return options =>
        {
            options.ConnectionString = connectionString;
            options.ConnectionStringName = connectionName;
        };
    }

    private static bool HasDataSource(IServiceCollection services, object? serviceKey)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType != typeof(DbDataSource))
            {
                continue;
            }

            if (serviceKey is null ? !descriptor.IsKeyedService : descriptor.IsKeyedService && Equals(descriptor.ServiceKey, serviceKey))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Names Quartz's two signals to the application's OpenTelemetry pipeline.
    /// </summary>
    /// <remarks>
    /// No exporter. A ServiceDefaults project calls <c>UseOtlpExporter()</c> whenever
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, and the AppHost sets it; that method may be called only
    /// once and cannot be combined with a signal-specific <c>AddOtlpExporter()</c>, so adding one here
    /// would turn a working application into a <see cref="NotSupportedException"/> at startup.
    /// <c>AddOpenTelemetry()</c> itself composes — there is one tracer provider and one meter provider
    /// per container — so this runs before or after <c>AddServiceDefaults()</c> equally well.
    /// </remarks>
    private static void AddTelemetry(IServiceCollection services, QuartzAspireSettings settings)
    {
        if (settings.DisableTracing && settings.DisableMetrics)
        {
            return;
        }

        IOpenTelemetryBuilder telemetry = services.AddOpenTelemetry();

        if (!settings.DisableTracing)
        {
            telemetry.WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName));
        }

        if (!settings.DisableMetrics)
        {
            telemetry.WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
        }
    }
}
