
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore;

namespace Quartz.Tests.AspNetCore.HealthChecks;

public class QuartzHealthCheckRegistrationTests
{
    [Test]
    public void AddQuartzServer_WithTags_RegistersHealthCheckWithTags()
    {
        ServiceCollection services = new();
        services.AddQuartzServer(configure: null, healthCheckTags: new[] { "ready", "live" });

        HealthCheckRegistration registration = GetQuartzRegistration(services);
        registration.Tags.Should().BeEquivalentTo("ready", "live");
    }

    [Test]
    public void AddQuartzServer_WithoutTags_RegistersHealthCheckWithoutTags()
    {
        ServiceCollection services = new();
        services.AddQuartzServer();

        HealthCheckRegistration registration = GetQuartzRegistration(services);
        registration.Tags.Should().BeEmpty();
    }

    private static HealthCheckRegistration GetQuartzRegistration(IServiceCollection services)
    {
        HealthCheckServiceOptions options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        return options.Registrations.Single(registration => registration.Name == "quartz-scheduler");
    }
}
