using System.Diagnostics;
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
/// What a process pays for fronting a scheduler that does not answer.
/// </summary>
/// <remarks>
/// An unreachable <c>HttpScheduler</c> answers <c>Status</c> and <c>SchedulerInstanceId</c> after its
/// client's timeout — 100 seconds out of the box — and both used to be read on paths that had no
/// business waiting that long: the repository read one under its lock, which stalled every lookup in the
/// process including the API's own scheduler resolution, and the listing read both to build one row.
/// </remarks>
public sealed class UnreachableTargetTest
{
    private WebApplicationFactory<Program> host = null!;
    private StallingHandler handler = null!;
    private HttpClient stalling = null!;

    [SetUp]
    public void SetUp()
    {
        TestContentRoot.Apply();
        host = new WebApplicationFactory<Program>();

        handler = new StallingHandler();
        stalling = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://unreachable.test/"),

            // The client's own timeout stands for the default: long enough that a page waiting for it is
            // a page nobody sees.
            Timeout = TimeSpan.FromMinutes(5)
        };

        ISchedulerRepository repository = host.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        IScheduler local = A.Fake<IScheduler>();
        A.CallTo(() => local.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => local.SchedulerInstanceId).Returns(TestData.SchedulerInstanceId);
        A.CallTo(() => local.Status).Returns(SchedulerStatus.Running);
        A.CallTo(() => local.GetStatus(A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => local.GetSchedulerInstanceId(A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => local.GetMetadata(A<CancellationToken>._)).Returns(TestData.Metadata);
        repository.Bind(local);

        repository.Bind(new HttpScheduler("far-away", stalling), "far-away");
    }

    [TearDown]
    public async Task TearDown()
    {
        handler.Release();
        stalling.Dispose();
        handler.Dispose();
        await host.DisposeAsync();
    }

    /// <summary>
    /// The listing answers within its own deadline, says <see cref="SchedulerStatus.Unknown" /> of the
    /// target that did not answer, and does not hold up a request about the scheduler beside it.
    /// </summary>
    /// <remarks>
    /// The concurrent read is the half that used to be invisible: the repository's lock is held by every
    /// lookup in the process, so one unreachable target made the whole API wait for somebody else's
    /// network.
    /// </remarks>
    [Test]
    public async Task AnUnreachableTargetCostsTheDeadlineAndDelaysNothingElse()
    {
        using HttpClient client = host.CreateClient();

        long started = Stopwatch.GetTimestamp();
        Task<HttpResponseMessage> listing = client.GetAsync("schedulers");
        Task<HttpResponseMessage> unrelated = client.GetAsync($"schedulers/{TestData.SchedulerName}");

        using HttpResponseMessage unrelatedResponse = await unrelated;
        TimeSpan unrelatedElapsed = Stopwatch.GetElapsedTime(started);

        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        unrelatedElapsed.Should().BeLessThan(ContainerSchedulerRegistry.StatusTimeout,
            "a request about a scheduler in this process must not wait on a repository lock held for somebody else's network");

        using HttpResponseMessage listingResponse = await listing;
        TimeSpan listingElapsed = Stopwatch.GetElapsedTime(started);

        listingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        listingElapsed.Should().BeLessThan(ContainerSchedulerRegistry.StatusTimeout + TimeSpan.FromSeconds(10),
            "the listing asks under a deadline of its own rather than under the client's timeout");

        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
        SchedulerHeaderDto[] schedulers = JsonSerializer.Deserialize<SchedulerHeaderDto[]>(
            await listingResponse.Content.ReadAsStringAsync(), serializerOptions)!;

        SchedulerHeaderDto unreachable = schedulers.Should().ContainSingle(x => x.Name == "far-away").Subject;
        unreachable.Status.Should().Be(SchedulerStatus.Unknown,
            "unreachable is not absent and not shut down, and Unknown is what says the state could not be determined");
        unreachable.SchedulerInstanceId.Should().BeNull("nothing answered, so nothing said which node it is");
        unreachable.Origin.Should().Be(SchedulerOrigin.Remote);

        schedulers.Should().ContainSingle(x => x.Name == TestData.SchedulerName)
            .Which.Status.Should().NotBe(SchedulerStatus.Unknown,
                "the scheduler in this process answered at once, whatever the one beside it did");
    }

    /// <summary>
    /// A handler that never answers until it is released, which is what a host behind a network that is
    /// down looks like from here.
    /// </summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        private readonly CancellationTokenSource released = new();

        public void Release() => released.Cancel();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, released.Token);

            try
            {
                await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (released.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // The test is over; answer something rather than leaving a request pending.
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                released.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
