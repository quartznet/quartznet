using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;

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
        services.AddQuartz("first");
        services.AddQuartzHttpApi();

        QuartzConverterCount(services).Should().BeGreaterThan(0,
            "the API cannot read a trigger or a calendar off the wire without Quartz's own converters");
    }

    [Test]
    public void SecondRegistrationDoesNotAddTheSameConvertersAgain()
    {
        ServiceCollection one = new();
        one.AddQuartz("first");
        one.AddQuartzHttpApi();

        ServiceCollection two = new();
        two.AddQuartz("first");
        two.AddQuartz("second");
        two.AddQuartzHttpApi();
        two.AddQuartzHttpApi();

        QuartzConverterCount(two).Should().Be(QuartzConverterCount(one),
            "the JSON options belong to the container rather than to one scheduler, so serving a second scheduler over HTTP must not stack the same converters onto them twice");
    }

    [Test]
    public void RegistrationAsksTheGeneratedContractBeforeReflection()
    {
        ServiceCollection services = new();
        services.AddQuartz("first");
        services.AddQuartzHttpApi();

        IList<IJsonTypeInfoResolver> resolvers = SerializerOptions(services).TypeInfoResolverChain;

        resolvers[0].Should().BeOfType<HttpApiJsonContext>(
            "a contract body must be answered from generated metadata rather than reflected over");
        resolvers[^1].Should().BeOfType<DefaultJsonTypeInfoResolver>(
            "the options are the whole application's, so the host's own bodies must keep resolving the way they did");
    }

    [Test]
    public void SecondRegistrationDoesNotAddTheSameResolverAgain()
    {
        ServiceCollection services = new();
        services.AddQuartz("first");
        services.AddQuartz("second");
        services.AddQuartzHttpApi();
        services.AddQuartzHttpApi();

        SerializerOptions(services).TypeInfoResolverChain.Count(resolver => resolver is HttpApiJsonContext).Should().Be(1,
            "serving a second scheduler over HTTP must not stack the contract onto the container's options twice, any more than it stacks the converters");
    }

    private static int QuartzConverterCount(IServiceCollection services)
    {
        return SerializerOptions(services).Converters.Count(converter => converter.GetType().Assembly == typeof(IScheduler).Assembly);
    }

    private static JsonSerializerOptions SerializerOptions(IServiceCollection services)
    {
        using ServiceProvider provider = services.BuildServiceProvider();
        JsonOptions options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        return options.SerializerOptions;
    }
}
