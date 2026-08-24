using System.Collections.Concurrent;

using MELT;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The arrangement multi-tenancy is built on, actually running: two schedulers, one database, one table
/// prefix, two <c>SCHED_NAME</c>s.
/// <para>
/// Every Quartz table has <c>SCHED_NAME</c> as the first column of its primary key and every statement
/// filters on it, so the isolation is a property of the schema rather than of the code paths. That is a
/// claim about every statement, which is why it is worth asserting against a database rather than a
/// stub: the two tenants here schedule the <em>same job and trigger keys</em>, so a query that forgot its
/// <c>SCHED_NAME</c> predicate would find the neighbour's row rather than nothing at all, and a scheduler
/// that acquired across the boundary would fire it.
/// </para>
/// <para>
/// The prefix is the same on both, deliberately. <see cref="Quartz.Configuration.SharedDatabaseValidator" />
/// warns when two schedulers of one container share a database and <em>disagree</em> about the prefix;
/// the supported arrangement has to stay silent, or the warning becomes noise every multi-tenant
/// deployment learns to ignore. <c>SharedDatabaseValidatorTest</c> covers the prefix matrix with stub
/// providers; this is the half that runs the arrangement.
/// </para>
/// </summary>
public abstract class SharedDatabaseTenancyTestBase
{
    /// <summary>The two tenants, which are two <c>SCHED_NAME</c> values and nothing else.</summary>
    protected const string Acme = "TenancyAcme";

    protected const string Initech = "TenancyInitech";

    /// <summary>
    /// The key both tenants schedule under. Identical on purpose: distinct keys would pass even against a
    /// store that ignored <c>SCHED_NAME</c> entirely.
    /// </summary>
    private const string SharedTriggerName = "nightly";

    private const string Group = "tenancy";

    [SetUp]
    public void ResetTenantJob() => TenantJob.Reset();

    /// <summary>
    /// Configures the tenant's store to point at this fixture's database, with the default table prefix.
    /// </summary>
    protected abstract void UseDatabase(IPersistentStoreBuilder store);

    /// <summary>
    /// Creates the schema, if this fixture's database does not already have one.
    /// </summary>
    protected virtual Task PrepareDatabase() => Task.CompletedTask;

    /// <summary>
    /// Removes both tenants' rows, so a later run starts against a database that holds neither.
    /// </summary>
    protected abstract Task CleanUpDatabase();

    [Test]
    public async Task TwoTenantsInOneTableSetFireOnlyTheirOwnTriggers()
    {
        await PrepareDatabase();

        ITestLoggerFactory loggerFactory = TestLoggerFactory.Create();

        ServiceCollection services = new();

        // Registered before AddQuartz, which only adds a factory when the container has none: this is
        // what puts the shared-database check's own warnings within reach of the assertion below.
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddQuartz(Acme, ConfigureTenant);
        services.AddQuartz(Initech, ConfigureTenant);

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler acme = await provider.GetRequiredKeyedService<ISchedulerFactory>(Acme).GetScheduler();
        IScheduler initech = await provider.GetRequiredKeyedService<ISchedulerFactory>(Initech).GetScheduler();

        try
        {
            acme.SchedulerName.Should().Be(Acme);
            initech.SchedulerName.Should().Be(Initech);

            await ScheduleTenantWork(acme);
            await ScheduleTenantWork(initech);

            // Asserted before either scheduler starts, because the shared one-shot trigger is deleted the
            // moment it fires and a listing taken afterwards could not tell "scoped correctly" from "gone".
            (await TriggerNames(acme)).Should().BeEquivalentTo([SharedTriggerName, Acme + "-only"],
                "a query is scoped to the scheduler that issues it, so a tenant lists its own trigger rows "
                + "and no others - including the one whose key it shares with its neighbour");
            (await TriggerNames(initech)).Should().BeEquivalentTo([SharedTriggerName, Initech + "-only"]);

            await acme.Start();
            await initech.Start();

            await WaitForCondition(
                () => TenantJob.Firings.Count >= 2,
                timeoutMs: 60_000,
                () => $"both tenants to fire their own copy of '{SharedTriggerName}'; so far: "
                      + string.Join(", ", TenantJob.Firings.Select(x => $"{x.SchedulerName}/{x.Tenant}")));

            // Absence cannot be polled for. A scheduler that acquired across the SCHED_NAME boundary
            // would fire the neighbour's trigger as a third firing, which arrives after the two legitimate
            // ones rather than before them.
            await Task.Delay(3000);

            TenantJob.Firings.Should().HaveCount(2,
                "there are two trigger rows, one per tenant, and each belongs to exactly one scheduler - a "
                + "third firing means one scheduler acquired a row that was not its own");

            foreach (Firing firing in TenantJob.Firings)
            {
                firing.SchedulerName.Should().Be(firing.Tenant,
                    "the tenant marker travels in the job data map of the row that was scheduled under that "
                    + "SCHED_NAME, so a firing whose scheduler disagrees with it is a row read across the boundary");
            }

            TenantJob.Firings.Select(x => x.SchedulerName).Should().BeEquivalentTo([Acme, Initech]);

            // What survives the firing is scoped too: each tenant's one-shot trigger is gone and its own
            // future one is not, which is the same boundary read from the far side.
            (await TriggerNames(acme)).Should().BeEquivalentTo([Acme + "-only"],
                "the tenant's own one-shot trigger completed and was removed; the neighbour's is untouched "
                + "and was never this tenant's to see");
            (await TriggerNames(initech)).Should().BeEquivalentTo([Initech + "-only"]);

            loggerFactory.Sink.LogEntries.Should().NotBeEmpty(
                "the silence asserted next means nothing unless this is the factory Quartz actually logs "
                + "through - a container that quietly built its own would make the check below vacuous");

            loggerFactory.Sink.LogEntries
                .Where(x => x.LoggerName.Contains("SharedDatabaseValidator", StringComparison.Ordinal))
                .Should().BeEmpty(
                    "one database, one table prefix and two scheduler names is the arrangement the check exists "
                    + "to protect, so warning about it would teach every multi-tenant deployment to filter the "
                    + "category out - and with it the mistyped prefix the warning is for");
        }
        finally
        {
            await acme.Shutdown(waitForJobsToComplete: false);
            await initech.Shutdown(waitForJobsToComplete: false);
            await CleanUpDatabase();
        }
    }

    private void ConfigureTenant(IQuartzBuilder builder)
    {
        builder.UsePersistentStore(store =>
        {
            UseDatabase(store);
            // The same prefix on both, which is the supported arrangement: SCHED_NAME is what separates
            // the tenants, and a prefix is for separate table sets rather than for isolation.
            store.Configure(options => options.TablePrefix = "QRTZ_");
        });
    }

    /// <summary>
    /// Schedules one trigger whose key both tenants use and one whose name only this tenant uses. The
    /// second never fires — it exists so that a listing has something to be wrong about.
    /// </summary>
    private static async Task ScheduleTenantWork(IScheduler scheduler)
    {
        IJobDetail job = JobBuilder.Create<TenantJob>()
            .WithIdentity("tenantJob", Group)
            .UsingJobData("tenant", scheduler.SchedulerName)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(job, new AddJobOptions { Replace = true });

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SharedTriggerName, Group)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(1))
            .Build());

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(scheduler.SchedulerName + "-only", Group)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build());
    }

    private static async Task<string[]> TriggerNames(IScheduler scheduler)
    {
        PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggers(new TriggerQuery());
        return triggers.Items.Select(x => x.Key.Name).ToArray();
    }

    private static async Task WaitForCondition(Func<bool> condition, int timeoutMs, Func<string> message)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Timed out after {timeoutMs} ms waiting for {message()}");
    }

    private sealed record Firing(string SchedulerName, string Tenant);

    /// <summary>
    /// Records which scheduler ran it and which tenant's data map it was handed. The two agree exactly
    /// when no row crossed the <c>SCHED_NAME</c> boundary.
    /// </summary>
    private sealed class TenantJob : IJob
    {
        private static volatile ConcurrentQueue<Firing> firings = new();

        public static ConcurrentQueue<Firing> Firings => firings;

        public static void Reset() => Interlocked.Exchange(ref firings, new ConcurrentQueue<Firing>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Firings.Enqueue(new Firing(context.Scheduler.SchedulerName, context.MergedJobDataMap.GetString("tenant")));
            return default;
        }
    }
}
