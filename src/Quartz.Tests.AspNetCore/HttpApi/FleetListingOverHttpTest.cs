using System.Net;
using System.Text.Json;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What the HTTP API says about a fleet: the listing carries every target and a cluster's members, and a
/// bare name that only targets hold is not found, naming them.
/// </summary>
/// <remarks>
/// The API addresses no targeted scheduler in 4.5 — <c>{name}</c> is a bare name, and a bare name is the
/// scheduler reached through no target. Answering with an arbitrary target would act on a process the
/// caller did not name.
/// </remarks>
public sealed class FleetListingOverHttpTest
{
    private WebApplicationFactory<Program> host = null!;

    [SetUp]
    public void SetUp()
    {
        TestContentRoot.Apply();
        host = new WebApplicationFactory<Program>();

        ISchedulerRepository repository = host.Services.GetRequiredService<ISchedulerRepository>();
        repository.Bind(new FakeProxyScheduler(Node("fleet", "node-a"), "A"));
        repository.Bind(new FakeProxyScheduler(Node("fleet", "node-b"), "B"));
        repository.Bind(new FakeProxyScheduler(Node("fleet", "node-c"), "C"));
        repository.Bind(new FakeProxyScheduler(Node("fleet", "node-d"), "D"));

        host.Services.GetRequiredService<SchedulerTargets>().Add(new SchedulerTarget
        {
            Name = "C+D",
            Origin = SchedulerOrigin.Cluster,
            Members = ["C", "D"]
        });
        repository.Bind(new FakeProxyScheduler(Node("fleet", "C+D"), "C+D", SchedulerOrigin.Cluster));
    }

    [TearDown]
    public async Task TearDown()
    {
        await host.DisposeAsync();
    }

    [Test]
    public async Task TheListingCarriesEveryTargetAndAClustersMembers()
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("schedulers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
        SchedulerHeaderDto[] schedulers = JsonSerializer.Deserialize<SchedulerHeaderDto[]>(
            await response.Content.ReadAsStringAsync(), serializerOptions)!;

        List<SchedulerHeaderDto> fleet = schedulers.Where(x => x.Name == "fleet").ToList();
        fleet.Select(x => x.Target).Should().Equal(["A", "B", "C+D"],
            "one row per target, with the cluster's members hidden behind the cluster's row");
        fleet.Should().ContainSingle(x => x.Origin == SchedulerOrigin.Cluster)
            .Which.Members.Should().Equal(["C", "D"]);
        fleet.Where(x => x.Origin == SchedulerOrigin.Remote).Should().OnlyContain(x => x.Members.Length == 0 && x.LastSeenUtc == null);
    }

    [Test]
    public async Task ABareNameHeldOnlyByTargetsIsNotFoundNamingThem()
    {
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("schedulers/fleet");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the API does not address a targeted scheduler, and answering with one of them would act on a process the caller did not name");
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Unknown scheduler fleet").And.Contain("A").And.Contain("B").And.Contain("C+D",
            "the targets that hold the name are what the caller needs next");
    }

    private static IScheduler Node(string name, string instanceId)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.GetStatus(A<CancellationToken>._)).Returns(SchedulerStatus.Running);
        A.CallTo(() => scheduler.GetSchedulerInstanceId(A<CancellationToken>._)).Returns(instanceId);
        return scheduler;
    }
}
