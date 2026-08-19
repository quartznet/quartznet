using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The HTTP API's converters go onto the container's JSON options, which every scheduler in it shares.
/// </summary>
public class QuartzHttpApiJsonOptionsTest
{
    [Test]
    public void RegistrationTeachesTheContainerJsonOptionsAboutQuartzTypes()
    {
        ServiceCollection services = new();
        services.AddQuartz("first", quartz => quartz.AddQuartzHttpApi());

        QuartzConverterCount(services).Should().BeGreaterThan(0,
            "the API cannot read a trigger or a calendar off the wire without Quartz's own converters");
    }

    [Test]
    public void SecondRegistrationDoesNotAddTheSameConvertersAgain()
    {
        ServiceCollection one = new();
        one.AddQuartz("first", quartz => quartz.AddQuartzHttpApi());

        ServiceCollection two = new();
        two.AddQuartz("first", quartz => quartz.AddQuartzHttpApi());
        two.AddQuartz("second", quartz => quartz.AddQuartzHttpApi());

        QuartzConverterCount(two).Should().Be(QuartzConverterCount(one),
            "the JSON options belong to the container rather than to one scheduler, so serving a second scheduler over HTTP must not stack the same converters onto them twice");
    }

    private static int QuartzConverterCount(IServiceCollection services)
    {
        using ServiceProvider provider = services.BuildServiceProvider();
        JsonOptions options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        return options.SerializerOptions.Converters.Count(converter => converter.GetType().Assembly == typeof(IScheduler).Assembly);
    }
}
