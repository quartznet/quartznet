using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HttpApi;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

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
        resolvers[^2].Should().BeOfType<DefaultJsonTypeInfoResolver>(
            "the options are the whole application's, so the host's own bodies must keep resolving the way they did");
        resolvers[^1].Should().BeOfType<HttpApiProblemDetailsJsonContext>(
            "the problem-details metadata is for a host with no reflection, so it goes behind reflection, where AddProblemDetails() puts ASP.NET Core's own");
    }

    [Test]
    public void SecondRegistrationDoesNotAddTheProblemDetailsResolverAgain()
    {
        ServiceCollection services = new();
        services.AddQuartz("first");
        services.AddQuartz("second");
        services.AddQuartzHttpApi();
        services.AddQuartzHttpApi();

        SerializerOptions(services).TypeInfoResolverChain.Count(resolver => resolver is HttpApiProblemDetailsJsonContext).Should().Be(1,
            "serving a second scheduler over HTTP must not stack the problem-details metadata onto the container's options twice");
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

    [Test]
    public void ProblemDetailsAreWrittenByOptionsThatHaveNoReflection()
    {
        // What a trimmed host's options hold: its own generated metadata and no reflection resolver. A chain
        // that is not empty is left without reflection by Quartz too, as a trimmed publish leaves it.
        JsonOptions options = new();
        options.SerializerOptions.TypeInfoResolverChain.Clear();
        options.SerializerOptions.TypeInfoResolverChain.Add(new ApplicationOwnMetadata());

        new QuartzJsonOptionsSetup(new SystemTextJsonSerializerRegistry()).Configure(options);

        ProblemDetails problem = new() { Status = 404, Detail = "no such job" };
        problem.Extensions[HttpApiConstants.ProblemDetailsExceptionType] = "NotFoundException";

        string json = JsonSerializer.Serialize(problem, options.SerializerOptions);

        json.Should().Contain("\"detail\":\"no such job\"",
            "Results.Problem writes through these options, and without metadata for ProblemDetails it throws rather than answering");
        json.Should().Contain($"\"{HttpApiConstants.ProblemDetailsExceptionType}\":\"NotFoundException\"",
            "the members Quartz adds to Extensions are strings, and they are written as their runtime type");
    }

    /// <summary>
    /// An application's own source-generated metadata, which knows nothing of <see cref="ProblemDetails" />.
    /// </summary>
    private sealed class ApplicationOwnMetadata : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
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
