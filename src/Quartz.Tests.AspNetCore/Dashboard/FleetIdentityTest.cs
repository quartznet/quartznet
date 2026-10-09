using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Two processes whose schedulers share a name, fronted by two HTTP targets: each is its own row, each is
/// reached by its own key, and the bare name reaches neither.
/// </summary>
/// <remarks>
/// Every worker's default scheduler is <c>QuartzScheduler</c>, so this is what a fleet of two workers looks
/// like to a dashboard. Before 4.5 the second registration was refused outright.
/// </remarks>
public sealed class FleetIdentityTest
{
    private WebApplicationFactory<Program> hostA = null!;
    private WebApplicationFactory<Program> hostB = null!;
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IQuartzApiClient client = null!;
    private string schedulerName = null!;

    [SetUp]
    public async Task SetUp()
    {
        TestContentRoot.Apply();
        hostA = Host("node-a");
        hostB = Host("node-b");

        // The test host maps the API but runs no hosted service, so nothing has built its scheduler yet.
        schedulerName = (await hostA.Services.GetRequiredService<ISchedulerFactory>().GetScheduler()).SchedulerName;
        (await hostB.Services.GetRequiredService<ISchedulerFactory>().GetScheduler()).SchedulerName.Should().Be(schedulerName,
            "the case is about two processes whose schedulers share a name");

        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = schedulerName;
            options.Target = "A";
            options.CreateHttpClient = _ => hostA.CreateClient();
        });
        services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = schedulerName;
            options.Target = "B";
            options.CreateHttpClient = _ => hostB.CreateClient();
        });

        provider = services.BuildServiceProvider();

        // Resolving them is what binds them into this container's repository, which the hosted service
        // would do in an application that had one.
        provider.GetRequiredKeyedService<IScheduler>("A");
        provider.GetRequiredKeyedService<IScheduler>("B");

        scope = provider.CreateScope();
        client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();
    }

    [TearDown]
    public async Task TearDown()
    {
        scope?.Dispose();
        if (provider is not null)
        {
            await provider.DisposeAsync();
        }

        await hostA.DisposeAsync();
        await hostB.DisposeAsync();
    }

    [Test]
    public async Task EachTargetIsItsOwnRowUnderItsOwnKey()
    {
        List<SchedulerHeaderDto> schedulers = await client.GetSchedulers();

        schedulers.Select(x => x.Key).Should().Equal([$"A/{schedulerName}", $"B/{schedulerName}"],
            "two targets fronting schedulers of one name are two schedulers, told apart by the target");
        schedulers.Should().OnlyContain(x => x.Origin == SchedulerOrigin.Remote && x.IsCreated,
            "each row is a scheduler another process built, asked for its status over the wire");
        schedulers.Select(x => x.SchedulerInstanceId).Should().Equal(["node-a", "node-b"],
            "each row carries the node its own process answered as");
    }

    [Test]
    public async Task ATargetedKeyReachesThatTargetsProcess()
    {
        SchedulerDetailDto a = await client.GetScheduler($"A/{schedulerName}");
        SchedulerDetailDto b = await client.GetScheduler($"b/{schedulerName}");

        a.SchedulerInstanceId.Should().Be("node-a");
        b.SchedulerInstanceId.Should().Be("node-b", "the target is compared ignoring case, the way names are");
    }

    [Test]
    public async Task TheBareNameReachesNeitherAndTheRefusalNamesBoth()
    {
        Func<Task> act = () => client.GetScheduler(schedulerName).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "a bare key is reserved for a scheduler reached through no target, and answering with an arbitrary "
            + "target would act on a process the caller did not name"))
            .Which.Message.Should().Contain($"'A/{schedulerName}'").And.Contain($"'B/{schedulerName}'");
    }

    [Test]
    public void TheTargetsHistoryAndEventsAreFoundByKey()
    {
        IExecutionHistoryStore shared = provider.GetRequiredService<IExecutionHistoryStore>();

        IExecutionHistoryStore? history = ExecutionHistoryLookup.Find(provider, null, shared, $"A/{schedulerName}", out string name, out string? refusal);
        history.Should().BeOfType<HttpExecutionHistoryStore>("the target keeps its history where it runs");
        history.Should().BeSameAs(provider.GetRequiredKeyedService<IExecutionHistoryStore>("A"),
            "and the reader of it is keyed by the target");
        name.Should().Be(schedulerName, "the store is asked for the scheduler's own name, not the key");
        refusal.Should().BeNull();

        SchedulerEventSources.For(provider, $"B/{schedulerName}").Should()
            .BeSameAs(provider.GetRequiredKeyedService<ISchedulerEventSource>("B"));

        provider.GetRequiredService<SchedulerTargets>().Snapshot().Select(x => x.Name).Should().Equal(["A", "B"]);
    }

    private static WebApplicationFactory<Program> Host(string instanceId)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<QuartzSchedulerOptions>(options => options.InstanceId = instanceId)));
    }
}
