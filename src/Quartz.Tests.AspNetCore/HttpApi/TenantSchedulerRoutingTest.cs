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
/// has already created and indexes them ignoring case: a tenant nothing has built answers <c>404</c>,
/// and one asked for under another casing resolves. The <em>listing</em> is the one read that does not
/// go through the repository — it answers from <see cref="ISchedulerRegistry" />, so the tenant that is
/// missing from the routes is present there, which is how a caller tells "not built yet" from "no such
/// tenant".
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
    /// knows the tenant and nothing has built it. Its routes answer <c>404</c> — not "unknown name" but
    /// "nothing has built it yet" — while the listing carries it with a null status, which is the
    /// difference <see cref="ISchedulerRegistry" /> exists to report and the API now passes on.
    /// </summary>
    [Test]
    public async Task ATenantNothingHasBuiltIsListedWithoutAStatusAndItsRoutesAnswerNotFound()
    {
        WebApplicationFactory<Program> application = CreateApplication("acme");

        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("schedulers/acme");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the route resolves through the repository, which holds schedulers something has created - and "
            + "answering it by building one would make an operator's dashboard start every tenant it lists");

        SchedulerHeaderDto[] listed = await ReadSchedulers(client);
        SchedulerHeaderDto tenant = listed.Should().ContainSingle(x => x.Name == "acme",
            "the listing reads the registry, so a tenant nothing has built is listed rather than being "
            + "indistinguishable from a name that does not exist").Subject;
        tenant.Status.Should().BeNull("null is what says the registration is there and nothing has built it");
        tenant.SchedulerInstanceId.Should().BeNull("there is no scheduler to have an instance id");
        tenant.Origin.Should().Be(SchedulerOrigin.Container);

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
    /// A container-built scheduler's context reads back over the API, holding what the application put
    /// there and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is specific to a tenant; it is that a real container-built scheduler had never been
    /// driven through this route, and doing so answered <c>500</c> every time (#3408). Two things made
    /// it: <c>SchedulerContentInitializer</c> wrote the <see cref="IServiceProvider" /> into
    /// <c>scheduler.Context["Quartz.ServiceProvider"]</c> so that plugins could reach the container, and
    /// <c>SchedulerContextDto.Create</c> threw on any value that was not a string — so every scheduler
    /// from <c>AddQuartz</c> carried exactly one entry the endpoint refused, while
    /// <c>SchedulerEndpointsTest.GetSchedulerContextShouldWork</c> passed against a fake whose context
    /// the test had filled with strings.
    /// </para>
    /// <para>
    /// Both halves are asserted: the container is absent from the context, and a value the application
    /// put there that is not a string is rendered rather than refused.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheContextOfAContainerBuiltSchedulerCanBeRead()
    {
        WebApplicationFactory<Program> application = CreateApplication("Acme");

        IScheduler scheduler = await application.Services
            .GetRequiredKeyedService<ISchedulerFactory>("Acme")
            .GetScheduler();

        try
        {
            scheduler.Context["tenant"] = "Acme";
            scheduler.Context["retries"] = 3;

            using HttpClient client = application.CreateClient();

            using HttpResponseMessage response = await client.GetAsync("schedulers/Acme/context");
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "a scheduler's context is readable over the API, and an application may put any object in it");

            JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

            string body = await response.Content.ReadAsStringAsync();
            SchedulerContextDto dto = JsonSerializer.Deserialize<SchedulerContextDto>(body, serializerOptions)!;

            dto.Context.Should().Equal(
                new Dictionary<string, string?>
                {
                    ["tenant"] = "Acme",
                    ["retries"] = "3"
                },
                "the context carries what the application put in it - the container is not application data, "
                + "and a value that is not a string arrives as its invariant text");
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
