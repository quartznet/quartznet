
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;


namespace Quartz.Tests.AspNetCore.HealthChecks;

public class QuartzHealthCheckRegistrationTests
{
    [Test]
    public void WithoutConfigurationRegistersDefaultHealthCheck()
    {
        ServiceCollection services = new();
        services.AddQuartzHealthChecks();

        HealthCheckRegistration registration = GetQuartzRegistration(services, "quartz-scheduler");
        registration.Tags.Should().BeEmpty();
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Test]
    public void WithConfigurationAppliesNameTagsAndFailureStatus()
    {
        ServiceCollection services = new();
        services.AddQuartzHealthChecks(options =>
        {
            options.Name = "quartz";
            options.Tags = ["ready", "live"];
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

    private static HealthCheckRegistration GetQuartzRegistration(IServiceCollection services, string name)
    {
        HealthCheckServiceOptions options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        return options.Registrations.Single(registration => registration.Name == name);
    }
}
