using System.Net;
using System.Net.Http.Json;

using AwesomeAssertions.Execution;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What <see cref="QuartzHttpApiOptions.IsJobTypeAllowed" /> does to the three endpoints that take a job
/// in their body: a name the predicate refuses is <c>403</c> and nothing is stored, and a deployment that
/// sets no predicate is the API as every earlier release had it.
/// </summary>
/// <remarks>
/// <para>
/// The schedulers are real and so are their stores, because "nothing was stored" is the assertion that
/// matters and only a store can answer it.
/// </para>
/// <para>
/// The refused name is <c>Quartz.Jobs.NativeJob</c> — the type <c>SECURITY.md</c> names as the reason an
/// authorized caller is trusted with the host — and this project references neither it nor its package,
/// which <see cref="TheRefusedJobTypeNameIsGenuinelyUnresolvable" /> pins. That is what makes the answer
/// meaningful: a refusal that had gone looking for the type first would have failed differently.
/// </para>
/// </remarks>
public abstract class JobTypeAllowListTest
{
    /// <summary>
    /// The type <c>SECURITY.md</c> names: it implements <see cref="IJob" /> and starts the executable its
    /// job data names, so it is what an allow-list exists to keep off the wire.
    /// </summary>
    private const string NativeJobTypeName = "Quartz.Jobs.NativeJob, Quartz.Jobs";

    private const string SchedulerName = "allow-list";
    private const string JobsRoute = "schedulers/" + SchedulerName + "/jobs";

    private static readonly string AllowedJobTypeName = typeof(DummyJob).AssemblyQualifiedName!;

    private readonly List<WebApplicationFactory<Program>> factories = [];

    private IScheduler scheduler = null!;

    [TearDown]
    public async Task TearDown()
    {
        if (scheduler is not null)
        {
            await scheduler.Shutdown();
            scheduler = null!;
        }

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    [Test]
    public void TheRefusedJobTypeNameIsGenuinelyUnresolvable()
    {
        // Everything below is weaker if this name happens to resolve in the test process.
        Type.GetType(NativeJobTypeName, throwOnError: false).Should().BeNull();
    }

    [Test]
    public async Task AnAllowedJobTypeIsStored()
    {
        using HttpClient client = await ApiWith(OnlyTheTestSupportNamespace);

        using HttpResponseMessage response = await AddJob(client, AllowedJobTypeName);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        IJobDetail? stored = await scheduler.GetJobDetail(new JobKey("nightly", "reports"));
        stored.Should().NotBeNull();
        stored!.JobType.FullName.Should().Be(AllowedJobTypeName,
            "an allow-list decides which names are accepted and changes nothing about the one that is");
    }

    [Test]
    public async Task ARefusedJobTypeIsForbiddenAndTheDetailNamesIt()
    {
        using HttpClient client = await ApiWith(OnlyTheTestSupportNamespace);

        using HttpResponseMessage response = await AddJob(client, NativeJobTypeName);
        string body = await response.Content.ReadAsStringAsync();

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"body was {body}");
            body.Should().Contain(NativeJobTypeName,
                "the caller sent that name, so repeating it says which job was refused and gives nothing away");
            body.Should().NotContain("Quartz-ExceptionType",
                "a rule that said no is a decision rather than a failure, which is how the per-scheduler "
                + "policy's refusal is shaped too");

            (await scheduler.Exists(new JobKey("nightly", "reports"))).Should().BeFalse(
                "the refusal comes before the conversion, so the store never hears about the request");
        }
    }

    /// <summary>
    /// The batch endpoint converts every job before it stores any of them, so one refused name refuses
    /// the whole call rather than storing the jobs that came before it.
    /// </summary>
    [Test]
    public async Task ARefusedNameInABatchRefusesTheWholeBatch()
    {
        using HttpClient client = await ApiWith(OnlyTheTestSupportNamespace);

        // Through the client, so the batch on the wire is the one Quartz.HttpClient writes rather than
        // one hand-typed here — the trigger half of that body is a serializer's job.
        HttpScheduler remote = new(SchedulerName, client);

        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> batch = new()
        {
            [Job("first", AllowedJobTypeName)] = [Trigger("first")],
            [Job("second", NativeJobTypeName)] = [Trigger("second")],
        };

        Func<Task> act = async () => await remote.ScheduleJobs(batch, new ScheduleJobOptions { Replace = true });

        await act.Should().ThrowAsync<HttpClientException>().WithMessage("*" + NativeJobTypeName + "*",
            "the refusal names the job that caused it, which is the only way a caller can fix the batch");

        using (new AssertionScope())
        {
            (await scheduler.Exists(new JobKey("first", "reports"))).Should().BeFalse(
                "a batch is one request, and a request that is refused stores none of what it carried");
            (await scheduler.Exists(new JobKey("second", "reports"))).Should().BeFalse();
        }
    }

    /// <summary>
    /// The default. Leaving the option unset is the API as every release before 4.1 had it, which is what
    /// makes this addition purely additive.
    /// </summary>
    [Test]
    public async Task WithNoAllowListAnyNameIsAccepted()
    {
        using HttpClient client = await ApiWith(isJobTypeAllowed: null);

        using HttpResponseMessage response = await AddJob(client, NativeJobTypeName);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "null allows every type, and it is the default");

        IJobDetail? stored = await scheduler.GetJobDetail(new JobKey("nightly", "reports"));
        stored.Should().NotBeNull();
        stored!.JobType.FullName.Should().Be(NativeJobTypeName,
            "the name is still stored unresolved, whichever of the two paths the request took");
    }

    /// <summary>
    /// An allow-list narrows what may be named; it does not turn a malformed request into a refusal. The
    /// two answers are for different people — one is the caller's to fix, the other is the operator's to
    /// widen.
    /// </summary>
    [Test]
    public async Task AMalformedNameIsStillABadRequest()
    {
        using HttpClient client = await ApiWith(OnlyTheTestSupportNamespace);

        using HttpResponseMessage response = await AddJob(client, "   ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static bool OnlyTheTestSupportNamespace(string jobTypeName)
    {
        // A namespace prefix rather than a set of exact names, because one type has more than one
        // spelling and the prefix matches all of them.
        return jobTypeName.StartsWith("Quartz.Tests.AspNetCore.Support.", StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> AddJob(HttpClient client, string jobTypeName)
    {
        return client.PostAsJsonAsync(JobsRoute, new { job = JobBody("nightly", jobTypeName), replace = true });
    }

    private static object JobBody(string name, string jobTypeName) => new
    {
        name,
        group = "reports",
        jobType = jobTypeName,
        durable = true,
        // Stated, so that neither end has any reason to go looking for the type to deduce them.
        concurrentExecutionDisallowed = false,
        persistJobDataAfterExecution = false,
        jobDataMap = new Dictionary<string, object>(),
    };

    private static IJobDetail Job(string name, string jobTypeName) => JobBuilder.Create()
        .OfType((JobType) jobTypeName)
        .WithIdentity(name, "reports")
        .StoreDurably()
        // Stated, so that neither end has any reason to go looking for the type to deduce them.
        .DisallowConcurrentExecution(false)
        .PersistJobDataAfterExecution(false)
        .Build();

    private static ITrigger Trigger(string name) => TriggerBuilder.Create()
        .WithIdentity(name, "reports")
        .ForJob(name, "reports")
        .StartAt(DateTimeOffset.UtcNow.AddHours(1))
        .Build();

    private async Task<HttpClient> ApiWith(Func<string, bool>? isJobTypeAllowed)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddQuartz(SchedulerName, ConfigureStore);
            services.AddQuartzHttpApi(options => options.IsJobTypeAllowed = isJobTypeAllowed);
        }));

        factories.Add(configured);

        HttpClient client = configured.CreateClient();

        // Built, because a registration nothing has built answers 404 on every route and the status
        // codes are the whole subject here.
        scheduler = await configured.Services.GetRequiredKeyedService<ISchedulerFactory>(SchedulerName).GetScheduler();

        return client;
    }

    /// <summary>
    /// The store the scheduler is given. The in-memory one here; a derived fixture runs the same contract
    /// over a persistent one.
    /// </summary>
    protected virtual void ConfigureStore(IQuartzBuilder builder) => builder.UseInMemoryStore();
}
