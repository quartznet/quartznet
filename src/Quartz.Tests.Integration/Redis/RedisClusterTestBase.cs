using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Extensions.Redis;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Integration.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.Redis;

/// <summary>
/// The arrangement <c>Quartz.Extensions.Redis</c> exists for, actually running: two schedulers of one
/// cluster, one persistent store between them, and a Redis key as the only thing that keeps them out
/// of each other's way.
/// </summary>
/// <remarks>
/// <para>
/// The store is PostgreSQL rather than a SQLite file, which is what an in-process two-node fixture
/// would otherwise reach for. SQLite cannot carry this case at all: <c>AdoJobStoreBase.Initialize</c>
/// throws outright for a clustered SQLite store, and — before it gets that far — it replaces whatever
/// lock handler was configured with <c>SqliteLockHandler</c>, because a single file has one writer and
/// the global in-process gate is what makes it work. A fixture on SQLite would therefore either not
/// start or not be locking through Redis, and either way would prove nothing. So the Redis CI leg
/// starts a PostgreSQL container beside its Redis one; see <c>TestcontainersDatabaseEnvironment</c>.
/// </para>
/// <para>
/// Every trigger here carries <see cref="SimpleTriggerMisfireInstruction.IgnoreMisfires" />, which is
/// what makes "fired exactly once" a statement about a fixed set of instants rather than about a count.
/// A trigger with that instruction is exempt from the misfire handler and from acquisition's
/// <c>NoEarlierThan</c> bound, so its fire times stay the arithmetic series it was scheduled with
/// however far behind the nodes fall — a slow CI runner makes the test slower, never differently
/// scheduled.
/// </para>
/// </remarks>
public abstract class RedisClusterTestBase : ClusteredJobStoreTestBase
{
    /// <summary>
    /// The group every job and trigger a fixture here schedules belongs to.
    /// </summary>
    protected const string Group = "redisCluster";

    /// <summary>
    /// The trigger of the job with no concurrency constraint.
    /// </summary>
    protected const string OrdinaryTriggerName = "ordinaryTrigger";

    /// <summary>
    /// The trigger of the <c>[DisallowConcurrentExecution]</c> job.
    /// </summary>
    protected const string SerialTriggerName = "serialTrigger";

    /// <summary>
    /// How long one execution of the <c>[DisallowConcurrentExecution]</c> job takes.
    /// </summary>
    /// <remarks>
    /// Not zero, because an overlap is only observable while two executions are both inside. It is the
    /// window a second node's execution would have to land in for
    /// <see cref="PeakConcurrentSerialExecutions" /> to see it, and it is also what the recorded
    /// entry/exit pairs are intervals of.
    /// </remarks>
    private static readonly TimeSpan SerialJobDuration = TimeSpan.FromMilliseconds(10);

    private static volatile ConcurrentBag<FiringRecord> firings = new();
    private static int concurrentSerialExecutions;
    private static int peakConcurrentSerialExecutions;

    private string keyPrefix;

    protected RedisClusterTestBase() : base(TestConstants.PostgresProvider)
    {
    }

    /// <summary>
    /// Every firing both nodes recorded, in no particular order.
    /// </summary>
    internal static ConcurrentBag<FiringRecord> Firings => firings;

    /// <summary>
    /// The most executions of the <c>[DisallowConcurrentExecution]</c> job that were ever inside
    /// <see cref="SerialJob.Execute" /> at the same moment. One, or the guarantee is broken.
    /// </summary>
    protected static int PeakConcurrentSerialExecutions => Volatile.Read(ref peakConcurrentSerialExecutions);

    /// <summary>
    /// Fresh state, and a Redis key namespace of this test's own.
    /// </summary>
    /// <remarks>
    /// The prefix is per test rather than per fixture so that a lock a crashed run left behind — the
    /// keys carry a time to live, but it is thirty seconds — cannot make the next run wait for it.
    /// </remarks>
    [SetUp]
    public void ResetRedisClusterState()
    {
        Interlocked.Exchange(ref firings, new ConcurrentBag<FiringRecord>());
        Volatile.Write(ref concurrentSerialExecutions, 0);
        Volatile.Write(ref peakConcurrentSerialExecutions, 0);
        keyPrefix = $"quartz:test:{Guid.NewGuid():N}:";
    }

    /// <summary>
    /// Builds one node of this fixture's cluster: its own container, its own scheduler, and a Redis
    /// lock handler pointed at the fixture's server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A container per node, because the two nodes share a scheduler name and the container keys a
    /// scheduler's parts by that name — two nodes in one container would be one node asked for twice.
    /// </para>
    /// <para>
    /// The two settings that matter to the proof are on <c>ConfigureStore</c>, applied after
    /// <c>UseClustering</c> so that they win: <c>UseDbLocks</c> is off, so no <c>QRTZ_LOCKS</c> row is
    /// ever taken and Redis is the only mutual exclusion there is, and
    /// <c>AcquireTriggersWithinLock</c> is on, so every acquisition cycle goes through it rather than
    /// only the batched ones.
    /// </para>
    /// </remarks>
    internal async Task<RedisNode> CreateNode(string instanceId)
    {
        ServiceCollection services = new();

        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = instanceId;
                options.IdleWaitTime = TimeSpan.FromSeconds(1);

                // One at a time, deliberately. Both triggers are due at the same instants, so a node
                // batching would take both rows every cycle and could hold the whole schedule while its
                // peer found nothing — and a fixture where one node fires everything proves nothing
                // about a lock. At one, both nodes reach for the same earliest row on every cycle,
                // which is the contention a broken lock turns into a double fire, and the loser of each
                // race still has the other trigger to fire.
                options.MaxBatchSize = 1;
            });

            quartz.UseDefaultThreadPool(maxConcurrency: 10);

            quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(Database.ConnectionString);
                store.UseSystemTextJsonSerializer();

                store.UseClustering(cluster =>
                {
                    cluster.CheckinInterval = TimeSpan.FromSeconds(1);
                    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(5);
                });

                store.UseRedisLockHandler(redis =>
                {
                    redis.RedisConfiguration = RedisTestEnvironment.ConnectionString;
                    redis.KeyPrefix = keyPrefix;

                    // The shipped default is 100 ms, which is a tenth of a second of latency for the
                    // node that lost the race. This fixture schedules a firing every 100 ms, which no
                    // deployment does, so the poll is tightened to match rather than the workload
                    // loosened to hide it.
                    redis.LockRetryInterval = TimeSpan.FromMilliseconds(20);
                });

                store.ConfigureStore(options =>
                {
                    options.TablePrefix = "QRTZ_";

                    // UseClustering turned this on, because clustering has never worked without a
                    // cluster-wide lock. Here the cluster-wide lock is the Redis key, so the database
                    // one is turned back off: what is being proved is that Redis alone is enough, and
                    // a row lock underneath would prove it whatever Redis did.
                    options.UseDbLocks = false;

                    // Load-bearing, and the reason MaxBatchSize can stay at one: acquisition takes the
                    // lock when it asks for more than one trigger *or* when this is set, so without it
                    // a one-at-a-time node would acquire lock-free and the Redis key would guard
                    // nothing that matters here.
                    options.AcquireTriggersWithinLock = true;
                });
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        AdoJobStoreBase store = (AdoJobStoreBase) provider.GetRequiredService<IJobStore>();

        return new RedisNode(provider, scheduler, store);
    }

    /// <summary>
    /// Adds the durable job both triggers fire, and the two triggers themselves.
    /// </summary>
    /// <remarks>
    /// Scheduled on one node; the other reaches them through the store, which is the point.
    /// </remarks>
    protected static async Task<DateTimeOffset[]> ScheduleOrdinaryTrigger(
        IScheduler scheduler,
        DateTimeOffset start,
        int firingCount,
        TimeSpan interval)
    {
        IJobDetail job = JobBuilder.Create<OrdinaryJob>()
            .WithIdentity("ordinary", Group)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(job, new AddJobOptions { Replace = true });

        return await Schedule(scheduler, job, OrdinaryTriggerName, start, firingCount, interval);
    }

    /// <summary>
    /// The same, for the job that may only ever have one execution anywhere in the cluster.
    /// </summary>
    protected static async Task<DateTimeOffset[]> ScheduleSerialTrigger(
        IScheduler scheduler,
        DateTimeOffset start,
        int firingCount,
        TimeSpan interval)
    {
        IJobDetail job = JobBuilder.Create<SerialJob>()
            .WithIdentity("serial", Group)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(job, new AddJobOptions { Replace = true });

        return await Schedule(scheduler, job, SerialTriggerName, start, firingCount, interval);
    }

    private static async Task<DateTimeOffset[]> Schedule(
        IScheduler scheduler,
        IJobDetail job,
        string triggerName,
        DateTimeOffset start,
        int firingCount,
        TimeSpan interval)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(triggerName, Group)
            .ForJob(job)
            .StartAt(start)
            .WithSimpleSchedule(schedule => schedule
                .WithInterval(interval)
                .WithRepeatCount(firingCount - 1)
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires))
            .Build());

        // The same arithmetic SimpleTriggerImpl does, which is the whole of a simple trigger's schedule
        // once misfires are out of the picture.
        DateTimeOffset[] expected = new DateTimeOffset[firingCount];
        for (int i = 0; i < firingCount; i++)
        {
            expected[i] = start + i * interval;
        }

        return expected;
    }

    /// <summary>
    /// The property the whole fixture exists for: the trigger's scheduled fire times are exactly the
    /// ones it was given, each of them fired, and none of them fired twice.
    /// </summary>
    /// <remarks>
    /// Stated as three assertions rather than one set comparison so that a failure says which of the
    /// three went wrong, and names the instants involved. A doubled fire time means two nodes fired the
    /// same row; a missing one means a row was acquired and dropped.
    /// </remarks>
    protected static void AssertFiredExactlyOnce(string triggerName, DateTimeOffset[] expected)
    {
        FiringRecord[] recorded = Firings.Where(x => x.TriggerKey.Name == triggerName).ToArray();

        DateTimeOffset[] doubled = recorded
            .GroupBy(x => x.ScheduledFireTimeUtc)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToArray();

        doubled.Should().BeEmpty(
            "a scheduled fire time belongs to exactly one node, so a repeat means both acquired the "
            + "same row through the Redis lock; doubled: [{0}]", Format(doubled));

        DateTimeOffset[] missing = expected.Except(recorded.Select(x => x.ScheduledFireTimeUtc)).OrderBy(x => x).ToArray();

        missing.Should().BeEmpty(
            "every scheduled fire time of '{0}' has to fire — a missing one is a firing that was "
            + "acquired and then dropped, which is the other half of what a broken lock produces; "
            + "missing: [{1}]", triggerName, Format(missing));

        DateTimeOffset[] unexpected = recorded.Select(x => x.ScheduledFireTimeUtc).Except(expected).OrderBy(x => x).ToArray();

        unexpected.Should().BeEmpty(
            "the misfire instruction is IgnoreMisfires, so nothing reschedules '{0}' and its fire times "
            + "stay the series it was scheduled with; unexpected: [{1}]", triggerName, Format(unexpected));

        recorded.Select(x => x.FireInstanceId).Should().OnlyHaveUniqueItems(
            "a fire instance id names one firing, and two firings that shared one would make every "
            + "count above agree with itself while the store had fired twice");
    }

    /// <summary>
    /// That the <c>[DisallowConcurrentExecution]</c> job was never running twice at once, said twice:
    /// by the counter the executions themselves keep, and by the entry/exit instants they recorded.
    /// </summary>
    /// <remarks>
    /// The counter is what actually catches an overlap — it is incremented and decremented inside the
    /// execution, so it cannot miss one. The interval check is what makes a failure readable: it names
    /// the two firings and the nodes they ran on.
    /// </remarks>
    protected static void AssertSerialJobNeverOverlapped()
    {
        PeakConcurrentSerialExecutions.Should().Be(1,
            "[DisallowConcurrentExecution] is a cluster-wide guarantee, and the store keeps it by "
            + "blocking the trigger while an execution holds the job — a peak above one means two nodes "
            + "were inside the same job at the same moment");

        FiringRecord[] ordered = Firings
            .Where(x => x.TriggerKey.Name == SerialTriggerName)
            .OrderBy(x => x.Entered)
            .ToArray();

        for (int i = 1; i < ordered.Length; i++)
        {
            FiringRecord previous = ordered[i - 1];
            FiringRecord current = ordered[i];

            current.Entered.Should().BeOnOrAfter(previous.Exited,
                "one execution of the job has to finish before the next begins; {0} on {1} entered at "
                + "{2:O} while {3} on {4} was still inside until {5:O}",
                current.FireInstanceId, current.InstanceId, current.Entered,
                previous.FireInstanceId, previous.InstanceId, previous.Exited);
        }
    }

    /// <summary>
    /// Writes what the run actually produced, so that a green leg still reports its numbers.
    /// </summary>
    protected static void ReportFirings()
    {
        TestContext.Out.WriteLine("Firings per node: " + NodeFiringCounts());

        TestContext.Out.WriteLine("Firings per trigger: " + string.Join(", ", Firings
            .GroupBy(x => x.TriggerKey.Name)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Count()}")));

        TestContext.Out.WriteLine(
            "Peak concurrent executions of the [DisallowConcurrentExecution] job: "
            + PeakConcurrentSerialExecutions.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Both nodes fired, which is what says the lock rotated rather than one node holding it throughout.
    /// </summary>
    protected static void AssertBothNodesFired(params string[] instanceIds)
    {
        Firings.Select(x => x.InstanceId).Distinct().Should().BeEquivalentTo(instanceIds,
            "a lock that never changes hands would let one node fire everything, and every assertion "
            + "about double firing would pass against a cluster that was not one");
    }

    protected static int FiringCount() => Firings.Count;

    /// <summary>
    /// How many firings the named node recorded.
    /// </summary>
    protected static int NodeFiringCount(string instanceId)
        => Firings.Count(x => string.Equals(x.InstanceId, instanceId, StringComparison.Ordinal));

    /// <summary>
    /// The per-node tally, for the message a timed-out wait fails with: "no firings arrived" and "one
    /// node took them all" are different failures and want telling apart.
    /// </summary>
    protected static string NodeFiringCounts()
        => string.Join(", ", Firings
            .GroupBy(x => x.InstanceId)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Count()}"));

    private static string Format(IEnumerable<DateTimeOffset> instants)
        => string.Join(", ", instants.Select(x => x.ToString("O", CultureInfo.InvariantCulture)));

    private static void Record(IJobExecutionContext context, DateTimeOffset entered, DateTimeOffset exited)
    {
        Firings.Add(new FiringRecord(
            context.FireInstanceId,
            context.Scheduler.SchedulerInstanceId,
            context.Trigger.Key,
            context.ScheduledFireTimeUtc!.Value,
            entered,
            exited));
    }

    /// <summary>
    /// One firing, as the assertions need to see it: which firing it was, which node ran it, which
    /// trigger and scheduled instant it was for, and the window it occupied.
    /// </summary>
    internal sealed record FiringRecord(
        string FireInstanceId,
        string InstanceId,
        TriggerKey TriggerKey,
        DateTimeOffset ScheduledFireTimeUtc,
        DateTimeOffset Entered,
        DateTimeOffset Exited);

    /// <summary>
    /// The job with no concurrency constraint at all: whatever exactly-once holds for it is the store's
    /// doing rather than a queue's.
    /// </summary>
    public sealed class OrdinaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Record(context, now, now);
            return default;
        }
    }

    /// <summary>
    /// The job that may only have one execution in the whole cluster at a time.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            DateTimeOffset entered = DateTimeOffset.UtcNow;

            int inside = Interlocked.Increment(ref concurrentSerialExecutions);
            int peak = Volatile.Read(ref peakConcurrentSerialExecutions);
            while (inside > peak && Interlocked.CompareExchange(ref peakConcurrentSerialExecutions, inside, peak) != peak)
            {
                peak = Volatile.Read(ref peakConcurrentSerialExecutions);
            }

            try
            {
                // Deliberately not the job's own token. A node shutting down mid-run is one of the
                // things under test, and a firing that turned into a cancellation there would be a
                // firing the totals could not count.
                await Task.Delay(SerialJobDuration, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Record(context, entered, DateTimeOffset.UtcNow);
                Interlocked.Decrement(ref concurrentSerialExecutions);
            }
        }
    }

    /// <summary>
    /// One node of the cluster, and everything a test has to reach into it for: the scheduler, the
    /// store it was built with, and the lock handler that store settled on.
    /// </summary>
    internal sealed class RedisNode : IAsyncDisposable
    {
        private readonly ServiceProvider provider;

        internal RedisNode(ServiceProvider provider, IScheduler scheduler, AdoJobStoreBase store)
        {
            this.provider = provider;
            Scheduler = scheduler;
            Store = store;
        }

        public IScheduler Scheduler { get; }

        internal AdoJobStoreBase Store { get; }

        /// <summary>
        /// The handler the store is actually locking through, which is the premise every assertion in
        /// these fixtures rests on.
        /// </summary>
        internal RedisLockHandler LockHandler => (RedisLockHandler) Store.LockHandler;

        public async ValueTask DisposeAsync()
        {
            if (Scheduler.Status is not (SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown))
            {
                await Scheduler.Shutdown(waitForJobsToComplete: false);
            }

            await provider.DisposeAsync();
        }
    }
}
