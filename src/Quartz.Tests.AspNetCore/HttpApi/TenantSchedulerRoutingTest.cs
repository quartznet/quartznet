using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// How the HTTP API answers for a tenant, where "registered" and "running" come apart.
/// </summary>
/// <remarks>
/// The routes resolve through <see cref="ISchedulerRepository" />, which holds the schedulers something
/// has already created. That is what <c>multi-tenancy.md</c> says in its "the dashboard and the HTTP API
/// list the repository, not the registry" box, and nothing had gone through the wire to check it.
/// </remarks>
[NonParallelizable]
public sealed class TenantSchedulerRoutingTest
{
    private readonly List<WebApplicationFactory<Program>> factories = [];

    [TearDown]
    public async Task DisposeApplications()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    /// <summary>
    /// The window <c>AddQuartzHostedService()</c> closes and everything else leaves open: the container
    /// knows the tenant, nothing has built it, and the two views of "what schedulers are there" disagree
    /// on purpose. The API answering <c>404</c> here is not "unknown name" — it is "nothing has built it
    /// yet", and <see cref="ISchedulerRegistry" /> is the read that can tell the two apart.
    /// </summary>
    [Test]
    public async Task ATenantNothingHasBuiltIsMissingFromTheApiAndPresentInTheRegistry()
    {
        WebApplicationFactory<Program> application = CreateApplication("acme");

        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("schedulers/acme");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the route resolves through the repository, which holds schedulers something has created - and "
            + "answering it by building one would make an operator's dashboard start every tenant it lists");

        SchedulerHeaderDto[] listed = await ReadSchedulers(client);
        listed.Should().NotContain(x => x.Name == "acme",
            "the listing reads the same repository the lookup does, so a tenant absent from one is absent from both");

        List<SchedulerRegistration> registrations =
            await application.Services.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        SchedulerRegistration registration = registrations.Should().ContainSingle(x => x.Name == "acme",
            "the registry reads the registrations, which is where a tenant nobody has asked for still exists").Subject;
        registration.Status.Should().BeNull(
            "null is the answer this query exists to give - the registration is there, nothing was built from "
            + "it, and asking did not build it");
        registration.IsCreated.Should().BeFalse();
    }

    /// <summary>
    /// Builds the test application with one named scheduler registered beside the default one, and
    /// records it for disposal. Per test rather than per fixture, because whether the tenant has been
    /// created is exactly what these tests differ on.
    /// </summary>
    private WebApplicationFactory<Program> CreateApplication(string tenantName)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddQuartz(tenantName, q => q.UseInMemoryStore())));
        factories.Add(application);

        return application;
    }

    private static async Task<SchedulerHeaderDto[]> ReadSchedulers(HttpClient client)
    {
        // This endpoint is not one HttpScheduler calls, so the reader is built here - off the same wire
        // contract the endpoint writes with.
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        using HttpResponseMessage response = await client.GetAsync("schedulers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SchedulerHeaderDto[]>(body, serializerOptions)!;
    }
}
