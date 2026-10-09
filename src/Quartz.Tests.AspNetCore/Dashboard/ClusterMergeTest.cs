using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Configuration;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The cluster rule: which targets fronting schedulers of one name are merged into one row, which are
/// not, where a call through the merged row lands, and that membership survives an outage.
/// </summary>
/// <remarks>
/// Fakes rather than hosts: the rule is about what the targets answer, and three fakes say it in a line
/// each. <c>FleetClusterIntegrationTest</c> runs the same rule over two real nodes on one database.
/// </remarks>
public sealed class ClusterMergeTest
{
    private const string Name = "QuartzScheduler";

    private ServiceProvider provider = null!;
    private FakeTimeProvider clock = null!;
    private ISchedulerRepository repository = null!;
    private FleetMonitor monitor = null!;
    private readonly Dictionary<string, IScheduler> inner = new(StringComparer.OrdinalIgnoreCase);
    private RecordingLoggerProvider logs = null!;

    [SetUp]
    public void SetUp()
    {
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        logs = new RecordingLoggerProvider();
        inner.Clear();

        ServiceCollection services = new();
        // Recorded down to Debug, which is where a member's failure to answer is logged.
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        services.AddSingleton<TimeProvider>(clock);

        // What AddQuartz or AddQuartzHttpClient would register: the repository the proxies are bound
        // into and the registry that lists them. The proxies here are bound by hand.
        services.AddQuartzSharedServices();
        services.AddQuartzDashboard(options => options.ClusterDetectionInterval = TimeSpan.FromMinutes(1));

        provider = services.BuildServiceProvider();
        repository = provider.GetRequiredService<ISchedulerRepository>();
        // AddQuartzDashboard registers the monitor as a hosted service.
        monitor = provider.GetServices<IHostedService>().OfType<FleetMonitor>().Single();
    }

    [TearDown]
    public async Task TearDown()
    {
        // The container owns the monitor and disposes it; disposed here too so the analyzer can see it.
        monitor.Dispose();
        await provider.DisposeAsync();
        logs.Dispose();
    }

    [Test]
    public async Task TargetsOnOneClusteredStoreThatSeeOneAnotherAreOneRow()
    {
        Bind("a", "n1", clustered: true, "n1", "n2", "n3");
        Bind("b", "n2", clustered: true, "n1", "n2", "n3");
        Bind("c", "n3", clustered: true, "n1", "n2", "n3");

        await monitor.StartedAsync(CancellationToken.None);

        List<SchedulerRegistration> listed = await Listing();
        SchedulerRegistration cluster = listed.Should().ContainSingle(
            "the three targets front the nodes of one cluster, and three rows would show every node three times").Subject;

        cluster.Key.Should().Be("a+b+c/" + Name);
        cluster.Origin.Should().Be(SchedulerOrigin.Cluster);
        cluster.Members.Should().Equal(["a", "b", "c"]);
        cluster.Status.Should().Be(SchedulerStatus.Running);
        cluster.SchedulerInstanceId.Should().BeNull("a cluster is every node at once, not a node");

        repository.Lookup(Name, "a").Should().NotBeNull("a member stays resolvable by its own key");
    }

    [Test]
    public async Task TargetsWhoseStoresAreNotClusteredStayApart()
    {
        Bind("a", "NON_CLUSTERED", clustered: false, "NON_CLUSTERED");
        Bind("b", "NON_CLUSTERED", clustered: false, "NON_CLUSTERED");
        Bind("c", "NON_CLUSTERED", clustered: false, "NON_CLUSTERED");

        await monitor.StartedAsync(CancellationToken.None);

        (await Listing()).Select(x => x.Key).Should().Equal(["a/" + Name, "b/" + Name, "c/" + Name],
            "every non-clustered scheduler reports the same NON_CLUSTERED node, and the gate is what keeps them apart");
    }

    [Test]
    public async Task OnlyTargetsThatShareANodeAreMergedTransitively()
    {
        Bind("a", "n1", clustered: true, "n1", "n2");
        Bind("b", "n2", clustered: true, "n2", "n3");
        Bind("c", "n9", clustered: true, "n9");

        await monitor.StartedAsync(CancellationToken.None);

        (await Listing()).Select(x => x.Key).Should().Equal(["a+b/" + Name, "c/" + Name],
            "a and b see n2 between them; c sees a cluster of its own");
    }

    /// <summary>
    /// One member failing in a way nothing anticipated is that member's failure alone: the rest of its
    /// group and every other group are settled, and the failure is logged.
    /// </summary>
    [Test]
    public async Task AMemberThrowingAnUnexpectedExceptionLeavesTheOtherGroupsSettledAndIsLogged()
    {
        Bind("a", "n1", clustered: true, "n1", "n2", "n3");
        Bind("b", "n2", clustered: true, "n1", "n2", "n3");
        Bind("c", "n3", clustered: true, "n1", "n2", "n3");
        A.CallTo(() => inner["c"].GetMetadata(A<CancellationToken>._)).Throws(new InvalidOperationException("the client was disposed"));

        Bind("Other", "d", "o1", clustered: true, "o1", "o2");
        Bind("Other", "e", "o2", clustered: true, "o1", "o2");

        await monitor.StartedAsync(CancellationToken.None);

        (await Listing()).Select(x => x.Key).Should().Equal(["a+b/" + Name, "c/" + Name],
            "the member that threw is left as it was, and the two that answered form the cluster");
        (await Listing("Other")).Select(x => x.Key).Should().Equal(["d+e/Other"],
            "the other group is settled whatever happened in this one");

        logs.WithEventId(9110).Should().ContainSingle()
            .Which.Properties.Should().Contain("Target", "c");
    }

    [Test]
    public async Task AMemberThatCannotSayWhichNodesItSeesStaysStandalone()
    {
        Bind("a", "n1", clustered: true, "n1", "n2", "n3");
        Bind("b", "n2", clustered: true, "n1", "n2", "n3");
        Bind("c", "n3", clustered: true, "n1", "n2", "n3");
        A.CallTo(() => inner["c"].QueryClusterNodes(A<CancellationToken>._)).Throws(new SchedulerException("state table unavailable"));

        await monitor.StartedAsync(CancellationToken.None);

        (await Listing()).Select(x => x.Key).Should().Equal(["a+b/" + Name, "c/" + Name],
            "a target that has never answered is never merged");
    }

    [Test]
    public async Task InterruptingAFiringReachesTheMemberWhoseNodeOwnsIt()
    {
        Bind("a", "n1", clustered: true, "n1", "n2", "n3");
        Bind("b", "n2", clustered: true, "n1", "n2", "n3");
        Bind("c", "n3", clustered: true, "n1", "n2", "n3");
        A.CallTo(() => inner["a"].QueryFireInstances(A<FireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstance>([Firing("f", "n2")], HasMore: false));
        A.CallTo(() => inner["b"].InterruptFireInstance("f", A<CancellationToken>._)).Returns(true);

        await monitor.StartedAsync(CancellationToken.None);

        using IServiceScope scope = provider.CreateScope();
        IQuartzApiClient client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();

        (await client.InterruptFireInstance("a+b+c/" + Name, "f")).Should().BeTrue();

        // The store says the firing belongs to n2, which b answered as.
        A.CallTo(() => inner["b"].InterruptFireInstance("f", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => inner["a"].InterruptFireInstance(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => inner["c"].InterruptFireInstance(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task TheLifecycleVerbsAreRefusedOnTheClusterAndStoreCallsLandOnThePreferredMember()
    {
        Bind("a", "n1", clustered: true, "n1", "n2");
        Bind("b", "n2", clustered: true, "n1", "n2");
        JobKey job = new("nightly", "reports");
        A.CallTo(() => inner["a"].PauseJob(job, A<CancellationToken>._)).Returns(true);

        await monitor.StartedAsync(CancellationToken.None);

        using IServiceScope scope = provider.CreateScope();
        IQuartzApiClient client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();

        Func<Task> standby = () => client.Standby("a+b/" + Name).AsTask();
        (await standby.Should().ThrowAsync<NotSupportedException>("a cluster is every node at once, and stand-by acts on one"))
            .Which.Message.Should().Contain("Cluster page");

        (await client.PauseJob("a+b/" + Name, new JobKeyDto(job.Group, job.Name))).Should().BeTrue();
        // a is the first member by target that answered, so it is the preferred one.
        A.CallTo(() => inner["a"].PauseJob(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => inner["b"].PauseJob(A<JobKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Membership is sticky: a member that stops answering stays in its cluster, and the row keeps its key.
    /// </summary>
    [Test]
    public async Task AMemberThatStopsAnsweringStaysInTheClusterAndThePreferredMemberMovesOffIt()
    {
        Bind("a", "n1", clustered: true, "n1", "n2", "n3");
        Bind("b", "n2", clustered: true, "n1", "n2", "n3");
        Bind("c", "n3", clustered: true, "n1", "n2", "n3");
        JobKey job = new("nightly", "reports");
        A.CallTo(() => inner["b"].PauseJob(job, A<CancellationToken>._)).Returns(true);

        await monitor.StartedAsync(CancellationToken.None);
        (await Listing()).Should().ContainSingle().Which.Key.Should().Be("a+b+c/" + Name);

        // a, the preferred member, goes down: every question it is asked fails at once.
        A.CallTo(() => inner["a"].GetMetadata(A<CancellationToken>._)).Throws(new HttpRequestException("connection refused"));
        A.CallTo(() => inner["a"].GetStatus(A<CancellationToken>._)).Throws(new HttpRequestException("connection refused"));
        A.CallTo(() => inner["a"].QueryClusterNodes(A<CancellationToken>._)).Throws(new HttpRequestException("connection refused"));
        A.CallTo(() => inner["a"].PauseJob(A<JobKey>._, A<CancellationToken>._)).Throws(new HttpRequestException("connection refused"));

        await monitor.RunRound();

        List<SchedulerRegistration> listed = await Listing();
        SchedulerRegistration cluster = listed.Should().ContainSingle("an outage is not a topology change").Subject;
        cluster.Key.Should().Be("a+b+c/" + Name, "the key changes on topology changes only, so a remembered selection survives a node going down");
        cluster.Members.Should().Equal(["a", "b", "c"]);
        cluster.Status.Should().Be(SchedulerStatus.Running, "the members that answered say so");

        using IServiceScope scope = provider.CreateScope();
        IQuartzApiClient client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();
        (await client.PauseJob("a+b+c/" + Name, new JobKeyDto(job.Group, job.Name))).Should().BeTrue();
        // The preferred member is re-elected each round among those that answered.
        A.CallTo(() => inner["b"].PauseJob(job, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A member is judged against what the members that answered this round see. With the other member
    /// down there is nothing to judge against, so a node restarted under a new instance id stays in its
    /// cluster rather than dissolving it.
    /// </summary>
    [Test]
    public async Task ARestartedNodeStaysInItsClusterWhileTheOtherMemberIsDown()
    {
        Bind("a", "n1", clustered: true, "n1", "n2");
        Bind("b", "n2", clustered: true, "n1", "n2");

        await monitor.StartedAsync(CancellationToken.None);
        (await Listing()).Should().ContainSingle().Which.Key.Should().Be("a+b/" + Name);

        // b goes down, and a restarts under a new instance id, seeing a store with only its own new row.
        A.CallTo(() => inner["b"].GetMetadata(A<CancellationToken>._)).Throws(new HttpRequestException("connection refused"));
        A.CallTo(() => inner["b"].QueryClusterNodes(A<CancellationToken>._)).Throws(new HttpRequestException("connection refused"));
        A.CallTo(() => inner["a"].GetMetadata(A<CancellationToken>._)).Returns(TestData.Metadata with
        {
            SchedulerName = Name,
            SchedulerInstanceId = "n1-restarted",
            IsProxy = true,
            JobStoreClustered = true,
            JobStorePersistent = true
        });
        A.CallTo(() => inner["a"].QueryClusterNodes(A<CancellationToken>._)).Returns(Nodes("n1-restarted"));

        await monitor.RunRound();

        SchedulerRegistration cluster = (await Listing()).Should().ContainSingle(
            "an outage beside a restart is not a topology change: nobody who answered contradicts the membership").Subject;
        cluster.Key.Should().Be("a+b/" + Name);
        cluster.Members.Should().Equal(["a", "b"]);
    }

    [Test]
    public async Task AMemberThatAnswersWithNodesTheClusterDoesNotHaveLeavesIt()
    {
        Bind("a", "n1", clustered: true, "n1", "n2", "n3");
        Bind("b", "n2", clustered: true, "n1", "n2", "n3");
        Bind("c", "n3", clustered: true, "n1", "n2", "n3");

        await monitor.StartedAsync(CancellationToken.None);

        // c was repointed at another cluster.
        A.CallTo(() => inner["c"].QueryClusterNodes(A<CancellationToken>._)).Returns(Nodes("n9"));

        clock.Advance(TimeSpan.FromMinutes(1));
        await Eventually.Until(() => repository.Lookup(Name, "a+b") is not null);

        (await Listing()).Select(x => x.Key).Should().Equal(["a+b/" + Name, "c/" + Name],
            "a member leaves when it says it sees none of the cluster's nodes, and the row is renamed for it");
        repository.Lookup(Name, "a+b+c").Should().BeNull("the old key is gone with the old membership");
    }

    [Test]
    public async Task AClusterThatShrinksBelowTwoDissolves()
    {
        Bind("a", "n1", clustered: true, "n1", "n2");
        Bind("b", "n2", clustered: true, "n1", "n2");

        await monitor.StartedAsync(CancellationToken.None);
        (await Listing()).Should().ContainSingle().Which.Key.Should().Be("a+b/" + Name);

        repository.Remove(Name, "b");
        clock.Advance(TimeSpan.FromMinutes(1));
        await Eventually.Until(() => repository.Lookup(Name, "a+b") is null);

        (await Listing()).Select(x => x.Key).Should().Equal(["a/" + Name], "one target is not a cluster");
        provider.GetRequiredService<SchedulerTargets>().Find("a+b").Should().BeNull();
    }

    [Test]
    public async Task ATargetArrivingAtRuntimeTriggersARound()
    {
        Bind("a", "n1", clustered: true, "n1", "n2");

        await monitor.StartedAsync(CancellationToken.None);
        (await Listing()).Should().ContainSingle().Which.Key.Should().Be("a/" + Name);

        // An agent connecting: bound, and announced through the target registry.
        Bind("b", "n2", clustered: true, "n1", "n2");
        provider.GetRequiredService<SchedulerTargets>().Add(new SchedulerTarget { Name = "b", Origin = SchedulerOrigin.Agent });

        await Eventually.Until(() => repository.Lookup(Name, "a+b") is not null);

        (await Listing()).Should().ContainSingle().Which.Key.Should().Be("a+b/" + Name,
            "the monitor re-evaluates when a target is added, without waiting for the interval");
    }

    private void Bind(string target, string instanceId, bool clustered, params string[] nodes)
    {
        Bind(Name, target, instanceId, clustered, nodes);
    }

    private void Bind(string schedulerName, string target, string instanceId, bool clustered, params string[] nodes)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(schedulerName);
        A.CallTo(() => scheduler.GetStatus(A<CancellationToken>._)).Returns(SchedulerStatus.Running);
        A.CallTo(() => scheduler.GetSchedulerInstanceId(A<CancellationToken>._)).Returns(instanceId);
        A.CallTo(() => scheduler.GetMetadata(A<CancellationToken>._)).Returns(TestData.Metadata with
        {
            SchedulerName = schedulerName,
            SchedulerInstanceId = instanceId,
            IsProxy = true,
            JobStoreClustered = clustered,
            JobStorePersistent = clustered
        });
        A.CallTo(() => scheduler.QueryClusterNodes(A<CancellationToken>._)).Returns(Nodes(nodes));

        inner[target] = scheduler;
        repository.Bind(new FakeProxyScheduler(scheduler, target));
    }

    private static List<ClusterNode> Nodes(params string[] instanceIds)
    {
        return instanceIds
            .Select(id => new ClusterNode(id, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15), ClusterNodeState.Alive, IsCurrentNode: false))
            .ToList();
    }

    private static FireInstance Firing(string fireInstanceId, string instanceId)
    {
        return new FireInstance(
            fireInstanceId,
            new TriggerKey("at-midnight", "nightly"),
            new JobKey("nightly", "reports"),
            instanceId,
            FireInstanceState.Executing,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            ExecutionGroup: null);
    }

    private async Task<List<SchedulerRegistration>> Listing(string schedulerName = Name)
    {
        List<SchedulerRegistration> all = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();
        return all.Where(x => string.Equals(x.Name, schedulerName, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
