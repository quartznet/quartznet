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
    [Test]
    public async Task GetAllSchedulersShouldWork()
    {
        var secondFake = A.Fake<IScheduler>();
        A.CallTo(() => secondFake.SchedulerInstanceId).Returns("TEST_2_NON_CLUSTERED");
        WebApplicationFactory.Services.GetRequiredService<ISchedulerRepository>().Bind(secondFake);

        // This endpoint is not used by HttpScheduler, so the reader is built here - off the same wire
        // contract, because the status is an enum and the API spells it by name
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        using var httpClient = WebApplicationFactory.CreateClient();
        var result = await httpClient.Get<SchedulerHeaderDto[]>("schedulers", serializerOptions, CancellationToken.None);
        using (new AssertionScope())
        {
            result.Length.Should().Be(2);
            result.Should().ContainSingle(x => x.SchedulerInstanceId == TestData.SchedulerInstanceId);
            result.Should().ContainSingle(x => x.SchedulerInstanceId == "TEST_2_NON_CLUSTERED");
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
            .Build());

        captured.Should().NotBeNull("the server builds the limits it was sent before anything can be read back");

        ExecutionLimits? readBack = await HttpScheduler.GetExecutionLimits();

        readBack.Should().NotBeNull();
        readBack.Groups.Should().BeEquivalentTo(new[]
        {
            new ExecutionGroupLimit(ExecutionGroupScope.Named("batch"), 2),
            new ExecutionGroupLimit(ExecutionGroupScope.Named("tenant"), 8, ExecutionLimitScope.Cluster),
            new ExecutionGroupLimit(ExecutionGroupScope.OtherGroups, 1, ExecutionLimitScope.Cluster),
            new ExecutionGroupLimit(ExecutionGroupScope.Default, 3),
        });
    }

    [Test]
    public async Task GetSchedulerDetailsShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetMetadata(A<CancellationToken>._)).Returns(TestData.Metadata);
        A.CallTo(() => FakeScheduler.IsStarted).Returns(TestData.Metadata.Started);
        A.CallTo(() => FakeScheduler.InStandbyMode).Returns(TestData.Metadata.InStandbyMode);
        A.CallTo(() => FakeScheduler.IsShutdown).Returns(TestData.Metadata.Shutdown);

        HttpScheduler.SchedulerName.Should().Be(TestData.SchedulerName);
        HttpScheduler.SchedulerInstanceId.Should().Be(TestData.SchedulerInstanceId);
        HttpScheduler.InStandbyMode.Should().BeFalse();
        HttpScheduler.IsShutdown.Should().BeFalse();
        HttpScheduler.IsStarted.Should().BeTrue();

        var metadata = await HttpScheduler.GetMetadata();
        metadata.Should().BeEquivalentTo(TestData.Metadata, x => x.Excluding(y => y.IsProxy).Excluding(x => x.SchedulerTypeName));
        metadata.IsProxy.Should().BeTrue("HttpScheduler is a proxy to the remote scheduler");
        metadata.SchedulerTypeName.Should().Be(typeof(HttpScheduler).AssemblyQualifiedNameWithoutVersion());
        metadata.JobStoreTypeName.Should().Be(
            TestData.Metadata.JobStoreTypeName,
            "the job store type name passes through as a string - the remote type need not exist in the client process");
    }

    [Test]
    public void GetSchedulerContextShouldWork()
    {
        var testContext = new SchedulerContext
        {
            { "TestKey1", "TestValue" },
            { "TestKey2", "4352" }
        };

        A.CallTo(() => FakeScheduler.Context).Returns(testContext);

        var result = HttpScheduler.Context;
        result.Should().BeEquivalentTo(testContext);
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