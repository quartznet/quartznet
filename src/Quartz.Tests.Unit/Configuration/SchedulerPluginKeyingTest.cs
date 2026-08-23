#nullable enable

using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A plugin named by a <c>quartz.plugin.&lt;name&gt;.type</c> key belongs to the scheduler whose
/// properties named it, whether it is built on the spot or resolved from the container.
/// </summary>
/// <remarks>
/// Two halves, and only one of them was ever broken. Building a plugin from a type name has always
/// produced one instance per scheduler, configured from that scheduler's own keys; the probe that runs
/// first — "is this type registered as a service?" — was unkeyed, so a type that happened to be
/// registered collapsed every scheduler onto one instance. Both halves are pinned here, because
/// fixing the first must not cost the second.
/// </remarks>
public sealed class SchedulerPluginKeyingTest
{
    [Test]
    public async Task ANamedSchedulerGetsItsOwnPluginRatherThanTheDefaultSchedulersRegistration()
    {
        PluginLog log = new();

        ServiceCollection services = new();
        services.AddSingleton(log);

        // Registered as a service, which is the only case in which the probe finds anything at all —
        // and, before this, the case in which every scheduler naming the type got this one instance.
        services.AddSingleton<RecordingPlugin>();

        services.AddQuartz(new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "shared",
            ["quartz.plugin.recording.type"] = typeof(RecordingPlugin).AssemblyQualifiedName,
            ["quartz.plugin.recording.tenant"] = "shared",
        });

        services.AddQuartz("acme", new NameValueCollection
        {
            ["quartz.plugin.recording.type"] = typeof(RecordingPlugin).AssemblyQualifiedName,
            ["quartz.plugin.recording.tenant"] = "acme",
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        await CreateSchedulers(provider, "acme");

        IReadOnlyList<(RecordingPlugin Plugin, string Scheduler)> initializations = log.Initializations;

        initializations.Should().HaveCount(2, "each scheduler runs the plugin its own properties named");

        initializations.Select(x => x.Scheduler).Should().BeEquivalentTo(["shared", "acme"]);
        initializations.Select(x => x.Plugin.Tenant).Should().BeEquivalentTo(["shared", "acme"],
            "each instance is configured from the property bag of the scheduler that named it");

        initializations[0].Plugin.Should().NotBeSameAs(initializations[1].Plugin,
            "a plugin is told which scheduler it extends when it is initialized, so one instance shared "
            + "between two schedulers has the second initialization overwrite the first");

        provider.GetRequiredService<RecordingPlugin>().Should().BeSameAs(
            initializations.Single(x => x.Scheduler == "shared").Plugin,
            "the unkeyed registration is the default scheduler's own, and stays that");
    }

    [Test]
    public async Task APropertyNamedPluginUsesThisSchedulersOwnKeyedRegistration()
    {
        PluginLog log = new();

        ServiceCollection services = new();
        services.AddSingleton(log);

        // A plugin instance a named scheduler was given on purpose. Keyed, because that is how
        // everything else belonging to one scheduler is registered.
        RecordingPlugin acmePlugin = new(log);
        services.AddKeyedSingleton(typeof(RecordingPlugin), "acme", acmePlugin);

        services.AddQuartz("acme", new NameValueCollection
        {
            ["quartz.plugin.recording.type"] = typeof(RecordingPlugin).AssemblyQualifiedName,
            ["quartz.plugin.recording.tenant"] = "acme",
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        await CreateSchedulers(provider, "acme");

        log.Initializations.Should().ContainSingle().Which.Plugin.Should().BeSameAs(acmePlugin,
            "a registration made under this scheduler's key is this scheduler's plugin");

        acmePlugin.Tenant.Should().Be("acme",
            "the leftover quartz.plugin.<name>.* keys configure the instance however it was obtained");
    }

    [Test]
    public async Task APropertyNamedPluginThatIsNotRegisteredIsStillBuiltPerScheduler()
    {
        PluginLog log = new();

        ServiceCollection services = new();
        services.AddSingleton(log);

        services.AddQuartz("acme", new NameValueCollection
        {
            ["quartz.plugin.recording.type"] = typeof(RecordingPlugin).AssemblyQualifiedName,
            ["quartz.plugin.recording.tenant"] = "acme",
        });

        services.AddQuartz("initech", new NameValueCollection
        {
            ["quartz.plugin.recording.type"] = typeof(RecordingPlugin).AssemblyQualifiedName,
            ["quartz.plugin.recording.tenant"] = "initech",
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        await CreateSchedulers(provider, "acme", "initech");

        IReadOnlyList<(RecordingPlugin Plugin, string Scheduler)> initializations = log.Initializations;

        initializations.Select(x => x.Plugin.Tenant).Should().BeEquivalentTo(["acme", "initech"],
            "a type nobody registered was already built once per scheduler from that scheduler's keys, "
            + "and keying the probe must not have cost that");

        initializations[0].Plugin.Should().NotBeSameAs(initializations[1].Plugin);
    }

    /// <summary>
    /// Builds the default scheduler when one is registered, then each named one, so that plugins are
    /// initialized in the order the assertions read.
    /// </summary>
    private static async Task CreateSchedulers(IServiceProvider provider, params string[] names)
    {
        List<IScheduler> schedulers = [];

        ISchedulerFactory? shared = provider.GetService<ISchedulerFactory>();
        if (shared is not null)
        {
            schedulers.Add(await shared.GetScheduler());
        }

        foreach (string name in names)
        {
            schedulers.Add(await provider.GetRequiredKeyedService<ISchedulerFactory>(name).GetScheduler());
        }

        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown();
        }
    }

    private sealed class PluginLog
    {
        private readonly List<(RecordingPlugin Plugin, string Scheduler)> initializations = [];

        public IReadOnlyList<(RecordingPlugin Plugin, string Scheduler)> Initializations
        {
            get
            {
                lock (initializations)
                {
                    return [.. initializations];
                }
            }
        }

        public void Record(RecordingPlugin plugin, string scheduler)
        {
            lock (initializations)
            {
                initializations.Add((plugin, scheduler));
            }
        }
    }

    private sealed class RecordingPlugin : ISchedulerPlugin
    {
        private readonly PluginLog log;

        public RecordingPlugin(PluginLog log)
        {
            this.log = log;
        }

        public string Tenant { get; set; } = "";

        public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            log.Record(this, scheduler.SchedulerName);
            return default;
        }

        public ValueTask Start(CancellationToken cancellationToken = default) => default;

        public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;
    }
}
