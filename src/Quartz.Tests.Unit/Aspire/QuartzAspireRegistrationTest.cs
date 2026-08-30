#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// What <c>AddQuartzPersistentStore</c> puts in the container, and what each <c>Disable*</c> takes back
/// out again.
/// </summary>
public class QuartzAspireRegistrationTest
{
    /// <summary>
    /// The order of <c>AddQuartzPersistentStore</c> and <c>AddQuartz</c> does not matter.
    /// </summary>
    /// <remarks>
    /// A package that adds something to every scheduler cannot know whether the application registers
    /// its schedulers before or after calling it, which is what <c>ConfigureAllQuartzSchedulers</c> is
    /// for. Written as a comparison of two whole containers rather than of one value, because the point
    /// is that nothing differs.
    /// </remarks>
    [Test]
    public void CallingItBeforeOrAfterAddQuartzGivesTheSameContainer()
    {
        (DataSourceOptions DataSource, AdoJobStoreOptions Store) before = Build(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.TablePrefix = "APP_");
            builder.AddQuartz();
        });

        (DataSourceOptions DataSource, AdoJobStoreOptions Store) after = Build(builder =>
        {
            builder.AddQuartz();
            builder.AddQuartzPersistentStore("quartz", settings => settings.TablePrefix = "APP_");
        });

        after.DataSource.Should().BeEquivalentTo(before.DataSource);
        after.Store.Should().BeEquivalentTo(before.Store);

        before.DataSource.Provider.Should().Be(DataSourceOptions.Providers.Npgsql,
            "a comparison of two empty results would pass just as well, so one of them is checked for "
            + "having happened at all");
    }

    [Test]
    public void TheStoreIsThePersistentOneAndKnowsItsDataSource()
    {
        (DataSourceOptions dataSource, AdoJobStoreOptions store) = Build(Standard);

        store.DataSource.Should().Be("quartz",
            "the store's data source is named after the scheduler that owns it, and 'quartz' is the "
            + "default scheduler's");
        dataSource.Provider.Should().Be(DataSourceOptions.Providers.Npgsql);
    }

    [Test]
    public void TheTablePrefixIsLeftAloneWhenNothingSetsIt()
    {
        (_, AdoJobStoreOptions store) = Build(Standard);

        store.TablePrefix.Should().Be("QRTZ_",
            "an unset setting must not overwrite what Quartz:JobStore:TablePrefix or the default already "
            + "said");
    }

    [Test]
    public void TheTablePrefixIsAppliedWhenItIsSet()
    {
        (_, AdoJobStoreOptions store) = Build(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.TablePrefix = "APP_");
            builder.AddQuartz();
        });

        store.TablePrefix.Should().Be("APP_");
    }

    [Test]
    public void ClusteringIsOffUnlessAskedFor()
    {
        using IHost host = Host(Standard);

        Clustering(host).Enabled.Should().BeFalse();
        AspireApplication.StoreOf(host.Services).UseDbLocks.Should().BeFalse();
    }

    [Test]
    public void ClusteredTurnsOnClusteringAndTheDatabaseLocksItNeeds()
    {
        using IHost host = Host(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.Clustered = true);
            builder.AddQuartz();
        });

        Clustering(host).Enabled.Should().BeTrue();
        AspireApplication.StoreOf(host.Services).UseDbLocks.Should().BeTrue(
            "clustering has never worked without database locking, and UseClustering() enables both");
    }

    /// <summary>
    /// The other half of what a cluster needs, which an Aspire replica set cannot supply for itself.
    /// </summary>
    /// <remarks>
    /// A node recognises its own check-in row and its own fired triggers by <c>InstanceId</c>, and every
    /// scheduler starts life carrying <c>NON_CLUSTERED</c>. <c>WithReplicas(2)</c> is one call in an
    /// AppHost and gives the copies no identity of their own, so a cluster whose nodes all answer to one
    /// id is not a hypothetical — it is what asking for clustering and nothing else would have produced.
    /// </remarks>
    [Test]
    public void ClusteredGeneratesTheInstanceId()
    {
        using IHost host = Host(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.Clustered = true);
            builder.AddQuartz();
        });

        Scheduler(host).GenerateInstanceId.Should().BeTrue(
            "an Aspire replica has no identity of its own to borrow, and every node keeping "
            + "NON_CLUSTERED is the worst failure clustering has");
    }

    [Test]
    public void AnUnclusteredSchedulerIsLeftAsItWas()
    {
        using IHost host = Host(Standard);

        Scheduler(host).GenerateInstanceId.Should().BeFalse(
            "a single-node scheduler needs no derived id, and deriving one would change what SCHED_NAME's "
            + "companion column says for no reason");
    }

    [Test]
    public void ClusteredKeepsAnExplicitInstanceId()
    {
        using IHost host = Host(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.Clustered = true);
            builder.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceId = "orders-0"));
        });

        QuartzSchedulerOptions scheduler = Scheduler(host);

        scheduler.InstanceId.Should().Be("orders-0");
        scheduler.GenerateInstanceId.Should().BeFalse(
            "an application that named its nodes has said what they are called - an ordinal deployment, or "
            + "a hostname it trusts - and deriving one instead would ignore it");
    }

    [Test]
    public void ClusteredKeepsAnInstanceIdThatCameFromConfiguration()
    {
        HostApplicationBuilder builder = AspireApplication.Worker(
            ("ConnectionStrings:quartz", AspireApplication.Postgres),
            ("Quartz:Scheduler:InstanceId", "orders-1"));

        builder.AddQuartzPersistentStore("quartz", settings => settings.Clustered = true);
        builder.AddQuartz();

        using IHost host = builder.Build();

        Scheduler(host).InstanceId.Should().Be("orders-1",
            "this runs from ConfigureAllQuartzSchedulers, which AddQuartz applies after the configuration "
            + "binding as well as after the scheduler's own callback, so what it reads is everything the "
            + "application said");
        Scheduler(host).GenerateInstanceId.Should().BeFalse();
    }

    [Test]
    public void SchedulerNameLimitsTheStoreToOneScheduler()
    {
        using IHost host = Host(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.SchedulerName = "orders");
            builder.AddQuartz("orders");
            builder.AddQuartz("reporting");
        });

        AspireApplication.DataSourceOf(host.Services, "orders").Provider.Should().Be(DataSourceOptions.Providers.Npgsql);
        AspireApplication.StoreOf(host.Services, "orders").DataSource.Should().Be("orders");

        host.Services.GetKeyedService<IDbProvider>("reporting").Should().BeNull(
            "a settings object naming a scheduler is talking about that one, and the other scheduler keeps "
            + "whatever store it chose for itself - which is the in-memory one, with no database to reach");
        host.Services.GetRequiredKeyedService<IDbProvider>("orders").Should().NotBeNull();
    }

    [Test]
    public void NamingNoSchedulerReachesEveryOneOfThem()
    {
        using IHost host = Host(builder =>
        {
            builder.AddQuartzPersistentStore("quartz");
            builder.AddQuartz("orders");
            builder.AddQuartz("reporting");
        });

        AspireApplication.StoreOf(host.Services, "orders").DataSource.Should().Be("orders");
        AspireApplication.StoreOf(host.Services, "reporting").DataSource.Should().Be("reporting",
            "one database and several schedulers is a cluster, which is a configuration rather than a "
            + "mistake");
    }

    [Test]
    public void TheHealthCheckIsRegisteredForTheSchedulerInQuestion()
    {
        using IHost host = Host(Standard);

        CheckNames(host).Should().Contain("quartz-scheduler");
    }

    [Test]
    public void DisableHealthChecksLeavesTheCheckUnregistered()
    {
        using IHost host = Host(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.DisableHealthChecks = true);
            builder.AddQuartz();
        });

        CheckNames(host).Should().NotContain("quartz-scheduler",
            "an application that reports its scheduler's health some other way should not have a second "
            + "answer appear on /health");
    }

    [Test]
    public void EachSchedulerGetsItsOwnCheck()
    {
        using IHost host = Host(builder =>
        {
            builder.AddQuartzPersistentStore("quartz");
            builder.AddQuartz("orders");
            builder.AddQuartz("reporting");
        });

        CheckNames(host).Should().Contain(["quartz-scheduler-orders", "quartz-scheduler-reporting"],
            "a check reports one scheduler's state, so two schedulers need two of them under two names");
    }

    [Test]
    public void BothTelemetrySignalsAreNamedToThePipeline()
    {
        IServiceCollection services = Services(Standard);

        Providers(services, "TracerProvider").Should().Be(1);
        Providers(services, "MeterProvider").Should().Be(1);

        Deferred(services, "IConfigureTracerProviderBuilder").Should().BePositive(
            "AddSource records its name against the collection, to be replayed when the provider is built");
        Deferred(services, "IConfigureMeterProviderBuilder").Should().BePositive();
    }

    [Test]
    public void DisableTracingRemovesTheTracerSubscriptionAndNothingElse()
    {
        IServiceCollection enabled = Services(Standard);
        IServiceCollection disabled = Services(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.DisableTracing = true);
            builder.AddQuartz();
        });

        Providers(disabled, "TracerProvider").Should().Be(0,
            "the one registration this call makes for tracing is the tracer provider itself");
        Deferred(disabled, "IConfigureTracerProviderBuilder").Should().Be(0,
            "and with it goes the deferred AddSource it existed to carry");

        Providers(disabled, "MeterProvider").Should().Be(Providers(enabled, "MeterProvider"));
        Deferred(disabled, "IConfigureMeterProviderBuilder").Should().Be(Deferred(enabled, "IConfigureMeterProviderBuilder"),
            "the two signals are subscribed to independently, so turning one off leaves the other exactly "
            + "as it was");
    }

    [Test]
    public void DisableMetricsRemovesTheMeterSubscriptionAndNothingElse()
    {
        IServiceCollection enabled = Services(Standard);
        IServiceCollection disabled = Services(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.DisableMetrics = true);
            builder.AddQuartz();
        });

        Providers(disabled, "MeterProvider").Should().Be(0);
        Deferred(disabled, "IConfigureMeterProviderBuilder").Should().Be(0);

        Providers(disabled, "TracerProvider").Should().Be(Providers(enabled, "TracerProvider"));
        Deferred(disabled, "IConfigureTracerProviderBuilder").Should().Be(Deferred(enabled, "IConfigureTracerProviderBuilder"));
    }

    [Test]
    public void DisablingBothSignalsLeavesNoOpenTelemetryRegistrationAtAll()
    {
        IServiceCollection services = Services(builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings =>
            {
                settings.DisableTracing = true;
                settings.DisableMetrics = true;
            });
            builder.AddQuartz();
        });

        services.Select(descriptor => descriptor.ServiceType.Namespace ?? "")
            .Where(name => name.StartsWith("OpenTelemetry", StringComparison.Ordinal))
            .Should().BeEmpty(
                "an application that wants neither signal should not pay for the pipeline that carries them");
    }

    /// <summary>
    /// Nothing here exports. A ServiceDefaults project calls <c>UseOtlpExporter()</c> whenever the AppHost
    /// set <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, that method may be called only once, and it cannot be
    /// combined with a signal-specific <c>AddOtlpExporter()</c> — so an exporter added here would turn a
    /// working application into a <c>NotSupportedException</c> at startup.
    /// </summary>
    [Test]
    public void NoExporterIsAdded()
    {
        IServiceCollection services = Services(Standard);

        services.Select(descriptor => descriptor.ServiceType.FullName ?? "")
            .Where(name => name.Contains("Exporter", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    /// <summary>
    /// The registration an application under Aspire writes, and the one most of these start from.
    /// </summary>
    private static void Standard(HostApplicationBuilder builder)
    {
        builder.AddQuartzPersistentStore("quartz");
        builder.AddQuartz();
    }

    private static (DataSourceOptions DataSource, AdoJobStoreOptions Store) Build(Action<HostApplicationBuilder> configure)
    {
        using IHost host = Host(configure);

        return (AspireApplication.DataSourceOf(host.Services), AspireApplication.StoreOf(host.Services));
    }

    private static IHost Host(Action<HostApplicationBuilder> configure)
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(AspireApplication.Postgres);
        configure(builder);
        return builder.Build();
    }

    private static IServiceCollection Services(Action<HostApplicationBuilder> configure)
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(AspireApplication.Postgres);
        configure(builder);
        return builder.Services;
    }

    private static QuartzSchedulerOptions Scheduler(IHost host, string? schedulerName = null)
    {
        return host.Services.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>()
            .Get(schedulerName ?? Options.DefaultName);
    }

    private static ClusteringOptions Clustering(IHost host, string? schedulerName = null)
    {
        return host.Services.GetRequiredService<IOptionsMonitor<ClusteringOptions>>()
            .Get(schedulerName ?? Options.DefaultName);
    }

    private static IEnumerable<string> CheckNames(IHost host)
    {
        return host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name);
    }

    /// <summary>
    /// How many deferred OpenTelemetry configurations of one kind the container holds.
    /// </summary>
    /// <remarks>
    /// <c>AddSource</c> and <c>AddMeter</c> on the builder <c>AddOpenTelemetry()</c> hands out are
    /// deferred: each records one <c>IConfigureTracerProviderBuilder</c> or
    /// <c>IConfigureMeterProviderBuilder</c> against the collection, to be replayed when the provider is
    /// built. Counting them is what makes "this call subscribes to exactly one thing, and turning it off
    /// removes exactly that" checkable without building a provider and reading process-global
    /// <c>ActivitySource</c> state, which fixtures running in parallel would share.
    /// </remarks>
    private static int Deferred(IServiceCollection services, string interfaceName) => ByName(services, interfaceName);

    /// <summary>
    /// How many of one OpenTelemetry provider the container holds. This is the "exactly one registration"
    /// a signal costs — the provider itself, added by <c>WithTracing</c> or <c>WithMetrics</c>.
    /// </summary>
    private static int Providers(IServiceCollection services, string typeName) => ByName(services, typeName);

    /// <summary>
    /// Counts service registrations by their service type's name.
    /// </summary>
    /// <remarks>
    /// By name because <c>IConfigureTracerProviderBuilder</c> and its meter twin are internal to the
    /// OpenTelemetry SDK. A rename does not pass silently: the positive tests assert a count above zero,
    /// so a name that stops matching fails them rather than turning every count into zero unnoticed.
    /// </remarks>
    private static int ByName(IServiceCollection services, string typeName)
    {
        return services.Count(descriptor => string.Equals(descriptor.ServiceType.Name, typeName, StringComparison.Ordinal));
    }
}
