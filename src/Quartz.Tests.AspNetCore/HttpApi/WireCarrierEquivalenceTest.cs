using System.Net;
using System.Text;
using System.Text.Json;

using FakeItEasy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Quartz.AspNetCore;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Holds the HTTP API to the operation catalogue it is a carrier of: the endpoints it maps are the
/// catalogue's routes, and the catalogue's refusals are answered as the endpoints' own.
/// </summary>
public sealed class WireCarrierEquivalenceTest
{
    private WebApplicationFactory<Program> host = null!;
    private HttpClient httpClient = null!;

    [SetUp]
    public void SetUp()
    {
        TestContentRoot.Apply();

        host = new WebApplicationFactory<Program>();

        ISchedulerRepository repository = host.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        repository.Bind(fake);

        httpClient = host.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        httpClient.Dispose();
        await host.DisposeAsync();
    }

    /// <summary>
    /// The server maps exactly the catalogue's routes, each at its template, with its method and under its
    /// name.
    /// </summary>
    /// <remarks>
    /// Under an API path of its own, so the template is seen to be relative to wherever the API is mapped.
    /// The name is the endpoint's OpenAPI operation id and the word the mutation audit logs, so it is as
    /// much the contract as the path is.
    /// </remarks>
    [Test]
    public async Task EveryRouteOfTheCatalogueIsMappedAsItSaysAndNothingElseIs()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartz();
        builder.Services.AddQuartzHttpApi();
        await using WebApplication app = builder.Build();
        app.MapQuartzHttpApi("/quartz-api").AllowAnonymous();

        List<string> mapped = ((IEndpointRouteBuilder) app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<QuartzEndpointMarker>() is not null)
            .Select(endpoint => Describe(
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []),
                endpoint.RoutePattern.RawText))
            .ToList();

        List<string> catalogued = SchedulerRoutes.All
            .Select(route => Describe(route.Name, route.Method, "/quartz-api/" + route.Template))
            .ToList();

        mapped.Should().BeEquivalentTo(catalogued,
            "every endpoint the API maps is a route of the catalogue, at the catalogue's template, with its method and under its name");

        static string Describe(string? name, string methods, string? pattern) => $"{name}: {methods} {pattern}";
    }

    /// <summary>
    /// A listing the catalogue refuses is answered exactly as a body the endpoint refuses: a <c>400</c>
    /// with the same problem-details members, under the exception name the wire has always used.
    /// </summary>
    /// <remarks>
    /// The catalogue cannot raise ASP.NET Core's <c>BadHttpRequestException</c> and raises its own; a client
    /// matching on <c>Quartz-ExceptionType</c> must not be able to tell which layer said no.
    /// </remarks>
    [Test]
    public async Task TheCatalogueRefusesAMalformedListingAsTheEndpointRefusesAMalformedBody()
    {
        using HttpResponseMessage fromCatalogue = await httpClient.GetAsync($"schedulers/{TestData.SchedulerName}/jobs?take=not-a-number");
        using StringContent invalidJob = new("""{"replace":true}""", Encoding.UTF8, "application/json");
        using HttpResponseMessage fromEndpoint = await httpClient.PostAsync($"schedulers/{TestData.SchedulerName}/jobs", invalidJob);

        using JsonDocument catalogueBody = JsonDocument.Parse(await fromCatalogue.Content.ReadAsStringAsync());
        using JsonDocument endpointBody = JsonDocument.Parse(await fromEndpoint.Content.ReadAsStringAsync());

        fromCatalogue.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        fromEndpoint.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        Members(catalogueBody).Should().Equal(Members(endpointBody), "the two refusals are one shape");
        catalogueBody.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString()
            .Should().Be("BadHttpRequestException", "that is what the wire has always named a request the API refused");
        catalogueBody.RootElement.GetProperty("detail").GetString()
            .Should().Contain("take must be a number", "the refusal says what to fix");

        static List<string> Members(JsonDocument document) => document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
    }
}
