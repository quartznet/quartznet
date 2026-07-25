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

    private static ServiceProvider Build(Dictionary<string, string> values, string schedulerName = null, bool persistent = false)
    {
        var services = new ServiceCollection();
        services.BindQuartzOptions(Section(values), schedulerName, persistent);
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
            ["Scheduler:InterruptJobsOnShutdown"] = "true",
        });

        var options = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.InstanceName, Is.EqualTo("core"));
            Assert.That(options.InstanceId, Is.EqualTo("node-1"));
            Assert.That(options.IdleWaitTime, Is.EqualTo(TimeSpan.FromSeconds(45)));
            Assert.That(options.MaxBatchSize, Is.EqualTo(7));
            Assert.That(options.InterruptJobsOnShutdown, Is.True);
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(scheduler.InstanceId, Is.EqualTo(QuartzSchedulerOptions.DefaultInstanceId));
            Assert.That(scheduler.IdleWaitTime, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(scheduler.MaxBatchSize, Is.EqualTo(1));
            Assert.That(threadPool.MaxConcurrency, Is.EqualTo(ThreadPoolOptions.DefaultMaxConcurrency));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(options.Context["environment"], Is.EqualTo("staging"));
            Assert.That(options.Context["region"], Is.EqualTo("eu-north"));
        });
    }

    [Test]
    public void JobStoreSection_BindsToInMemoryOptionsByDefault()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["JobStore:MisfireThreshold"] = "00:00:30",
        });

        var options = provider.GetRequiredService<IOptions<InMemoryJobStoreOptions>>().Value;

        Assert.That(options.MisfireThreshold, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void JobStoreSection_BindsToAdoOptionsWhenPersistent()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["JobStore:DataSource"] = "default",
            ["JobStore:TablePrefix"] = "MY_",
            ["JobStore:UseProperties"] = "true",
            ["JobStore:Clustered"] = "true",
            ["JobStore:UseDbLocks"] = "true",
            ["JobStore:ClusterCheckinInterval"] = "00:00:10",
        }, persistent: true);

        var options = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.DataSource, Is.EqualTo("default"));
            Assert.That(options.TablePrefix, Is.EqualTo("MY_"));
            Assert.That(options.UseProperties, Is.True);
            Assert.That(options.Clustered, Is.True);
            Assert.That(options.ClusterCheckinInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.PerformSchemaValidation, Is.True, "unset booleans keep their default");
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Get("primary").Provider, Is.EqualTo("SqlServer"));
            Assert.That(monitor.Get("primary").ConnectionString, Is.EqualTo("Server=a"));
            Assert.That(monitor.Get("reporting").Provider, Is.EqualTo("Npgsql"));
            Assert.That(monitor.Get("reporting").ConnectionStringName, Is.EqualTo("reportingDb"));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(scheduler.Get("reporting").InstanceName, Is.EqualTo("reporting"));
            Assert.That(threadPool.Get("reporting").MaxConcurrency, Is.EqualTo(3));
            Assert.That(threadPool.Get(Options.DefaultName).MaxConcurrency, Is.EqualTo(ThreadPoolOptions.DefaultMaxConcurrency),
                "the default scheduler must not pick up a named scheduler's configuration");
        });
    }

    [Test]
    public void IdleWaitTimeBelowOneSecond_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["Scheduler:IdleWaitTime"] = "00:00:00.500",
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value);

        Assert.That(exception!.Message, Does.Contain(nameof(QuartzSchedulerOptions.IdleWaitTime)));
    }

    [Test]
    public void ClusteringWithoutDbLocks_FailsValidation()
    {
        using var provider = Build(new Dictionary<string, string>
        {
            ["JobStore:DataSource"] = "default",
            ["JobStore:Clustered"] = "true",
        }, persistent: true);

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value);

        Assert.That(exception!.Message, Does.Contain(nameof(AdoJobStoreOptions.UseDbLocks)));
    }

    [Test]
    public void PersistentStoreWithoutDataSource_FailsValidation()
    {
        using var provider = Build([], persistent: true);

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value);

        Assert.That(exception!.Message, Does.Contain(nameof(AdoJobStoreOptions.DataSource)));
    }
}
