using System.Net;
using System.Net.Http.Json;

using FakeItEasy;

using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What a job type name means on the wire: it is a name, both ends treat it as data, and the two
/// attribute-derived flags travel as "stated" or "not stated" rather than as a bare
/// <see langword="bool" />.
/// </summary>
public class JobTypeOverTheWireTest : WebApiTest
{
    private const string ServerOnlyJobTypeName = "Quartz.Tests.AspNetCore.HttpApi.ServerOnlyJob, Server.Only.Assembly";

    [Test]
    public void TheServerOnlyJobTypeNameIsGenuinelyUnresolvable()
    {
        // Everything below is vacuous if this name happens to resolve in the test process.
        Type.GetType(ServerOnlyJobTypeName, throwOnError: false).Should().BeNull();
    }

    /// <summary>
    /// A well-formed request that says nothing about concurrency stores a job whose author declared it
    /// unsafe to run concurrently as unsafe to run concurrently.
    /// </summary>
    /// <remarks>
    /// The flags used to be non-nullable on the wire, and System.Text.Json's default for a missing
    /// <see langword="bool" /> is <see langword="false" />, so the request overrode
    /// <see cref="DisallowConcurrentExecutionAttribute" /> without saying anything. Nothing about the
    /// request looked wrong, and a hand-written client or a curl example is the normal way to send one.
    /// </remarks>
    [Test]
    public async Task AnOmittedConcurrencyFlagDoesNotOverrideTheAttribute()
    {
        IJobDetail? stored = null;
        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<AddJobOptions>._, A<CancellationToken>._))
            .Invokes((IJobDetail jobDetail, AddJobOptions _, CancellationToken _) => stored = jobDetail);

        using HttpClient client = WebApplicationFactory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/schedulers/{TestData.SchedulerName}/jobs",
            new
            {
                job = new
                {
                    name = "j",
                    group = "g",
                    jobType = typeof(NonConcurrentJob).AssemblyQualifiedName,
                    durable = true,
                    jobDataMap = new Dictionary<string, object>()
                },
                replace = true
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stored.Should().NotBeNull();
        stored!.ConcurrentExecutionDisallowed.Should().BeTrue(
            "the type says so and the request said nothing, so the type is what decides");
        stored.PersistJobDataAfterExecution.Should().BeTrue();
    }

    /// <summary>
    /// A flag the request does state still wins, which is what lets a caller add a job of a type only the
    /// server has.
    /// </summary>
    [Test]
    public async Task AStatedConcurrencyFlagIsHonoured()
    {
        IJobDetail? stored = null;
        A.CallTo(() => FakeScheduler.AddJob(A<IJobDetail>._, A<AddJobOptions>._, A<CancellationToken>._))
            .Invokes((IJobDetail jobDetail, AddJobOptions _, CancellationToken _) => stored = jobDetail);

        using HttpClient client = WebApplicationFactory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/schedulers/{TestData.SchedulerName}/jobs",
            new
            {
                job = new
                {
                    name = "j",
                    group = "g",
                    jobType = typeof(NonConcurrentJob).AssemblyQualifiedName,
                    durable = true,
                    concurrentExecutionDisallowed = false,
                    jobDataMap = new Dictionary<string, object>()
                },
                replace = true
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stored.Should().NotBeNull();
        stored!.ConcurrentExecutionDisallowed.Should().BeFalse();
    }

    /// <summary>
    /// Reading a job whose type resolves nowhere answers with what is known. One accepted write used to
    /// leave a row that made <c>GET /jobs/{group}/{name}</c> fail for everyone, for good — and it is the
    /// ordinary heterogeneous-cluster case as much as it is a poisoned row.
    /// </summary>
    [Test]
    public async Task ReadingAJobWhoseTypeResolvesNowhereAnswers()
    {
        JobKey key = new("j4", "g");
        IJobDetail jobDetail = JobBuilder.Create()
            .OfType((JobType) ServerOnlyJobTypeName)
            .WithIdentity(key)
            .StoreDurably()
            .Build();

        A.CallTo(() => FakeScheduler.GetJobDetail(key, A<CancellationToken>._)).Returns(jobDetail);

        using HttpClient client = WebApplicationFactory.CreateClient();
        HttpResponseMessage response = await client.GetAsync($"/schedulers/{TestData.SchedulerName}/jobs/g/j4");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a node that lacks the job's assembly still has to be able to serve the job's detail");
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ServerOnlyJobTypeName, "what is known about the job is its name");
    }

    /// <summary>
    /// A client schedules a job whose type only the server has. <c>packages/http-client.md</c> promises
    /// exactly this — "client doesn't need the type locally to manage jobs" — and before rc.1 the client
    /// threw <c>InvalidOperationException: Job type … cannot be resolved</c> out of the projection onto
    /// the wire, so remote provisioning required the client to carry the job assembly.
    /// </summary>
    [Test]
    public async Task AClientSchedulesAJobWhoseTypeOnlyTheServerHas()
    {
        IJobDetail? received = null;
        A.CallTo(() => FakeScheduler.ScheduleJob(A<IJobDetail>._, A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .Invokes((IJobDetail jobDetail, ITrigger _, ScheduleJobOptions _, CancellationToken _) => received = jobDetail)
            .Returns(DateTimeOffset.UtcNow);

        IJobDetail jobDetail = JobBuilder.Create()
            .OfType((JobType) ServerOnlyJobTypeName)
            .WithIdentity("remote", "remote-group")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("remote-trigger", "remote-group")
            .ForJob(jobDetail)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();

        await HttpScheduler.ScheduleJob(jobDetail, trigger);

        received.Should().NotBeNull();
        received!.JobType.FullName.Should().Be(ServerOnlyJobTypeName);
    }

    /// <summary>
    /// The other direction of the same promise: a job the server answers with is not resolved on receipt,
    /// so a server cannot choose an assembly simple name a client's runtime goes looking for.
    /// </summary>
    [Test]
    public async Task AJobReadOverTheWireIsNotResolvedByTheClient()
    {
        JobKey key = new("remote-read", "remote-group");
        A.CallTo(() => FakeScheduler.GetJobDetail(key, A<CancellationToken>._)).Returns(
            JobBuilder.Create().OfType((JobType) ServerOnlyJobTypeName).WithIdentity(key).StoreDurably().Build());

        IJobDetail? jobDetail = await HttpScheduler.GetJobDetail(key);

        jobDetail.Should().NotBeNull();
        jobDetail!.JobType.FullName.Should().Be(ServerOnlyJobTypeName);
        JobDetailFlags.ConcurrentExecutionDisallowed(jobDetail).Should().BeNull(
            "the server knew nothing about the flag and the client did not go looking for the type to find out");
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    private sealed class NonConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
