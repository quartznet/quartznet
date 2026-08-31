#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.Unit.HealthChecks;

public class QuartzHealthCheckRegistrationTests
{
    [Test]
    public void WithoutConfigurationRegistersDefaultHealthCheck()
    {
        ServiceCollection services = new();
        services.AddHealthChecks().AddQuartz();

        HealthCheckRegistration registration = GetQuartzRegistration(services, "quartz-scheduler");
        registration.Tags.Should().BeEmpty();
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Test]
    public void WithConfigurationAppliesNameTagsAndFailureStatus()
    {
        ServiceCollection services = new();
        services.AddHealthChecks().AddQuartz(options =>
        {
            options.Name = "quartz";
            options.Tags.AddRange(["ready", "live"]);
            options.FailureStatus = HealthStatus.Degraded;
        });

        HealthCheckRegistration registration = GetQuartzRegistration(services, "quartz");
        registration.Tags.Should().BeEquivalentTo("ready", "live");
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    [Test]
    public void ANamedSchedulerRegistersACheckOfItsOwn()
    {
        ServiceCollection services = new();
        services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());

        HealthCheckRegistration registration = GetQuartzRegistration(services, "quartz-scheduler-reporting");
        registration.Tags.Should().BeEmpty();

        // The check has to reach that scheduler's factory, which is registered under its name as the
        // service key rather than unkeyed.
        using ServiceProvider provider = services.BuildServiceProvider();
        registration.Factory(provider).Should().NotBeNull();
    }

    /// <summary>
    /// The check composes with an application's other health checks rather than requiring a call of its
    /// own.
    /// </summary>
    [Test]
    public void TheCheckIsAddedThroughTheHealthChecksBuilder()
    {
        ServiceCollection services = new();
        services.AddHealthChecks()
            .AddCheck("other", () => HealthCheckResult.Healthy())
            .AddQuartz()
            .AddQuartz("reporting", options => options.Tags.Add("ready"));

        HealthCheckServiceOptions options = Options(services);

        options.Registrations.Select(registration => registration.Name)
            .Should().BeEquivalentTo(["other", "quartz-scheduler", "quartz-scheduler-reporting"]);

        options.Registrations.Single(registration => registration.Name == "quartz-scheduler-reporting")
            .Tags.Should().BeEquivalentTo(["ready"]);
    }

    /// <summary>
    /// The options go through the options pipeline, so every source of them has its say.
    /// </summary>
    /// <remarks>
    /// The options object used to be constructed and read inside the registration call, which meant a
    /// <c>Configure&lt;QuartzHealthCheckOptions&gt;</c> — or a configuration section bound to the type —
    /// silently did nothing at all.
    /// </remarks>
    [Test]
    public void ConfigureAppliesToTheCheckWhicheverOrderItIsCalledIn()
    {
        ServiceCollection services = new();
        services.Configure<QuartzHealthCheckOptions>(options => options.Tags.Add("before"));
        services.AddHealthChecks().AddQuartz(options => options.FailureStatus = HealthStatus.Degraded);
        services.Configure<QuartzHealthCheckOptions>(options => options.Name = "renamed");

        HealthCheckRegistration registration = GetQuartzRegistration(services, "renamed");
        registration.Tags.Should().BeEquivalentTo(["before"]);
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    /// <summary>
    /// A named scheduler's check reads the options registered under that scheduler's name, like every
    /// other per-scheduler setting.
    /// </summary>
    [Test]
    public void ANamedSchedulersCheckReadsThatSchedulersOptions()
    {
        ServiceCollection services = new();
        services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());
        services.AddHealthChecks().AddQuartz();

        services.Configure<QuartzHealthCheckOptions>("reporting", options => options.Tags.Add("reporting-only"));

        HealthCheckServiceOptions options = Options(services);

        options.Registrations.Single(registration => registration.Name == "quartz-scheduler-reporting")
            .Tags.Should().BeEquivalentTo(["reporting-only"]);

        options.Registrations.Single(registration => registration.Name == "quartz-scheduler")
            .Tags.Should().BeEmpty("the default scheduler's check reads the unnamed options");
    }

    private static HealthCheckRegistration GetQuartzRegistration(IServiceCollection services, string name)
    {
        return Options(services).Registrations.Single(registration => registration.Name == name);
    }

    private static HealthCheckServiceOptions Options(IServiceCollection services)
    {
        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
    }
}
