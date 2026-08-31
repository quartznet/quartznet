#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

/// <summary>
/// Registering Quartz on a host application builder, which has both halves of what
/// <c>AddQuartz(configuration, configure)</c> needs.
/// </summary>
public sealed class HostApplicationBuilderTest
{
    [Test]
    public void AddQuartz_ReadsTheQuartzConfigurationSection()
    {
        HostApplicationBuilder builder = Builder(new Dictionary<string, string?>
        {
            ["Quartz:quartz.scheduler.instanceName"] = "from-configuration",
            ["Quartz:ThreadPool:MaxConcurrency"] = "7",
        });

        builder.AddQuartz();

        using IHost host = builder.Build();

        host.Services.GetRequiredService<IScheduler>().SchedulerName.Should().Be("from-configuration");
        host.Services.SchedulerOptions<ThreadPoolOptions>().MaxConcurrency.Should().Be(7);
    }

    [Test]
    public void AddQuartz_AppliesTheCallbackOverTheConfiguration()
    {
        HostApplicationBuilder builder = Builder(new Dictionary<string, string?>
        {
            ["Quartz:quartz.scheduler.instanceName"] = "from-configuration",
        });

        builder.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "from-code"));

        using IHost host = builder.Build();

        host.Services.GetRequiredService<IScheduler>().SchedulerName.Should().Be("from-code",
            "configuration written in code beats a configuration file, here as everywhere else");
    }

    [Test]
    public void AddQuartz_WithNoQuartzSection_RegistersTheSchedulerAnyway()
    {
        HostApplicationBuilder builder = Builder([]);

        builder.AddQuartz(q => q.UseInMemoryStore());

        using IHost host = builder.Build();

        host.Services.GetRequiredService<IScheduler>().SchedulerName.Should().Be("QuartzScheduler");
    }

    /// <summary>
    /// A string names a scheduler here, as it does everywhere else in Quartz — never a configuration
    /// section.
    /// </summary>
    [Test]
    public void AddQuartz_WithAName_RegistersThatSchedulerFromItsOwnSection()
    {
        HostApplicationBuilder builder = Builder(new Dictionary<string, string?>
        {
            ["Quartz:Schedulers:reporting:ThreadPool:MaxConcurrency"] = "3",
        });

        builder.AddQuartz("reporting");

        using IHost host = builder.Build();

        host.Services.GetRequiredKeyedService<IScheduler>("reporting").SchedulerName.Should().Be("reporting");
        host.Services.SchedulerOptions<ThreadPoolOptions>("reporting").MaxConcurrency.Should().Be(3);
    }

    [Test]
    public void AddQuartzSchedulers_RegistersOnePerChildOfTheSchedulersSection()
    {
        HostApplicationBuilder builder = Builder(new Dictionary<string, string?>
        {
            ["Quartz:Schedulers:reporting:ThreadPool:MaxConcurrency"] = "3",
            ["Quartz:Schedulers:billing:ThreadPool:MaxConcurrency"] = "5",
        });

        builder.AddQuartzSchedulers();

        using IHost host = builder.Build();

        host.Services.SchedulerOptions<ThreadPoolOptions>("reporting").MaxConcurrency.Should().Be(3);
        host.Services.SchedulerOptions<ThreadPoolOptions>("billing").MaxConcurrency.Should().Be(5);
    }

    /// <summary>
    /// The one difference between the two receivers, said out loud: a builder holds its configuration
    /// and a service collection is handed one.
    /// </summary>
    /// <remarks>
    /// <c>AddQuartzSchedulers</c> takes an <see cref="IConfiguration"/> on <c>IServiceCollection</c> and
    /// none on <c>IHostApplicationBuilder</c>, which reads like two contracts under one name until you
    /// see that <c>AddQuartz</c> differs in exactly the same way and for the same reason. What would
    /// make them genuinely incompatible is a service collection that read configuration it was never
    /// given, so that is what this pins.
    /// </remarks>
    [Test]
    public void AServiceCollectionReadsNoConfigurationItWasNotHanded()
    {
        HostApplicationBuilder builder = Builder(new Dictionary<string, string?>
        {
            ["Quartz:quartz.scheduler.instanceName"] = "from-configuration",
            ["Quartz:ThreadPool:MaxConcurrency"] = "7",
        });

        // The container's own IConfiguration says all of that, and this overload was handed none of it.
        builder.Services.AddQuartz();

        using IHost host = builder.Build();

        host.Services.GetRequiredService<IScheduler>().SchedulerName.Should().Be("QuartzScheduler",
            "services.AddQuartz(configure) means a scheduler configured entirely in code; a receiver "
            + "that went looking for an ambient section would configure schedulers a caller never "
            + "described");
        host.Services.SchedulerOptions<ThreadPoolOptions>().MaxConcurrency
            .Should().Be(ThreadPoolOptions.DefaultMaxConcurrency);
    }

    [Test]
    public void AddQuartzHostedService_RegistersTheHostedService()
    {
        HostApplicationBuilder builder = Builder([]);

        builder.AddQuartz();
        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        using IHost host = builder.Build();

        host.Services.GetServices<IHostedService>().Should().ContainSingle()
            .Which.Should().BeOfType<QuartzHostedService>();
    }

    private static HostApplicationBuilder Builder(Dictionary<string, string?> configuration)
    {
        // Empty rather than default: the test's own configuration is the whole of what the builder reads,
        // so an appsettings.json left in the test output cannot change the answer.
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }
}

file static class SchedulerOptionsExtensions
{
    public static TOptions SchedulerOptions<TOptions>(this IServiceProvider provider, string? schedulerName = null)
        where TOptions : class
    {
        return provider.GetSchedulerOptions<TOptions>(schedulerName);
    }
}
