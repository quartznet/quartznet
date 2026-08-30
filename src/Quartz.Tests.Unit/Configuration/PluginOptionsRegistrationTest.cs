#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards that a plugin is handed the options that were configured for <em>its</em> scheduler.
/// </summary>
/// <remarks>
/// <c>AddPlugin&lt;T, TOptions&gt;</c> configures named options, keyed by the scheduler's name, while the
/// plugin is built by <c>ActivatorUtilities</c> and so asks for <see cref="IOptions{TOptions}" /> — which
/// always resolves the unnamed instance. A plugin on a named scheduler therefore used to see defaults,
/// with nothing thrown and nothing logged. These are the cases that were silent.
/// </remarks>
public class PluginOptionsRegistrationTest
{
    [Test]
    public void APluginOnANamedSchedulerSeesTheOptionsConfiguredForIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(
            options => options.Setting = "configured"));

        using var provider = services.BuildServiceProvider();

        Plugin(provider, "reporting").Setting.Should().Be("configured",
            "AddPlugin configures the scheduler's named options, so the plugin has to be given those");
    }

    [Test]
    public void APluginOnTheDefaultSchedulerSeesTheOptionsConfiguredForIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(
            options => options.Setting = "configured"));

        using var provider = services.BuildServiceProvider();

        Plugin(provider, key: null).Setting.Should().Be("configured");
    }

    [Test]
    public void NamedSchedulersDoNotShareTheirPluginOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(
            options => options.Setting = "reporting"));
        services.AddQuartz("ingest", q => q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(
            options => options.Setting = "ingest"));

        using var provider = services.BuildServiceProvider();

        Plugin(provider, "reporting").Setting.Should().Be("reporting");
        Plugin(provider, "ingest").Setting.Should().Be("ingest",
            "two schedulers sharing an options type must still each get their own configuration");
    }

    [Test]
    public void ADefaultSchedulerKeepsItsOwnOptionsWhenANamedSchedulerSharesTheType()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(
            options => options.Setting = "default"));
        services.AddQuartz("reporting", q => q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(
            options => options.Setting = "reporting"));

        using var provider = services.BuildServiceProvider();

        Plugin(provider, key: null).Setting.Should().Be("default");
        Plugin(provider, "reporting").Setting.Should().Be("reporting");
    }

    [Test]
    public void APluginWithoutOptionsConfigurationStillGetsItsSchedulersOptions()
    {
        var services = new ServiceCollection();
        services.Configure<RecordingPluginOptions>("reporting", options => options.Setting = "from-configuration");
        services.AddQuartz("reporting", q => q.AddPlugin<RecordingPlugin, RecordingPluginOptions>());

        using var provider = services.BuildServiceProvider();

        Plugin(provider, "reporting").Setting.Should().Be("from-configuration",
            "the options name is the scheduler name whether or not AddPlugin was handed a callback");
    }

    [Test]
    public async Task ANamedSchedulerStartsThePluginWithTheOptionsConfiguredForIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.UseInMemoryStore();
            q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(options => options.Setting = "configured", "recorder");
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();
        try
        {
            var plugin = Plugin(provider, "reporting");

            plugin.Setting.Should().Be("configured");
            plugin.PluginName.Should().Be("recorder",
                "the plugin the scheduler initialized has to be the one the options were configured for");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A plugin that declares <c>Initialize</c> and nothing else is started and shut down like any
    /// other: the scheduler calls both through the interface, and the interface answers. Before
    /// <see cref="ISchedulerPlugin.Start" /> and <see cref="ISchedulerPlugin.Shutdown" /> had defaults,
    /// such a plugin did not compile.
    /// </summary>
    [Test]
    public async Task APluginThatOnlyImplementsInitializeRunsTheWholeLifecycle()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();
            q.AddPlugin<RecordingPlugin, RecordingPluginOptions>(options => options.Setting = "configured", "recorder");
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.Start();
        await scheduler.Shutdown(waitForJobsToComplete: false);

        Plugin(provider, key: null).PluginName.Should().Be("recorder",
            "the plugin went through the whole lifecycle, and the two members it does not declare are the interface's");
    }

    private static RecordingPlugin Plugin(IServiceProvider provider, string? key)
    {
        IEnumerable<ISchedulerPlugin> plugins = key is null
            ? provider.GetServices<ISchedulerPlugin>()
            : provider.GetKeyedServices<ISchedulerPlugin>(key);

        return plugins.OfType<RecordingPlugin>().Single();
    }

    public sealed class RecordingPluginOptions
    {
        public string Setting { get; set; } = "default-setting";
    }

    /// <summary>
    /// Declares <see cref="ISchedulerPlugin.Initialize" /> and nothing else, which is what most plugins
    /// have to say. The scheduler still calls <c>Start</c> and <c>Shutdown</c> on it through the
    /// interface, so this compiling and the scheduler above running is the assertion that the interface's
    /// defaults are reached.
    /// </summary>
    public sealed class RecordingPlugin : ISchedulerPlugin
    {
        public RecordingPlugin(IOptions<RecordingPluginOptions> options)
        {
            Setting = options.Value.Setting;
        }

        public string Setting { get; }

        public string? PluginName { get; private set; }

        public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            PluginName = pluginName;
            return default;
        }
    }
}
