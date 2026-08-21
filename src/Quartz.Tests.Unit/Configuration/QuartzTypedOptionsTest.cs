using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

public class QuartzTypedOptionsTest
{
    private static IConfiguration Section(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => "Quartz:" + x.Key, x => x.Value))
            .Build()
            .GetSection("Quartz");
    }

    private static ServiceProvider Build(Dictionary<string, string> values, string schedulerName = null)
    {
        var services = new ServiceCollection();
        services.BindQuartzOptions(Section(values), schedulerName);
        return services.BuildServiceProvider();
    }

    [Test]
    public void SchedulerSection_BindsDirectlyOntoOptions()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["Scheduler:InstanceName"] = "core",
            ["Scheduler:InstanceId"] = "node-1",
            ["Scheduler:IdleWaitTime"] = "00:00:45",
            ["Scheduler:MaxBatchSize"] = "7",
        });

        var options = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;

        options.InstanceName.Should().Be("core");
        options.InstanceId.Should().Be("node-1");
        options.IdleWaitTime.Should().Be(TimeSpan.FromSeconds(45));
        options.MaxBatchSize.Should().Be(7);
    }

    [Test]
    public void ShutdownJobInterruption_BindsByName()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["Scheduler:ShutdownJobInterruption"] = "WhenWaitingForJobs",
        });

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.ShutdownJobInterruption
            .Should().Be(ShutdownJobInterruption.WhenWaitingForJobs);
    }

    [Test]
    public void UnsetValues_KeepTheirDefaults()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["Scheduler:InstanceName"] = "core",
        });

        var scheduler = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;
        var threadPool = provider.GetRequiredService<IOptions<ThreadPoolOptions>>().Value;

        scheduler.InstanceId.Should().Be(QuartzSchedulerOptions.DefaultInstanceId);
        scheduler.IdleWaitTime.Should().Be(TimeSpan.FromSeconds(30));
        scheduler.MaxBatchSize.Should().Be(1);
        threadPool.MaxConcurrency.Should().Be(ThreadPoolOptions.DefaultMaxConcurrency);
    }

    [Test]
    public void SchedulerContext_BindsAsDictionary()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["Scheduler:Context:environment"] = "staging",
            ["Scheduler:Context:region"] = "eu-north",
        });

        var options = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;

        options.Context["environment"].Should().Be("staging");
        options.Context["region"].Should().Be("eu-north");
    }

    [Test]
    public void JobStoreSection_BindsToInMemoryOptionsByDefault()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["JobStore:MisfireThreshold"] = "00:00:30",
        });

        var options = provider.GetRequiredService<IOptions<InMemoryJobStoreOptions>>().Value;

        options.MisfireThreshold.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void JobStoreSection_BindsToAdoOptions()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["JobStore:DataSource"] = "default",
            ["JobStore:TablePrefix"] = "MY_",
            ["JobStore:UseProperties"] = "true",
            ["JobStore:UseDbLocks"] = "true",
            ["JobStore:Clustering:Enabled"] = "true",
            ["JobStore:Clustering:CheckinInterval"] = "00:00:10",
        });

        var options = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;

        options.DataSource.Should().Be("default");
        options.TablePrefix.Should().Be("MY_");
        options.UseProperties.Should().BeTrue();
        options.UseDbLocks.Should().BeTrue();
        options.PerformSchemaValidation.Should().BeTrue("unset booleans keep their default");

        var clustering = provider.GetRequiredService<IOptions<ClusteringOptions>>().Value;

        clustering.Enabled.Should().BeTrue(
            "clustering has a sub-section of its own, so it binds onto ClusteringOptions rather than the store's");
        clustering.CheckinInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void DataSources_BindAsNamedOptions()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["DataSource:primary:Provider"] = "SqlServer",
            ["DataSource:primary:ConnectionString"] = "Server=a",
            ["DataSource:reporting:Provider"] = "Npgsql",
            ["DataSource:reporting:ConnectionStringName"] = "reportingDb",
        });

        var monitor = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>();

        monitor.Get("primary").Provider.Should().Be("SqlServer");
        monitor.Get("primary").ConnectionString.Should().Be("Server=a");
        monitor.Get("reporting").Provider.Should().Be("Npgsql");
        monitor.Get("reporting").ConnectionStringName.Should().Be("reportingDb");
    }

    [Test]
    public void NamedScheduler_GetsItsOwnOptionsAndInstanceName()
    {
        var services = new ServiceCollection();
        services.BindQuartzOptions(Section(new Dictionary<string, string>
        {
            ["ThreadPool:MaxConcurrency"] = "3",
        }), schedulerName: "reporting");

        using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>();
        var threadPool = provider.GetRequiredService<IOptionsMonitor<ThreadPoolOptions>>();

        scheduler.Get("reporting").InstanceName.Should().Be("reporting");
        threadPool.Get("reporting").MaxConcurrency.Should().Be(3);
        threadPool.Get(Options.DefaultName).MaxConcurrency.Should().Be(ThreadPoolOptions.DefaultMaxConcurrency, "the default scheduler must not pick up a named scheduler's configuration");
    }

    [Test]
    public void IdleWaitTimeBelowOneSecond_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["Scheduler:IdleWaitTime"] = "00:00:00.500",
        });

        var act = () => provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(QuartzSchedulerOptions.IdleWaitTime)}*");
    }

    /// <summary>
    /// Clustering used to be validated against the store's <c>UseDbLocks</c>, which the validator can no
    /// longer see and never needed to: every path that enables clustering enables database locking with
    /// it. What is left to get wrong is an interval, and that is validated where it now lives.
    /// </summary>
    [Test]
    public void ClusteringWithANonPositiveCheckinInterval_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["JobStore:DataSource"] = "default",
            ["JobStore:Clustering:Enabled"] = "true",
            ["JobStore:Clustering:CheckinInterval"] = "00:00:00",
        });

        var act = () => provider.GetRequiredService<IOptions<ClusteringOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(ClusteringOptions.CheckinInterval)}*");
    }

    [Test]
    public void PersistentStoreWithoutDataSource_FailsValidation()
    {
        using var provider = Build([]);

        var act = () => provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(AdoJobStoreOptions.DataSource)}*");
    }
}
