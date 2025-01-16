using System.Text.Json;

using FakeItEasy;

using FluentAssertions;
using FluentAssertions.Execution;

using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.HttpClient;
using Quartz.Spi;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class SchedulerEndpointsTest : WebApiTest
{
    [Test]
    public async Task GetAllSchedulersShouldWork()
    {
        var secondFake = A.Fake<IScheduler>();
        A.CallTo(() => secondFake.SchedulerInstanceId).Returns("TEST_2_NON_CLUSTERED");
        WebApplicationFactory.Services.GetRequiredService<ISchedulerRepository>().Bind(secondFake);

        // This endpoint is not used by HttpScheduler
        using var httpClient = WebApplicationFactory.CreateClient();
        var result = await httpClient.Get<SchedulerHeaderDto[]>("schedulers", new JsonSerializerOptions(JsonSerializerDefaults.Web), CancellationToken.None);
        using (new AssertionScope())
        {
            result.Length.Should().Be(2);
            result.Should().ContainSingle(x => x.SchedulerInstanceId == TestData.SchedulerInstanceId);
            result.Should().ContainSingle(x => x.SchedulerInstanceId == "TEST_2_NON_CLUSTERED");
        }
    }

    [Test]
    public async Task GetSchedulerDetailsShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetMetaData(A<CancellationToken>._)).Returns(TestData.Metadata);
        A.CallTo(() => FakeScheduler.IsStarted).Returns(TestData.Metadata.Started);
        A.CallTo(() => FakeScheduler.InStandbyMode).Returns(TestData.Metadata.InStandbyMode);
        A.CallTo(() => FakeScheduler.IsShutdown).Returns(TestData.Metadata.Shutdown);

        HttpScheduler.SchedulerName.Should().Be(TestData.SchedulerName);
        HttpScheduler.SchedulerInstanceId.Should().Be(TestData.SchedulerInstanceId);
        HttpScheduler.InStandbyMode.Should().BeFalse();
        HttpScheduler.IsShutdown.Should().BeFalse();
        HttpScheduler.IsStarted.Should().BeTrue();

        var metadata = await HttpScheduler.GetMetaData();
        metadata.Should().BeEquivalentTo(TestData.Metadata, x => x.Excluding(y => y.SchedulerRemote).Excluding(x => x.SchedulerType));
        metadata.SchedulerRemote.Should().BeTrue();
        metadata.SchedulerType.Should().Be<HttpScheduler>();
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