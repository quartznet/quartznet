#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.Unit.DashboardAgent;

/// <summary>
/// <c>UseDashboardAgent</c> reads <c>Quartz:DashboardAgent</c>, or a named scheduler's own section, and a
/// callback written in code runs over what it read.
/// </summary>
public class DashboardAgentConfigurationTest
{
    private static readonly Uri Hub = new("https://ops.example.com/quartz/agents");

    [Test]
    public void TheOverloadWithoutACallbackIsConfiguredFromTheQuartzSection()
    {
        using ServiceProvider provider = Build(
            new Dictionary<string, string?>
            {
                ["Quartz:DashboardAgent:Endpoint"] = Hub.ToString(),
                ["Quartz:DashboardAgent:Token"] = "agent-token",
                ["Quartz:DashboardAgent:Target"] = "worker",
                ["Quartz:DashboardAgent:ReadOnly"] = "true",
                ["Quartz:DashboardAgent:MaxAccess"] = "Operate",
                ["Quartz:DashboardAgent:HeartbeatInterval"] = "00:00:05",
                ["Quartz:DashboardAgent:MaxConcurrentOperations"] = "2",
                ["Quartz:DashboardAgent:MaxPageSize"] = "50",
            },
            quartz => quartz.UseDashboardAgent());

        DashboardAgentOptions options = provider.GetRequiredService<IOptionsMonitor<DashboardAgentOptions>>().Get(Options.DefaultName);

        options.Endpoint.Should().Be(Hub);
        options.Token.Should().Be("agent-token");
        options.Target.Should().Be("worker");
        options.ReadOnly.Should().BeTrue();
        options.MaxAccess.Should().Be(SchedulerAccess.Operate);
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxConcurrentOperations.Should().Be(2);
        options.MaxPageSize.Should().Be(50);
    }

    [Test]
    public void TheCallbackRunsOverWhatConfigurationSaid()
    {
        using ServiceProvider provider = Build(
            new Dictionary<string, string?>
            {
                ["Quartz:DashboardAgent:Endpoint"] = Hub.ToString(),
                ["Quartz:DashboardAgent:Token"] = "from-configuration",
                ["Quartz:DashboardAgent:Target"] = "worker",
            },
            quartz => quartz.UseDashboardAgent(options => options.Token = "from-code"));

        DashboardAgentOptions options = provider.GetRequiredService<IOptionsMonitor<DashboardAgentOptions>>().Get(Options.DefaultName);

        options.Token.Should().Be("from-code", "code has the last word over a setting");
        options.Endpoint.Should().Be(Hub, "a key the callback leaves alone keeps what configuration said");
        options.Target.Should().Be("worker");
    }

    [Test]
    public void AKeyThatIsAbsentLeavesTheDefault()
    {
        using ServiceProvider provider = Build(
            new Dictionary<string, string?>
            {
                ["Quartz:DashboardAgent:Endpoint"] = Hub.ToString(),
                ["Quartz:DashboardAgent:Token"] = "agent-token",
            },
            quartz => quartz.UseDashboardAgent());

        DashboardAgentOptions options = provider.GetRequiredService<IOptionsMonitor<DashboardAgentOptions>>().Get(Options.DefaultName);

        options.MaxAccess.Should().Be(SchedulerAccess.Administer);
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(15));
        options.MaxConcurrentOperations.Should().Be(4);
        options.MaxPageSize.Should().Be(1000);
        options.ReadOnly.Should().BeFalse();
        options.Target.Should().BeNull("unset, the agent registers under the machine's name");
    }

    [Test]
    public void ANamedSchedulerReadsItsOwnSectionWhenItHasOne()
    {
        using ServiceProvider provider = Build(
            new Dictionary<string, string?>
            {
                ["Quartz:DashboardAgent:Endpoint"] = Hub.ToString(),
                ["Quartz:DashboardAgent:Token"] = "shared",
                ["Quartz:DashboardAgent:Target"] = "shared",
                ["Quartz:Schedulers:billing:DashboardAgent:Endpoint"] = Hub.ToString(),
                ["Quartz:Schedulers:billing:DashboardAgent:Token"] = "billing-token",
                ["Quartz:Schedulers:billing:DashboardAgent:Target"] = "worker-billing",
            },
            quartz => quartz.UseDashboardAgent(),
            schedulers: ["billing", "orders"]);

        IOptionsMonitor<DashboardAgentOptions> monitor = provider.GetRequiredService<IOptionsMonitor<DashboardAgentOptions>>();

        monitor.Get("billing").Target.Should().Be("worker-billing");
        monitor.Get("billing").Token.Should().Be("billing-token");
        monitor.Get("orders").Target.Should().Be("shared",
            "a named scheduler without a section of its own reads the shared one, as AddQuartz(name, configuration) does");
    }

    [Test]
    public void AContainerWithoutConfigurationLeavesTheOptionsToTheCallback()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.UseDashboardAgent(options =>
        {
            options.Endpoint = Hub;
            options.Token = "agent-token";
            options.Target = "worker";
        }));

        using ServiceProvider provider = services.BuildServiceProvider();
        DashboardAgentOptions options = provider.GetRequiredService<IOptionsMonitor<DashboardAgentOptions>>().Get(Options.DefaultName);

        options.Target.Should().Be("worker");
        options.Endpoint.Should().Be(Hub);
    }

    [Test]
    public void AnAgentNamedNowhereIsRefusedNamingTheSetting()
    {
        using ServiceProvider provider = Build(new Dictionary<string, string?>(), quartz => quartz.UseDashboardAgent());

        Action resolve = () => _ = provider.GetRequiredService<IOptionsMonitor<DashboardAgentOptions>>().Get(Options.DefaultName);

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*Quartz:DashboardAgent:Endpoint*");
    }

    private static ServiceProvider Build(
        Dictionary<string, string?> settings,
        Action<IQuartzBuilder> configure,
        string[]? schedulers = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        if (schedulers is null)
        {
            services.AddQuartz(configure);
        }
        else
        {
            foreach (string name in schedulers)
            {
                services.AddQuartz(name, configure);
            }
        }

        return services.BuildServiceProvider();
    }
}
