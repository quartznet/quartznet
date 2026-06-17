using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore;

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
            options.Tags.Add("ready");
            options.Tags.Add("live");
            options.FailureStatus = HealthStatus.Degraded;
        });

        HealthCheckRegistration registration = GetQuartzRegistration(services, "quartz");
        registration.Tags.Should().BeEquivalentTo("ready", "live");
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    private static HealthCheckRegistration GetQuartzRegistration(IServiceCollection services, string name)
    {
        HealthCheckServiceOptions options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        return options.Registrations.Single(registration => registration.Name == name);
    }
}
