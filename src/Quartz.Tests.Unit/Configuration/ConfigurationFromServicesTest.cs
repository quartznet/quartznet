#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Configuration whose value comes from a service rather than from a literal: what reaches the
/// scheduler, and — the part a caller cannot see for themselves — that it arrives through the whole
/// options pipeline rather than around it.
/// </summary>
/// <remarks>
/// <para>
/// The 3.x answer was <c>AddQuartz((q, provider) =&gt; …)</c>, which built a throwaway container to run
/// the callback against. 4.x has no such overload: a value that depends on a service is read where it is
/// used, when the scheduler is built and the real container exists. That is only an improvement if
/// <c>Configure</c>, <c>PostConfigure</c> and <c>IValidateOptions</c> have all run by then, which is what
/// these tests pin — reading <c>IConfiguration</c> at registration time, the shape the migration guide
/// used to lead with, runs none of them.
/// </para>
/// <para>
/// Reported as #3794.
/// </para>
/// </remarks>
public class ConfigurationFromServicesTest
{
    [Test]
    public async Task ExecutionLimitsCanBeReadFromTheContainer()
    {
        var services = new ServiceCollection();
        services.Configure<QueueOptions>(options => options.MaxConcurrent = 3);
        services.AddQuartz(q => q.UseExecutionLimits((provider, limits) => limits.ForGroup(
            "heavy",
            provider.GetRequiredService<IOptions<QueueOptions>>().Value.MaxConcurrent)));

        using var provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            LimitFor(await scheduler.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(3,
                "the callback runs once the container exists, so IOptions<T> answers with what was configured");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// The headline. The value exists only because <c>PostConfigure</c> ran, so a limit that carries it
    /// proves the whole pipeline was walked rather than the configuration section merely read.
    /// </summary>
    [Test]
    public async Task ExecutionLimitsSeeWhatPostConfigureAndValidationContributed()
    {
        var validator = new RecordingValidator();

        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<QueueOptions>>(validator);
        services.Configure<QueueOptions>(options => options.MaxConcurrent = 3);
        services.PostConfigure<QueueOptions>(options => options.MaxConcurrent *= 2);
        services.AddQuartz(q => q.UseExecutionLimits((provider, limits) => limits.ForGroup(
            "heavy",
            provider.GetRequiredService<IOptions<QueueOptions>>().Value.MaxConcurrent)));

        using var provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            LimitFor(await scheduler.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(6,
                "reading the configuration section at registration time would have answered 3 - the "
                + "post-configuration is the thing that only the options pipeline applies");
            validator.Ran.Should().BeTrue(
                "validation runs on the same resolution, so a setting the application refuses is refused here too");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task EachNamedSchedulerReadsItsOwnLimitsFromTheContainer()
    {
        var services = new ServiceCollection();
        services.Configure<QueueOptions>("reporting", options => options.MaxConcurrent = 2);
        services.Configure<QueueOptions>("ingest", options => options.MaxConcurrent = 7);

        foreach (string name in new[] { "reporting", "ingest" })
        {
            services.AddQuartz(name, q => q.UseExecutionLimits((provider, limits) => limits.ForGroup(
                "heavy",
                provider.GetRequiredService<IOptionsMonitor<QueueOptions>>().Get(q.SchedulerName).MaxConcurrent)));
        }

        using var provider = services.BuildServiceProvider();
        IScheduler reporting = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();
        IScheduler ingest = await provider.GetRequiredKeyedService<ISchedulerFactory>("ingest").GetScheduler();
        try
        {
            LimitFor(await reporting.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(2);
            LimitFor(await ingest.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(7,
                "each scheduler's callback is handed its own view of the container");
        }
        finally
        {
            await reporting.Shutdown();
            await ingest.Shutdown();
        }
    }

    /// <summary>
    /// Precedence does not change with the shape: registration is first-wins, for both of them.
    /// </summary>
    [Test]
    public async Task TheFirstLimitsDeclaredInCodeWinWhicheverShapeDeclaredThem()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseExecutionLimits(limits => limits.ForGroup("heavy", 1));
            q.UseExecutionLimits((_, limits) => limits.ForGroup("heavy", 9));
        });

        using var provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            LimitFor(await scheduler.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(1,
                "both shapes TryAdd the same registration, so the second call is the one that loses");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task LimitsDeclaredInCodeStillBeatTheSameLimitsSpelledAsKeys()
    {
        var properties = new System.Collections.Specialized.NameValueCollection
        {
            ["quartz.executionLimit.heavy"] = "4",
        };

        var services = new ServiceCollection();
        services.AddQuartz(properties, q => q.UseExecutionLimits((_, limits) => limits.ForGroup("heavy", 9)));

        using var provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            LimitFor(await scheduler.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(9,
                "the callback runs before the property-derived registration and both TryAdd, so the "
                + "deferred shape keeps the precedence the eager one has");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// The general route, which covers everything that lands in typed options: the application's own
    /// service reaches a Quartz option through <c>AddOptions&lt;T&gt;().Configure&lt;TDep&gt;</c>.
    /// </summary>
    [Test]
    public void AQuartzOptionCanBeConfiguredFromAnotherService()
    {
        var services = new ServiceCollection();
        services.Configure<QueueOptions>(options => options.MaxConcurrent = 3);
        services.PostConfigure<QueueOptions>(options => options.MaxConcurrent *= 2);
        services.AddOptions<ThreadPoolOptions>()
            .Configure<IOptions<QueueOptions>>((options, queue) => options.MaxConcurrency = queue.Value.MaxConcurrent);
        services.AddQuartz();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ThreadPoolOptions>>().Value.MaxConcurrency.Should().Be(6,
            "the option is resolved after the container is built, so everything the application "
            + "contributed to the service it depends on has been applied");
    }

    /// <summary>
    /// The caveat the same route carries: a scheduler's options are its own named instance, so the
    /// unnamed one configures nothing of a named scheduler's.
    /// </summary>
    [Test]
    public void ANamedSchedulersOptionsAreConfiguredUnderItsName()
    {
        var services = new ServiceCollection();
        services.AddOptions<ThreadPoolOptions>("reporting").Configure(options => options.MaxConcurrency = 5);
        services.AddOptions<ThreadPoolOptions>().Configure(options => options.MaxConcurrency = 11);
        services.AddQuartz("reporting", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<ThreadPoolOptions>>().Get("reporting").MaxConcurrency
            .Should().Be(5, "the scheduler's name is the options name, which is what AddQuartz configures under");
    }

    private static int? LimitFor(ExecutionLimits? limits, ExecutionGroupScope scope)
    {
        limits.Should().NotBeNull();
        limits!.TryGetLimit(scope, out int? limit).Should().BeTrue($"execution scope '{scope}' should be configured");
        return limit;
    }

    private sealed class QueueOptions
    {
        public int MaxConcurrent { get; set; }
    }

    private sealed class RecordingValidator : IValidateOptions<QueueOptions>
    {
        public bool Ran { get; private set; }

        public ValidateOptionsResult Validate(string? name, QueueOptions options)
        {
            Ran = true;
            return ValidateOptionsResult.Success;
        }
    }
}
