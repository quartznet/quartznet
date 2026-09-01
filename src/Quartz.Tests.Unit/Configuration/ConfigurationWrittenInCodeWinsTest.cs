#nullable enable

using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Plugins.History;
using Quartz.Plugins.Json;
using Quartz.Plugins.Xml;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards the precedence <c>AddQuartz</c> documents: a setting written in code beats the same setting
/// written as a string, and one setting is read by one reader.
/// </summary>
/// <remarks>
/// <para>
/// The companion of <see cref="ConfigurationIsNeverSilentlyDroppedTest"/>. That one is about
/// configuration nobody reads; this one is about configuration two things read, where the answer
/// depends on which of them ran last. Both failures look the same from outside — a scheduler
/// configured as if the application had said something else — and neither fails a build.
/// </para>
/// <para>
/// Every case here is one that was wrong: plugins were configured while they were built and the
/// <c>quartz.plugin.*</c> keys were applied on top, so for every plugin shipped with Quartz a leftover
/// string beat the code that said otherwise; and <c>Quartz:ThreadPool:MaxConcurrency</c> was read by
/// the typed binder and again by the property bridge.
/// </para>
/// </remarks>
public class ConfigurationWrittenInCodeWinsTest
{
    private static IConfiguration Section(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => "Quartz:" + x.Key, x => x.Value))
            .Build()
            .GetSection("Quartz");
    }

    /// <summary>
    /// The plugins one scheduler ends up with, built the way the scheduler factory builds them.
    /// </summary>
    private static List<ISchedulerPlugin> Plugins(IServiceProvider provider, string? schedulerName = null)
    {
        SchedulerKey key = new(schedulerName);
        return SchedulerPluginFactory.Create(
                provider,
                provider.GetSchedulerServices<ISchedulerPlugin>(key.Key),
                provider.GetSchedulerProperties(key.OptionsName),
                key)
            .Select(x => x.Plugin)
            .ToList();
    }

    private static T Plugin<T>(IServiceProvider provider, string? schedulerName = null) where T : ISchedulerPlugin
    {
        return Plugins(provider, schedulerName).OfType<T>().Single();
    }

    [Test]
    public void APluginKeepsWhatCodeConfiguredWhenAFlatKeySaysOtherwise()
    {
        ServiceCollection services = new();
        services.AddQuartz(
            new NameValueCollection { ["quartz.plugin.jobHistory.jobSuccessMessage"] = "from the property bag" },
            q => q.UseJobHistoryLogging(options => options.JobSuccessMessage = "from code"));

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugin<LoggingJobHistoryPlugin>(provider).JobSuccessMessage.Should().Be("from code",
            "the flat keys are applied to a plugin before what it was configured with in code, because "
            + "an old configuration file must not quietly override the value the application asked for");
    }

    [Test]
    public void AFlatKeyStillReachesASettingCodeSaidNothingAbout()
    {
        ServiceCollection services = new();
        services.AddQuartz(
            new NameValueCollection { ["quartz.plugin.jobHistory.jobFailedMessage"] = "from the property bag" },
            q => q.UseJobHistoryLogging(options => options.JobSuccessMessage = "from code"));

        using ServiceProvider provider = services.BuildServiceProvider();
        LoggingJobHistoryPlugin plugin = Plugin<LoggingJobHistoryPlugin>(provider);

        plugin.JobFailedMessage.Should().Be("from the property bag",
            "code beating strings is per setting, not per plugin — configuring a plugin in code must not "
            + "silently discard the keys that configure the settings the code left alone");
        plugin.JobSuccessMessage.Should().Be("from code");
    }

    [Test]
    public void APluginOptionBoundFromConfigurationReachesThePlugin()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JobHistory:JobSuccessMessage"] = "from appsettings" })
            .Build();

        ServiceCollection services = new();
        services.Configure<JobHistoryLoggingOptions>(configuration.GetSection("JobHistory"));
        services.AddQuartz(q => q.UseJobHistoryLogging());

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugin<LoggingJobHistoryPlugin>(provider).JobSuccessMessage.Should().Be("from appsettings",
            "a plugin's options are the scheduler's own named options, so everything that configures "
            + "options — a configuration section most of all — reaches the plugin");
    }

    [Test]
    public void ThePluginCallbackBeatsTheConfigurationItsOptionsWereBoundFrom()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JobHistory:JobSuccessMessage"] = "from appsettings" })
            .Build();

        ServiceCollection services = new();
        services.Configure<JobHistoryLoggingOptions>(configuration.GetSection("JobHistory"));
        services.AddQuartz(q => q.UseJobHistoryLogging(options => options.JobSuccessMessage = "from code"));

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugin<LoggingJobHistoryPlugin>(provider).JobSuccessMessage.Should().Be("from code",
            "the callback is applied over the bound values, which is what makes it code beating strings "
            + "rather than a second source of them");
    }

    [Test]
    public void ANamedSchedulersPluginOptionsAreItsOwn()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JobHistory:JobSuccessMessage"] = "reporting only" })
            .Build();

        ServiceCollection services = new();
        services.Configure<JobHistoryLoggingOptions>("reporting", configuration.GetSection("JobHistory"));
        services.AddQuartz("reporting", q => q.UseJobHistoryLogging());
        services.AddQuartz(q => q.UseJobHistoryLogging());

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugin<LoggingJobHistoryPlugin>(provider, "reporting").JobSuccessMessage.Should().Be("reporting only");
        Plugin<LoggingJobHistoryPlugin>(provider).JobSuccessMessage.Should().NotBe("reporting only",
            "options named after a scheduler belong to that scheduler, or one tenant's configuration "
            + "would configure another's plugin");
    }

    [Test]
    public void TwoPluginsSharingAnOptionsTypeKeepTheirOwnConfiguration()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q
            .UseXmlSchedulingConfiguration(options => options.Files.Add("jobs.xml"))
            .UseJsonSchedulingConfiguration(options => options.Files.Add("jobs.json")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugin<XmlSchedulingDataProcessorPlugin>(provider).FileNames.Should().Be("jobs.xml",
            "both scheduling processors are configured by FileSchedulingOptions, so a callback registered "
            + "against the type rather than against this registration would hand each of them the "
            + "other's files");
        Plugin<JsonSchedulingDataProcessorPlugin>(provider).FileNames.Should().Be("jobs.json");
    }

    [Test]
    public void EveryShippedPluginIsConfiguredFromItsOwnRegistration()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q
            .UseJobHistoryLogging(options => options.JobFailedMessage = "job failed")
            .UseTriggerHistoryLogging(options => options.TriggerFiredMessage = "trigger fired")
            .UseStructuredJobLogging(options => options.JobWasVetoedMessage = "job vetoed")
            .UseStructuredTriggerLogging(options => options.TriggerCompleteMessage = "trigger complete")
            .UseXmlSchedulingConfiguration(options => options.Files.Add("jobs.xml"))
            .UseJsonSchedulingConfiguration(options => options.Files.Add("jobs.json")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugin<LoggingJobHistoryPlugin>(provider).JobFailedMessage.Should().Be("job failed");
        Plugin<LoggingTriggerHistoryPlugin>(provider).TriggerFiredMessage.Should().Be("trigger fired");
        Plugin<StructuredLoggingJobHistoryPlugin>(provider).JobWasVetoedMessage.Should().Be("job vetoed");
        Plugin<StructuredLoggingTriggerHistoryPlugin>(provider).TriggerCompleteMessage.Should().Be("trigger complete");
        Plugin<XmlSchedulingDataProcessorPlugin>(provider).FileNames.Should().Be("jobs.xml");
        Plugin<JsonSchedulingDataProcessorPlugin>(provider).FileNames.Should().Be("jobs.json");

        Plugin<LoggingJobHistoryPlugin>(provider).JobWasVetoedMessage.Should().NotBe("job vetoed",
            "the classic and structured history plugins share an options type, and a callback registered "
            + "against the type rather than against one registration would configure both of them");
        Plugin<StructuredLoggingJobHistoryPlugin>(provider).JobFailedMessage.Should().NotBe("job failed");
    }

    [Test]
    public void TheThreadPoolSectionIsReadByExactlyOneReader()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(new Dictionary<string, string?> { ["ThreadPool:MaxConcurrency"] = "7" }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ThreadPoolOptions>>().Value.MaxConcurrency.Should().Be(7);
        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties
            .Should().NotContainKey("quartz.threadPool.maxConcurrency",
                "the section binds onto ThreadPoolOptions, so flattening it as well would have the one "
                + "value written by two readers whose order decides the answer");
    }

    [Test]
    public void TheThreadPoolKeysWithNoTypedHomeAreStillFlattened()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(new Dictionary<string, string?>
        {
            ["ThreadPool:Type"] = "Quartz.Impl.DefaultThreadPool, Quartz",
            ["ThreadPool:MaxConcurrency"] = "7",
        }));

        using ServiceProvider provider = services.BuildServiceProvider();
        Dictionary<string, string?> properties = provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties;

        properties.Should().ContainKey("quartz.threadPool.type",
            "the type key selects an implementation and the bridge is its only reader, so dropping the "
            + "section from the flattened form would leave the pool unregistered");
        provider.GetRequiredService<IThreadPool>().Should().BeOfType<DefaultThreadPool>()
            .Which.MaxConcurrency.Should().Be(7,
                "the two halves of one section are read by different readers, and both have to land on "
                + "the same pool");
    }

    [Test]
    public void TheLegacyThreadCountSpellingStillReaches()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(new Dictionary<string, string?> { ["ThreadPool:ThreadCount"] = "5" }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ThreadPoolOptions>>().Value.MaxConcurrency.Should().Be(5,
            "threadCount is not a property of ThreadPoolOptions, so the bridge is its only reader and "
            + "the section has to keep reaching it");
    }

    [Test]
    public void TheFlatSpellingOfMaxConcurrencyStillReaches()
    {
        ServiceCollection sections = new();
        sections.AddQuartz(Section(new Dictionary<string, string?> { ["quartz.threadPool.maxConcurrency"] = "3" }));

        ServiceCollection properties = new();
        properties.AddQuartz(new NameValueCollection { ["quartz.threadPool.maxConcurrency"] = "3" });

        using ServiceProvider fromSection = sections.BuildServiceProvider();
        using ServiceProvider fromProperties = properties.BuildServiceProvider();

        fromSection.GetRequiredService<IOptions<ThreadPoolOptions>>().Value.MaxConcurrency.Should().Be(3,
            "a flat key written in a configuration section is passed through rather than synthesized, so "
            + "the bridge still reads it");
        fromProperties.GetRequiredService<IOptions<ThreadPoolOptions>>().Value.MaxConcurrency.Should().Be(3,
            "AddQuartz(NameValueCollection) has no configuration to bind, so the bridge is the only "
            + "reader that path ever had");
    }

    [Test]
    public void TheSchedulerNameIsReadByExactlyOneReader()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(new Dictionary<string, string?> { ["Scheduler:InstanceName"] = "reporting" }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.InstanceName.Should().Be("reporting");
        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties
            .Should().NotContainKey("quartz.scheduler.instanceName",
                "the section binds onto QuartzSchedulerOptions.InstanceName and the bridge reads the "
                + "same key onto the same property, so flattening it too is one value with two writers");
    }

    [Test]
    public void ANamedSchedulerKeepsTheNameItWasRegisteredUnder()
    {
        ServiceCollection services = new();
        services.AddQuartz("tenant-a", Section(new Dictionary<string, string?> { ["Scheduler:InstanceName"] = "something-else" }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get("tenant-a").InstanceName
            .Should().Be("tenant-a",
                "a named scheduler's name is fixed by its registration, and neither reader was ever "
                + "allowed to move it");
    }

    [Test]
    public void TheFlatSpellingOfTheSchedulerNameStillReaches()
    {
        ServiceCollection sections = new();
        sections.AddQuartz(Section(new Dictionary<string, string?> { ["quartz.scheduler.instanceName"] = "reporting" }));

        ServiceCollection properties = new();
        properties.AddQuartz(new NameValueCollection { ["quartz.scheduler.instanceName"] = "reporting" });

        using ServiceProvider fromSection = sections.BuildServiceProvider();
        using ServiceProvider fromProperties = properties.BuildServiceProvider();

        fromSection.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.InstanceName.Should().Be("reporting",
            "a flat key written in a configuration section is passed through rather than synthesized");
        fromProperties.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.InstanceName.Should().Be("reporting",
            "and the bridge is the only reader the NameValueCollection path ever had");
    }

    [Test]
    public void TheBatchSizeSectionSynthesizesNoKeyThatIsNotRead()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(new Dictionary<string, string?> { ["Scheduler:MaxBatchSize"] = "9" }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.MaxBatchSize.Should().Be(9);
        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties
            .Should().NotContainKey("quartz.scheduler.maxBatchSize",
                "the bridge's spelling is quartz.scheduler.batchTriggerAcquisitionMaxCount, so this one "
                + "is a key no reader consults and the validator rejects by name");
    }

    [Test]
    public void TheLegacyBatchSizeSpellingStillReaches()
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection { ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "9" });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.MaxBatchSize.Should().Be(9,
            "the legacy key is a different string from the property's own name, and the bridge is its "
            + "only reader");
    }

    [Test]
    public void TheSchedulerContextSectionSynthesizesNoKeyThatIsNotRead()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(new Dictionary<string, string?> { ["Scheduler:Context:environment"] = "staging" }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.Context
            .Should().ContainKey("environment").WhoseValue.Should().Be("staging");
        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties
            .Should().NotContainKey("quartz.scheduler.context.environment",
                "the bridge's spelling is quartz.context.key.*, so the whole synthesized subtree is keys "
                + "no reader consults");
    }

    [Test]
    public void TheLegacyContextSpellingStillReaches()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(new Dictionary<string, string?> { ["Context:key:environment"] = "staging" }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties
            .Should().ContainKey("quartz.context.key.environment",
                "nothing typed binds Quartz:Context, so the bridge is the only reader it has");
        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.Context
            .Should().ContainKey("environment").WhoseValue.Should().Be("staging");
    }

    /// <summary>
    /// The consequence that made the two unread spellings worth removing rather than merely untidy.
    /// </summary>
    /// <remarks>
    /// <see cref="QuartzOptions.ToProperties" /> is how one scheduler's keys are handed to another, and
    /// <c>AddQuartz(NameValueCollection)</c> is what takes them. A synthesized key the validator does not
    /// know made that round trip fail on keys Quartz had put in the bag itself.
    /// </remarks>
    [Test]
    public void TheKeysASectionFlattensIntoAreKeysAddQuartzAccepts()
    {
        ServiceCollection sections = new();
        sections.AddQuartz(Section(new Dictionary<string, string?>
        {
            ["Scheduler:InstanceName"] = "reporting",
            ["Scheduler:MaxBatchSize"] = "9",
            ["Scheduler:Context:environment"] = "staging",
            ["ThreadPool:MaxConcurrency"] = "7",
        }));

        using ServiceProvider fromSection = sections.BuildServiceProvider();

        NameValueCollection properties = [];
        foreach (KeyValuePair<string, string?> pair in fromSection.GetRequiredService<IOptions<QuartzOptions>>().Value.ToProperties())
        {
            properties[pair.Key] = pair.Value;
        }

        ServiceCollection round = new();
        round.AddQuartz(properties);

        using ServiceProvider fromProperties = round.BuildServiceProvider();

        Action read = () => fromProperties.GetRequiredService<IOptions<QuartzSchedulerOptions>>();

        read.Should().NotThrow(
            "every key the flattener writes has to be one the validator accepts, or a scheduler "
            + "configured from a section cannot hand its own properties to another one");
    }

    [Test]
    public void CodeStillBeatsTheThreadPoolSection()
    {
        ServiceCollection services = new();
        services.AddQuartz(
            Section(new Dictionary<string, string?> { ["ThreadPool:MaxConcurrency"] = "7" }),
            q => q.UseDefaultThreadPool(options => options.MaxConcurrency = 3));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ThreadPoolOptions>>().Value.MaxConcurrency.Should().Be(3,
            "options are last-wins and the callback runs after the section is bound");
    }
}
