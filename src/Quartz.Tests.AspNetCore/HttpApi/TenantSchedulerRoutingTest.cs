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
/// How the HTTP API answers for a tenant, on the two questions where "registered" and "running" come
/// apart: a tenant nothing has built yet, and a tenant asked for under a different casing than it was
/// registered with.
/// </summary>
/// <remarks>
/// Both routes resolve through <see cref="ISchedulerRepository" />, which holds the schedulers something
/// has already created and indexes them ignoring case. That is two separate statements in
/// <c>multi-tenancy.md</c> — the "the dashboard and the HTTP API list the repository, not the registry"
/// box, and the scheduler name being compared case-insensitively there while the container compares
/// service keys ordinally — and neither had gone through the wire.
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
    /// The same name spelled two ways. The repository indexes ignoring case, so the route finds the
    /// scheduler; the container compares service keys ordinally, which
    /// <c>SchedulerNameComparisonTest</c> covers on the other side of the same registration.
    /// </summary>
    [Test]
    public async Task ATenantRegisteredAsAcmeIsReachableAtTheAcmeRoute()
    {
        WebApplicationFactory<Program> application = CreateApplication("Acme");

        // Created explicitly: until something builds it, the route would answer 404 for either spelling,
        // and the casing would not be what the test was measuring.
        IScheduler scheduler = await application.Services
            .GetRequiredKeyedService<ISchedulerFactory>("Acme")
            .GetScheduler();

        try
        {
            using HttpClient client = application.CreateClient();

            using HttpResponseMessage details = await client.GetAsync("schedulers/acme");
            details.StatusCode.Should().Be(HttpStatusCode.OK,
                "every ISchedulerRepository lookup compares names ignoring case, and the API route is one");

            using HttpResponseMessage jobs = await client.GetAsync("schedulers/acme/jobs");
            jobs.StatusCode.Should().Be(HttpStatusCode.OK,
                "the casing is forgiven by the lookup rather than by one endpoint group, so a route under a "
                + "different one resolves the same scheduler the same way");

            SchedulerHeaderDto[] listed = await ReadSchedulers(client);
            listed.Should().ContainSingle(x => x.Name == "Acme",
                "a scheduler is listed under the name it was registered with, whatever spelling the caller used "
                + "to reach it");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A defect this fixture walked into, kept as the failing test rather than fixed here: the
    /// scheduler-context endpoint answers <c>500</c> for every scheduler a container built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SchedulerContentInitializer.Initialize</c> puts the <see cref="IServiceProvider" /> into
    /// <c>scheduler.Context["Quartz.ServiceProvider"]</c> so that plugins can reach the container, and
    /// <c>SchedulerContextDto.Create</c> throws <see cref="NotSupportedException" /> when any context value
    /// is not a string. Every scheduler built by <c>AddQuartz</c> — and by
    /// <c>QuartzSchedulerBuilder</c>, which builds a container of its own — therefore has exactly one
    /// entry the endpoint refuses, so <c>GET …/schedulers/{name}/context</c> is unusable in every real
    /// deployment while <c>SchedulerEndpointsTest.GetSchedulerContextShouldWork</c> passes: that fixture's
    /// scheduler is a fake whose context the test filled with strings.
    /// </para>
    /// <para>
    /// Nothing about it is specific to a tenant; it is only that a real container-built scheduler had
    /// never been driven through this route. Which way to fix it is a product decision — skip
    /// non-serializable entries, report them as their type name, or move the service provider off the
    /// context — so this test is <c>[Explicit]</c> and states the failure rather than choosing.
    /// </para>
    /// </remarks>
    [Test]
    [Explicit("Fails: GET /schedulers/{name}/context is 500 for any container-built scheduler. See the remarks.")]
    public async Task TheContextOfAContainerBuiltSchedulerCanBeRead()
    {
        WebApplicationFactory<Program> application = CreateApplication("Acme");

        IScheduler scheduler = await application.Services
            .GetRequiredKeyedService<ISchedulerFactory>("Acme")
            .GetScheduler();

        try
        {
            using HttpClient client = application.CreateClient();

            using HttpResponseMessage context = await client.GetAsync("schedulers/Acme/context");
            context.StatusCode.Should().Be(HttpStatusCode.OK,
                "a scheduler's context is readable over the API, and every container-built scheduler carries "
                + "the container in it - so an endpoint that refuses a non-string value refuses them all");
        }
        finally
        {
            await scheduler.Shutdown();
        }
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
