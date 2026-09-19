using AwesomeAssertions.Execution;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// A continuation scheduled through <see cref="HttpScheduler" /> and read back the same way: the
/// trigger, its state and the listing header that says what it is waiting for.
/// </summary>
/// <remarks>
/// <para>
/// The wire carries a continuation in two shapes, and each has its own way of going missing. A
/// <em>trigger</em> carries it as the three members the trigger serializer writes, so scheduling one
/// over HTTP is the serializer's round trip; a <em>listing header</em> carries it as the members
/// <c>TriggerHeaderDto</c> mirrors, which is a separate body with a separate mapping. Both are here.
/// </para>
/// <para>
/// A file-backed SQLite database rather than <c>:memory:</c>, for the reason <see cref="SqliteStores" />
/// gives, and a store rather than a fake scheduler because <c>AWAITING</c> is a state the store decides
/// on and a fake would simply hand back whatever it was told.
/// </para>
/// </remarks>
[NonParallelizable]
public class ContinuationOverHttpTest
{
    private const string SchedulerName = "continuations";

    private readonly SqliteStores stores = new("continuation-over-http");
    private readonly List<WebApplicationFactory<Program>> factories = [];

    private static readonly JobKey jobKey = new("job", "group");
    private static readonly TriggerKey parentKey = new("import", "nightly");
    private static readonly TriggerKey continuationKey = new("reconcile", "nightly");

    [OneTimeTearDown]
    public async Task TearDown()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
        stores.Dispose();
    }

    [Test]
    public async Task ATriggerScheduledOverTheWireWaitsForItsParent()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            await scheduler.ScheduleJob(
                JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create().WithIdentity(parentKey).ForJob(jobKey).WithCronSchedule("0 0 2 * * ?").Build());

            await client.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(continuationKey)
                .ForJob(jobKey)
                .StartAfter(parentKey, ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation)
                .WithCronSchedule("0 0 12 * * ?")
                .Build());

            ITrigger stored = (await scheduler.GetTrigger(continuationKey))!;
            ITrigger overTheWire = (await client.GetTrigger(continuationKey))!;

            using (new AssertionScope())
            {
                stored.Continuation.Should().Be(
                    Continuation.After(parentKey, ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation),
                    "the trigger the server stored is the one the client described, and the continuation "
                    + "is part of the description");
                overTheWire.Continuation.Should().Be(stored.Continuation, "and reading it back says the same");

                (await client.GetTriggerState(continuationKey)).Should().Be(TriggerState.Awaiting,
                    "the store holds a continuation in its own state, and the name of that state is what "
                    + "the wire carries");
                (await client.GetTriggerState(parentKey)).Should().Be(TriggerState.Normal,
                    "the parent is an ordinary trigger on an ordinary schedule");
            }
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// The listing is the answer to "why is this not running", so it has to carry the continuation
    /// without materializing the trigger.
    /// </summary>
    [Test]
    public async Task AListingSaysWhatAWaitingTriggerIsWaitingFor()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            await scheduler.ScheduleJob(
                JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create().WithIdentity(parentKey).ForJob(jobKey).WithCronSchedule("0 0 2 * * ?").Build());
            await scheduler.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(continuationKey)
                .ForJob(jobKey)
                .StartAfter(parentKey)
                .WithCronSchedule("0 0 12 * * ?")
                .Build());

            PagedResult<TriggerHeader> awaiting = await client.QueryTriggers(new TriggerQuery { State = TriggerState.Awaiting });

            TriggerHeader header = awaiting.Items.Should().ContainSingle(
                "the state filter travels over the wire, so a listing narrowed to Awaiting is narrowed "
                + "by the store rather than by the client").Subject;

            using (new AssertionScope())
            {
                header.Key.Should().Be(continuationKey);
                header.ContinuesAfter.Should().Be(parentKey,
                    "the header carries the parent, so an operator reading a listing does not have to "
                    + "load a trigger per row to learn what each one is waiting for");
                header.ContinuationCondition.Should().Be(ContinuationCondition.OnSuccess,
                    "an unstated condition is 'only if it worked', and the header says which it is");
            }

            PagedResult<TriggerHeader> everything = await client.QueryTriggers(new TriggerQuery());
            everything.Items.Single(x => x.Key.Equals(parentKey)).ContinuesAfter.Should().BeNull(
                "a trigger waiting for nothing says so, rather than carrying half of somebody else's key");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// One application per test, each with its own SQLite file: these tests write to the store, and a
    /// shared database would make the order they run in part of what they assert.
    /// </summary>
    private async Task<(IScheduler Scheduler, HttpScheduler Client)> CreateScheduler()
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        string schedulerName = $"{SchedulerName}-{factories.Count}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddQuartz(schedulerName, quartz => stores.Configure(quartz, schedulerName))));
        factories.Add(application);

        IScheduler scheduler = await application.Services
            .GetRequiredKeyedService<ISchedulerFactory>(schedulerName)
            .GetScheduler();

        // Deliberately not started: a continuation is about what the store holds, and a running
        // scheduler would fire the parent out from under the assertions.
        return (scheduler, new HttpScheduler(schedulerName, application.CreateClient()));
    }
}
