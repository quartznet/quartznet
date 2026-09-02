using System.Net;
using System.Text.Json;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Extensibility;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;
using Quartz.Util;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class SchedulerEndpointsTest : WebApiTest
{
    /// <summary>
    /// The listing is the registry's, so it carries both what has been built and what has only been
    /// registered — and says which is which.
    /// </summary>
    /// <remarks>
    /// The test application registers a default scheduler with <c>AddQuartz()</c> and never builds it,
    /// which is exactly the case the old listing could not report: it read the repository, so a
    /// registration nothing had resolved was indistinguishable from a name that did not exist.
    /// </remarks>
    [Test]
    public async Task GetAllSchedulersListsRegistrationsAndTheSchedulersBuiltFromThem()
    {
        IScheduler secondFake = A.Fake<IScheduler>();
        A.CallTo(() => secondFake.SchedulerName).Returns("SecondScheduler");
        A.CallTo(() => secondFake.SchedulerInstanceId).Returns("TEST_2_NON_CLUSTERED");
        A.CallTo(() => secondFake.Status).Returns(SchedulerStatus.Standby);
        WebApplicationFactory.Services.GetRequiredService<ISchedulerRepository>().Bind(secondFake);

        // This endpoint is not used by HttpScheduler, so the reader is built here - off the same wire
        // contract, because the status is an enum and the API spells it by name
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        using var httpClient = WebApplicationFactory.CreateClient();
        var result = await httpClient.Get<SchedulerHeaderDto[]>("schedulers", serializerOptions, CancellationToken.None);
        using (new AssertionScope())
        {
            SchedulerHeaderDto bound = result.Should().ContainSingle(x => x.Name == TestData.SchedulerName).Subject;
            bound.SchedulerInstanceId.Should().Be(TestData.SchedulerInstanceId);
            bound.Origin.Should().Be(SchedulerOrigin.Runtime, "a scheduler bound into the repository has no registration behind it");

            SchedulerHeaderDto second = result.Should().ContainSingle(x => x.Name == "SecondScheduler").Subject;
            second.SchedulerInstanceId.Should().Be("TEST_2_NON_CLUSTERED");
            second.Status.Should().Be(SchedulerStatus.Standby);

            SchedulerHeaderDto registered = result.Should().ContainSingle(x => x.Name == QuartzSchedulerOptions.DefaultInstanceName).Subject;
            registered.Status.Should().BeNull("nothing has built the default scheduler, and listing it did not");
            registered.SchedulerInstanceId.Should().BeNull("there is no scheduler to have an instance id");
            registered.Origin.Should().Be(SchedulerOrigin.Container);
        }
    }

    /// <summary>
    /// The wire carries a limit's scope, because a limit that lost it would come back as a per-node one
    /// — a quota silently multiplied by the node count, which is the very thing the scope exists to say.
    /// </summary>
    [Test]
    public async Task ExecutionLimitsRoundTripKeepsEachLimitsScope()
    {
        ExecutionLimits? captured = null;
        A.CallTo(() => FakeScheduler.SetExecutionLimits(A<ExecutionLimits>._, A<CancellationToken>._))
            .Invokes((ExecutionLimits limits, CancellationToken _) => captured = limits);
        A.CallTo(() => FakeScheduler.GetExecutionLimits(A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<ExecutionLimits?>(captured));

        await HttpScheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 2)
            .ForGroup("tenant", 8, ExecutionLimitScope.Cluster)
            .ForOtherGroups(1, ExecutionLimitScope.Cluster)
            .ForDefaultGroup(3)
            .UseTriggerGroupWhenUnset()
            .Build());

        captured.Should().NotBeNull("the server builds the limits it was sent before anything can be read back");
        captured.UsesTriggerGroupWhenUnset.Should().BeTrue("the derivation decides which bucket an ungrouped trigger is counted in, so the server has to be told");

        ExecutionLimits? readBack = await HttpScheduler.GetExecutionLimits();

        readBack.Should().NotBeNull();
        readBack.Groups.Should().BeEquivalentTo(new[]
        {
            new ExecutionGroupLimit(ExecutionGroupScope.Named("batch"), 2),
            new ExecutionGroupLimit(ExecutionGroupScope.Named("tenant"), 8, ExecutionLimitScope.Cluster),
            new ExecutionGroupLimit(ExecutionGroupScope.OtherGroups, 1, ExecutionLimitScope.Cluster),
            new ExecutionGroupLimit(ExecutionGroupScope.Default, 3),
        });
        readBack.UsesTriggerGroupWhenUnset.Should().BeTrue("the flag comes back with the limits it was set beside");
    }

    /// <summary>
    /// The trigger-group derivation survives the round trip on its own, with no group limit beside it.
    /// </summary>
    /// <remarks>
    /// It is the case both ends used to drop. The server built limits only when the request named a
    /// group, so a request carrying the flag alone <em>cleared</em> the limits; the client answered null
    /// whenever the limits map was empty, discarding a flag the server had sent. Nothing was limited
    /// either way, so nothing went wrong loudly — but <c>GetExecutionLimits</c> answered null where the
    /// scheduler held limits, and setting them through the client threw the configuration away.
    /// </remarks>
    [Test]
    public async Task ExecutionLimitsRoundTripKeepsTheTriggerGroupDerivationWithNoGroupLimits()
    {
        ExecutionLimits? captured = null;
        A.CallTo(() => FakeScheduler.SetExecutionLimits(A<ExecutionLimits>._, A<CancellationToken>._))
            .Invokes((ExecutionLimits limits, CancellationToken _) => captured = limits);
        A.CallTo(() => FakeScheduler.GetExecutionLimits(A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<ExecutionLimits?>(captured));

        await HttpScheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create().UseTriggerGroupWhenUnset().Build());

        captured.Should().NotBeNull("asking for the derivation is configuration, not the absence of it");
        captured.IsEmpty.Should().BeTrue("no group was named");
        captured.UsesTriggerGroupWhenUnset.Should().BeTrue();

        ExecutionLimits? readBack = await HttpScheduler.GetExecutionLimits();

        readBack.Should().NotBeNull("the server said the derivation is on, and an empty group map is not the same answer as none");
        readBack.IsEmpty.Should().BeTrue();
        readBack.UsesTriggerGroupWhenUnset.Should().BeTrue();
    }

    /// <summary>
    /// Passing null still clears the limits, which is the answer that has to stay distinguishable from
    /// the empty-but-configured one above.
    /// </summary>
    [Test]
    public async Task ClearingExecutionLimitsLeavesTheSchedulerWithNone()
    {
        ExecutionLimits? captured = ExecutionLimitsBuilder.Create().ForGroup("batch", 2).Build();
        A.CallTo(() => FakeScheduler.SetExecutionLimits(A<ExecutionLimits>._, A<CancellationToken>._))
            .Invokes((ExecutionLimits limits, CancellationToken _) => captured = limits);
        A.CallTo(() => FakeScheduler.GetExecutionLimits(A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<ExecutionLimits?>(captured));

        await HttpScheduler.SetExecutionLimits(null);

        captured.Should().BeNull();
        (await HttpScheduler.GetExecutionLimits()).Should().BeNull();
    }

    /// <summary>
    /// The node listing round-trips whole: the verdict, both times, and the order the server chose.
    /// </summary>
    /// <remarks>
    /// The order is contract rather than presentation — "current node first" is decided by the node that
    /// answered, and a client that re-sorted would be asserting something about its own identity, which
    /// it does not have. The node with no check-in history is here because null is not zero: a reader
    /// that saw <c>0001-01-01</c> would draw a node that has been dead since the epoch.
    /// </remarks>
    [Test]
    public async Task ClusterNodesRoundTripKeepEveryVerdictAndTheServersOrder()
    {
        A.CallTo(() => FakeScheduler.QueryClusterNodes(A<CancellationToken>._))
            .Returns(new List<ClusterNode>
            {
                TestData.CurrentClusterNode,
                TestData.FailedClusterNode,
                TestData.ClusterNodeWithoutCheckIn
            });

        List<ClusterNode> nodes = await HttpScheduler.QueryClusterNodes();

        nodes.Should().Equal([
            TestData.CurrentClusterNode,
            TestData.FailedClusterNode,
            TestData.ClusterNodeWithoutCheckIn
        ]);
    }

    [Test]
    public async Task GetSchedulerDetailsShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetMetadata(A<CancellationToken>._)).Returns(TestData.Metadata);
        A.CallTo(() => FakeScheduler.Status).Returns(TestData.Metadata.Status);

        HttpScheduler.SchedulerName.Should().Be(TestData.SchedulerName);
        HttpScheduler.SchedulerInstanceId.Should().Be(TestData.SchedulerInstanceId);
        HttpScheduler.Status.Should().Be(SchedulerStatus.Running);

        var metadata = await HttpScheduler.GetMetadata();
        metadata.Should().BeEquivalentTo(TestData.Metadata, x => x.Excluding(y => y.IsProxy).Excluding(x => x.SchedulerTypeName));
        metadata.IsProxy.Should().BeTrue("HttpScheduler is a proxy to the remote scheduler");
        metadata.SchedulerTypeName.Should().Be(typeof(HttpScheduler).AssemblyQualifiedNameWithoutVersion());
        metadata.JobStoreTypeName.Should().Be(
            TestData.Metadata.JobStoreTypeName,
            "the job store type name passes through as a string - the remote type need not exist in the client process");
    }

    /// <summary>
    /// The context endpoint reads a context holding what a context holds: whatever the application put
    /// in it.
    /// </summary>
    /// <remarks>
    /// The non-string entry is the point. This test passed for as long as the endpoint refused one,
    /// because the fixture's scheduler is a fake whose context the test filled with strings — while
    /// every scheduler a container had built carried a value that made the endpoint answer <c>500</c>.
    /// </remarks>
    [Test]
    public async Task GetSchedulerContextShouldWork()
    {
        SchedulerContext testContext = new()
        {
            { "TestKey1", "TestValue" },
            { "TestKey2", "4352" },
            { "TestKey3", 4352 }
        };

        A.CallTo(() => FakeScheduler.Context).Returns(testContext);

        // HttpScheduler does not call this endpoint - a remote scheduler has no in-process context to
        // hand out - so the reader is built here, off the same wire contract the endpoint writes
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        using var httpClient = WebApplicationFactory.CreateClient();
        var dto = await httpClient.Get<SchedulerContextDto>(
            $"schedulers/{TestData.SchedulerName}/context",
            serializerOptions,
            CancellationToken.None);

        dto.Context.Should().Equal(new Dictionary<string, string?>
        {
            ["TestKey1"] = "TestValue",
            ["TestKey2"] = "4352",
            ["TestKey3"] = "4352"
        }, "a value that is not a string arrives as its invariant text - text is all a remote reader has");
    }

    /// <summary>
    /// The two members a scheduler reached over HTTP cannot have: both are live in-process objects,
    /// and a snapshot of either would be a lie a caller could write to.
    /// </summary>
    [Test]
    public void ContextAndListenerManagerAreNotSupportedRemotely()
    {
        Action context = () => _ = HttpScheduler.Context;
        context.Should().Throw<NotSupportedException>("the context lives in the scheduler's own process")
            .WithMessage("*HttpScheduler.Context*");

        Action listenerManager = () => _ = HttpScheduler.ListenerManager;
        listenerManager.Should().Throw<NotSupportedException>("listeners run where the jobs run")
            .WithMessage("*HttpScheduler.ListenerManager*");
    }

    [Test]
    public async Task StartShouldWork()
    {
        await HttpScheduler.Start();
        A.CallTo(() => FakeScheduler.Start(A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);

        await HttpScheduler.StartDelayed(TimeSpan.FromMilliseconds(5_000));
        A.CallTo(() => FakeScheduler.StartDelayed(TimeSpan.FromMilliseconds(5_000), A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    /// <summary>
    /// The delay is a <see cref="TimeSpan" /> on the wire, and one with a sub-millisecond part arrives
    /// as it was sent — <c>?delayMilliseconds=</c> rounded it away.
    /// </summary>
    [Test]
    public async Task StartDelayedSendsTheDelayAsATimeSpan()
    {
        using HttpClient httpClient = WebApplicationFactory.CreateClient();

        using HttpResponseMessage response = await httpClient.PostAsync(
            $"schedulers/{TestData.SchedulerName}/start?delay=01:02:03.0004560",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        A.CallTo(() => FakeScheduler.StartDelayed(new TimeSpan(0, 1, 2, 3, 0, 456), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task StartWithANegativeDelayIsRejected()
    {
        using HttpClient httpClient = WebApplicationFactory.CreateClient();

        using HttpResponseMessage response = await httpClient.PostAsync(
            $"schedulers/{TestData.SchedulerName}/start?delay=-00:00:30",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a delay running backwards is not a delay");
        A.CallTo(() => FakeScheduler.StartDelayed(A<TimeSpan>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task StandbyShouldWork()
    {
        await HttpScheduler.Standby();
        A.CallTo(() => FakeScheduler.Standby(A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ShutdownShouldWork()
    {
        await HttpScheduler.Shutdown();
        A.CallTo(() => FakeScheduler.Shutdown(false, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);

        await HttpScheduler.Shutdown(true);
        A.CallTo(() => FakeScheduler.Shutdown(true, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ClearShouldWork()
    {
        await HttpScheduler.Clear();
        A.CallTo(() => FakeScheduler.Clear(A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task PauseAllShouldWork()
    {
        await HttpScheduler.PauseAll();
        A.CallTo(() => FakeScheduler.PauseAll(A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task ResumeAllShouldWork()
    {
        await HttpScheduler.ResumeAll();
        A.CallTo(() => FakeScheduler.ResumeAll(A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }
}