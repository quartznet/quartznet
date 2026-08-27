using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A cluster-scoped execution limit, measured across two real nodes sharing one database rather than
/// against a single store's ledger.
/// <para>
/// <c>JobStoreContractTest</c> already holds both stores to what the ledger says — a reservation counts,
/// a running execution counts, a node-scoped limit does not count either. None of that says what two
/// nodes racing each other actually manage to run at the same instant, which is the number a tenant
/// quota is bought for. The documented promise is deliberately not "never more than the limit": with
/// <c>AcquireTriggersWithinLock</c> off — the default — two nodes can read the same in-flight count in
/// the same instant and each take one trigger, so the ceiling holds within an acquisition round and the
/// transient overshoot is bounded by <c>limit + (nodes - 1)</c>. Turning the lock on makes it exact.
/// Both halves of that sentence are asserted here, because a bound nothing measures is a hope.
/// </para>
/// </summary>
/// <remarks>
/// Run against every engine that has a fixture: the ceiling is one aggregate over
/// <c>QRTZ_FIRED_TRIGGERS</c> evaluated beside the candidate select, under whichever lock the engine's
/// lock handler implements, and neither the aggregate's <c>COUNT(*)</c> type nor the locking is the same on
/// PostgreSQL as on SQL Server.
/// </remarks>
public abstract class ClusteredExecutionCeilingTestBase : ClusteredJobStoreTestBase
{
    /// <summary>The execution group the ceiling applies to — the "tenant" of a tenant quota.</summary>
    private const string Tenant = "tenant-acme";

    private const string Group = "clusterCeiling";

    /// <summary>How many triggers of <see cref="Tenant" /> may run at once, cluster-wide.</summary>
    private const int Limit = 2;

    private const int NodeCount = 2;

    /// <summary>
    /// Enough due work that the ceiling, rather than the supply of triggers, is what decides how many
    /// run: twenty against a ceiling of two leaves eighteen queued behind it for the whole measurement.
    /// </summary>
    private const int TriggerCount = 20;

    protected ClusteredExecutionCeilingTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterCeilingTest";

    [SetUp]
    public void ResetGate() => GatedJob.Reset();

    [TearDown]
    public void OpenGate() => GatedJob.Open();

    /// <summary>
    /// The default arrangement, and the one the bound is stated for: two nodes acquiring without the
    /// cluster's <c>TRIGGER_ACCESS</c> lock, one trigger per round each.
    /// </summary>
    [Test]
    public Task TwoNodesStayWithinTheStatedOvershootOfAClusterScopedCeiling()
    {
        // limit + (nodes - 1): each node acquiring lock-free can add at most one trigger over a ceiling
        // it read before the other node's acquisition landed.
        return AssertCeilingHolds(acquireTriggersWithinLock: false, allowedOvershoot: NodeCount - 1);
    }

    /// <summary>
    /// The same two nodes with acquisition serialized cluster-wide, which is the trade the documentation
    /// offers to anyone who needs the quota to be exact: the in-flight count and the acquisition that
    /// spends it happen inside one lock, so no node can act on a count another node has already spent.
    /// </summary>
    [Test]
    public Task WithAcquisitionUnderTheClusterLockTheCeilingIsExact()
    {
        return AssertCeilingHolds(acquireTriggersWithinLock: true, allowedOvershoot: 0);
    }

    private async Task AssertCeilingHolds(bool acquireTriggersWithinLock, int allowedOvershoot)
    {
        void ConfigureNode(NameValueCollection properties)
        {
            properties["quartz.clusterExecutionLimit." + Tenant] = Limit.ToString(CultureInfo.InvariantCulture);

            // One trigger per round, which is the only arrangement in which the lock-free path exists at
            // all: the store takes TRIGGER_ACCESS whenever it is asked for more than one trigger.
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "1";
            properties["quartz.jobStore.acquireTriggersWithinLock"] = acquireTriggersWithinLock ? "true" : "false";

            // Comfortably more threads than the ceiling allows, so that what caps concurrency is the
            // ceiling and not the thread pool - the failure mode where a green test proves nothing.
            properties["quartz.threadPool.maxConcurrency"] = "8";

            // Eighteen triggers sit behind the ceiling for as long as the gate is shut, and acquisition
            // excludes anything older than the misfire threshold. At the one-minute default the backlog
            // would be handed to RecoverMisfiredJobs partway through the measurement and the test would
            // be measuring misfire policy instead.
            properties["quartz.jobStore.misfireThreshold"] = "1800000";
        }

        IScheduler nodeA = await CreateScheduler("nodeA", configure: ConfigureNode);
        IScheduler nodeB = await CreateScheduler("nodeB", configure: ConfigureNode);

        try
        {
            IJobDetail job = JobBuilder.Create<GatedJob>()
                .WithIdentity("gatedJob", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            // Stored before either node is running, and dated far enough back that all twenty are due the
            // moment the nodes start: the contention this measures only exists while both nodes have more
            // work than the ceiling allows.
            DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-5);
            string[] expected = new string[TriggerCount];
            for (int i = 0; i < TriggerCount; i++)
            {
                expected[i] = "ceiling-" + i.ToString(CultureInfo.InvariantCulture);
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity(expected[i], Group)
                    .ForJob(job)
                    .WithExecutionGroup(Tenant)
                    .StartAt(start)
                    .Build());
            }

            await nodeA.Start();
            await nodeB.Start();

            await WaitForCondition(
                () => Task.FromResult(GatedJob.InFlight >= Limit),
                timeoutMs: 60_000,
                async () => $"the cluster to reach its ceiling of {Limit} concurrent executions; it reached "
                            + $"{GatedJob.Peak}. State:\n{await DumpDatabaseState()}");

            // An overshoot cannot be polled for, only waited out: it appears when two nodes acquire in the
            // same instant, and with the gate shut it then persists, because nothing completes to give the
            // slot back. Three idle-wait cycles of acquisition attempts by both nodes pass inside this.
            await Task.Delay(6000);

            int peak = GatedJob.Peak;
            TestContext.Out.WriteLine(
                $"Peak concurrent executions across {NodeCount} nodes: {peak} "
                + $"(ceiling {Limit}, bound {Limit + allowedOvershoot}, acquireTriggersWithinLock={acquireTriggersWithinLock})");
            TestContext.Out.WriteLine("Executions per node while held: " + string.Join(", ", GatedJob.Executions
                .GroupBy(x => x.InstanceId)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Count()}")));

            peak.Should().BeGreaterThanOrEqualTo(Limit,
                "a ceiling that never let its own quota through would pass every upper-bound assertion below "
                + "while serving the tenant less than it was promised");
            peak.Should().BeLessThanOrEqualTo(Limit + allowedOvershoot,
                "a cluster-scoped ceiling of {0} bounds what {1} nodes run at once by limit + (nodes - 1) when "
                + "they acquire lock-free, and by the limit itself when acquisition takes the cluster lock - "
                + "the numbers execution-groups.md states and a tenant quota is bought for",
                Limit, NodeCount);

            GatedJob.Open();

            await WaitForCondition(
                () => Task.FromResult(GatedJob.Completed >= TriggerCount),
                timeoutMs: 120_000,
                async () =>
                {
                    string[] missing = expected.Except(GatedJob.Executions.Select(x => x.TriggerName)).ToArray();
                    return $"all {TriggerCount} triggers to run once the ceiling stopped holding them back; "
                           + $"{missing.Length} never did ([{string.Join(", ", missing)}]). State:\n{await DumpDatabaseState()}";
                });

            GatedJob.Executions.Select(x => x.TriggerName).Should().BeEquivalentTo(expected,
                "a ceiling holds work back, it does not drop it - a missing trigger means the limit consumed "
                + "one instead of setting it aside, and a repeated one means two nodes ran the same trigger");

            GatedJob.Peak.Should().BeLessThanOrEqualTo(Limit + allowedOvershoot,
                "the ceiling applies to the drain as much as to the queue, so the peak over the whole run is "
                + "bounded by the same number as the peak while the gate was shut");
        }
        finally
        {
            // Before the shutdowns: a job still parked at the gate would otherwise hold a thread the
            // shutdown is waiting for, and the failure would be a hang rather than an assertion.
            GatedJob.Open();

            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    private sealed record Execution(string TriggerName, string InstanceId);

    /// <summary>
    /// Parks inside <c>Execute</c> until the test opens the gate, counting how many firings are inside it
    /// at once. That count is what a ceiling is a promise about, and it is only observable while the
    /// firings are held: a job that returns immediately never has a second one beside it to be counted
    /// with, however badly the limit is enforced.
    /// </summary>
    /// <remarks>
    /// The count is deliberately conservative at both ends — it rises after the store has already written
    /// the fired-trigger row and falls before the row is deleted — so it can only ever be at or below what
    /// the store's own ledger holds. That is the safe direction for an upper bound: this cannot report an
    /// overshoot the store did not commit.
    /// </remarks>
    private sealed class GatedJob : IJob
    {
        private static volatile TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static volatile ConcurrentQueue<Execution> executions = new();
        private static int inFlight;
        private static int peak;
        private static int completed;

        public static ConcurrentQueue<Execution> Executions => executions;

        public static int InFlight => Volatile.Read(ref inFlight);

        /// <summary>The most firings this job has ever had inside it at one instant.</summary>
        public static int Peak => Volatile.Read(ref peak);

        /// <summary>How many firings have run to the end, which only happens once the gate is open.</summary>
        public static int Completed => Volatile.Read(ref completed);

        public static void Reset()
        {
            Interlocked.Exchange(ref gate, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            Interlocked.Exchange(ref executions, new ConcurrentQueue<Execution>());
            Interlocked.Exchange(ref inFlight, 0);
            Interlocked.Exchange(ref peak, 0);
            Interlocked.Exchange(ref completed, 0);
        }

        public static void Open() => gate.TrySetResult();

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            // Recorded on the way in rather than on the way out, so that the ledger says who is running
            // what while the gate is still shut - which is the only moment at which it says anything.
            Executions.Enqueue(new Execution(context.Trigger.Key.Name, context.Scheduler.SchedulerInstanceId));
            RecordPeak(Interlocked.Increment(ref inFlight));
            try
            {
                await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
                Interlocked.Increment(ref completed);
            }
        }

        private static void RecordPeak(int observed)
        {
            int highest = Volatile.Read(ref peak);
            while (observed > highest)
            {
                int previous = Interlocked.CompareExchange(ref peak, observed, highest);
                if (previous == highest)
                {
                    return;
                }

                highest = previous;
            }
        }
    }
}
