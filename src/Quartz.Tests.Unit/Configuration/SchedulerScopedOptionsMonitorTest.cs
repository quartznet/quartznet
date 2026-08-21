#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards that a scheduler-scoped component asking for a <em>reloadable</em> view of its options —
/// <see cref="IOptionsMonitor{TOptions}"/> or <see cref="IOptionsSnapshot{TOptions}"/> — is answered with
/// its own scheduler's configuration.
/// </summary>
/// <remarks>
/// A scheduler's options are named options, keyed by the scheduler's name, and the unnamed members of
/// both interfaces resolve the unnamed instance. A component on a named scheduler therefore used to read
/// the default scheduler's configuration, silently. <c>IOptions&lt;T&gt;</c> is covered by
/// <see cref="PluginOptionsRegistrationTest"/>; these are the two interfaces it left behind.
/// </remarks>
public class SchedulerScopedOptionsMonitorTest
{
    [Test]
    public void AMonitorInjectedIntoANamedSchedulersComponentReportsThatSchedulersOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<WatchingPlugin>();
        });

        using var provider = services.BuildServiceProvider();

        Plugin<WatchingPlugin>(provider, "reporting").Monitor.CurrentValue.MaxBatchSize.Should().Be(7,
            "CurrentValue is the unnamed member, so for a scheduler's component it has to mean that scheduler");
    }

    [Test]
    public void AMonitorInjectedIntoTheDefaultSchedulersComponentReportsItsOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<WatchingPlugin>();
        });

        using var provider = services.BuildServiceProvider();

        Plugin<WatchingPlugin>(provider, key: null).Monitor.CurrentValue.MaxBatchSize.Should().Be(7);
    }

    [Test]
    public void NamedSchedulersDoNotShareWhatTheirMonitorsReport()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.MaxBatchSize = 1));
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<WatchingPlugin>();
        });
        services.AddQuartz("ingest", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 9);
            q.AddPlugin<WatchingPlugin>();
        });

        using var provider = services.BuildServiceProvider();

        Plugin<WatchingPlugin>(provider, "reporting").Monitor.CurrentValue.MaxBatchSize.Should().Be(7);
        Plugin<WatchingPlugin>(provider, "ingest").Monitor.CurrentValue.MaxBatchSize.Should().Be(9,
            "two schedulers watching the same options type must still each watch their own");
    }

    [Test]
    public void AMonitorAnsweringAnExplicitNameGivesThatNamesOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.MaxBatchSize = 1));
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<WatchingPlugin>();
        });
        services.AddQuartz("ingest", q => q.ConfigureScheduler(options => options.MaxBatchSize = 9));

        using var provider = services.BuildServiceProvider();
        var monitor = Plugin<WatchingPlugin>(provider, "reporting").Monitor;

        monitor.Get("ingest").MaxBatchSize.Should().Be(9,
            "Get names the instance it wants, so it must not be rewritten to the scheduler's own name");
        monitor.Get(Options.DefaultName).MaxBatchSize.Should().Be(1);
        monitor.Get(null).MaxBatchSize.Should().Be(1, "a null name is the default one, as it is everywhere else");
    }

    [Test]
    public void ASnapshotResolvedInANamedSchedulersScopeReportsThatSchedulersOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.MaxBatchSize = 1));
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<CapturingPlugin>(p => new CapturingPlugin(p));
        });

        using var provider = services.BuildServiceProvider();

        // The provider a job is built from: the scheduler's own, scoped the way the job factory scopes it.
        using IServiceScope scope = Scheduler(provider, "reporting").GetRequiredService<IServiceScopeFactory>().CreateScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<QuartzSchedulerOptions>>();

        snapshot.Value.MaxBatchSize.Should().Be(7,
            "IOptionsSnapshot derives from IOptions, so Value has to mean the same scheduler IOptions means");
        snapshot.Get("reporting").MaxBatchSize.Should().Be(7);
        snapshot.Get(Options.DefaultName).MaxBatchSize.Should().Be(1, "an explicit name is honoured as asked");
    }

    [Test]
    public void ASnapshotResolvedInTheDefaultSchedulersScopeReportsItsOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<CapturingPlugin>(p => new CapturingPlugin(p));
        });

        using var provider = services.BuildServiceProvider();

        using IServiceScope scope = Scheduler(provider, key: null).GetRequiredService<IServiceScopeFactory>().CreateScope();

        scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<QuartzSchedulerOptions>>()
            .Value.MaxBatchSize.Should().Be(7);
    }

    [Test]
    public void AMonitorOverAPluginsOwnOptionsReportsThatSchedulersOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.AddPlugin<WatchingPlugin, WatchedOptions>(options => options.Setting = "default"));
        services.AddQuartz("reporting", q => q.AddPlugin<WatchingPlugin, WatchedOptions>(
            options => options.Setting = "reporting"));

        using var provider = services.BuildServiceProvider();

        Plugin<WatchingPlugin>(provider, "reporting").Plugin.CurrentValue.Setting.Should().Be("reporting",
            "AddPlugin declares the options type as a scheduler's own, and that has to hold for a monitor too");
        Plugin<WatchingPlugin>(provider, key: null).Plugin.CurrentValue.Setting.Should().Be("default");
    }

    [Test]
    public void OnChangeFiresWhenThisSchedulersOptionsChange()
    {
        var changes = new ManualChangeTokenSource<QuartzSchedulerOptions>("reporting");
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsChangeTokenSource<QuartzSchedulerOptions>>(changes);
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<WatchingPlugin>();
        });

        using var provider = services.BuildServiceProvider();
        var monitor = Plugin<WatchingPlugin>(provider, "reporting").Monitor;

        List<(QuartzSchedulerOptions Options, string? Name)> seen = [];
        using IDisposable? registration = monitor.OnChange((options, name) => seen.Add((options, name)));

        changes.Trigger();

        seen.Should().ContainSingle("the scheduler's own options changed, so its listener has to hear about it");
        seen[0].Options.MaxBatchSize.Should().Be(7);
        seen[0].Name.Should().Be("reporting");
    }

    [Test]
    public void OnChangeStaysSilentWhenAnotherSchedulersOptionsChange()
    {
        var changes = new ManualChangeTokenSource<QuartzSchedulerOptions>("ingest");
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsChangeTokenSource<QuartzSchedulerOptions>>(changes);
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<WatchingPlugin>();
        });
        services.AddQuartz("ingest", q => q.ConfigureScheduler(options => options.MaxBatchSize = 9));

        using var provider = services.BuildServiceProvider();
        var monitor = Plugin<WatchingPlugin>(provider, "reporting").Monitor;

        List<QuartzSchedulerOptions> seen = [];
        using IDisposable? registration = monitor.OnChange(options => seen.Add(options));

        changes.Trigger();

        seen.Should().BeEmpty(
            "the single-argument listener is handed the changed value as if it were its own, so another " +
            "scheduler's change must not reach it at all");
    }

    [Test]
    public void OnChangeStopsFiringOnceTheRegistrationIsDisposed()
    {
        var changes = new ManualChangeTokenSource<QuartzSchedulerOptions>("reporting");
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsChangeTokenSource<QuartzSchedulerOptions>>(changes);
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureScheduler(options => options.MaxBatchSize = 7);
            q.AddPlugin<WatchingPlugin>();
        });

        using var provider = services.BuildServiceProvider();
        var monitor = Plugin<WatchingPlugin>(provider, "reporting").Monitor;

        int fired = 0;
        IDisposable? registration = monitor.OnChange((_, _) => fired++);
        registration.Should().NotBeNull("a wrapper that swallowed the registration could never be unhooked");

        changes.Trigger();
        registration!.Dispose();
        changes.Trigger();

        fired.Should().Be(1, "disposing the registration has to unhook the listener from the underlying monitor");
    }

    private static T Plugin<T>(IServiceProvider provider, string? key) where T : ISchedulerPlugin
    {
        IEnumerable<ISchedulerPlugin> plugins = key is null
            ? provider.GetServices<ISchedulerPlugin>()
            : provider.GetKeyedServices<ISchedulerPlugin>(key);

        return plugins.OfType<T>().Single();
    }

    /// <summary>
    /// The provider a scheduler's own components are built from.
    /// </summary>
    private static IServiceProvider Scheduler(IServiceProvider provider, string? key)
    {
        return Plugin<CapturingPlugin>(provider, key).Provider;
    }

    public sealed class WatchedOptions
    {
        public string Setting { get; set; } = "default-setting";
    }

    public sealed class WatchingPlugin : ISchedulerPlugin
    {
        public WatchingPlugin(IOptionsMonitor<QuartzSchedulerOptions> monitor, IOptionsMonitor<WatchedOptions> plugin)
        {
            Monitor = monitor;
            Plugin = plugin;
        }

        public IOptionsMonitor<QuartzSchedulerOptions> Monitor { get; }

        public IOptionsMonitor<WatchedOptions> Plugin { get; }

        public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default) => default;

        public ValueTask Start(CancellationToken cancellationToken = default) => default;

        public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Hands the test the provider a scheduler's components are constructed from.
    /// </summary>
    public sealed class CapturingPlugin : ISchedulerPlugin
    {
        public CapturingPlugin(IServiceProvider provider) => Provider = provider;

        public IServiceProvider Provider { get; }

        public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default) => default;

        public ValueTask Start(CancellationToken cancellationToken = default) => default;

        public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Reports a change to one named options instance on demand, which is what a reloading configuration
    /// source amounts to.
    /// </summary>
    private sealed class ManualChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>
    {
        private CancellationTokenSource source = new();

        public ManualChangeTokenSource(string name) => Name = name;

        public string Name { get; }

        public IChangeToken GetChangeToken() => new CancellationChangeToken(source.Token);

        public void Trigger()
        {
            CancellationTokenSource previous = Interlocked.Exchange(ref source, new CancellationTokenSource());
            previous.Cancel();
            previous.Dispose();
        }
    }
}
