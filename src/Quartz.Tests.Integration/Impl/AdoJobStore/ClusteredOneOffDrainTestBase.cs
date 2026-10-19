using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The gate a clustered store's automatic <c>MaxBatchSize</c> passed before it tracked the pool (#3862,
/// #3900): two and four nodes draining two thousand one-offs due at one instant, one trigger a round
/// against the automatic default, and what the cluster-wide lock does to that. Since 4.6 also the gate
/// of the round that takes no lock (#4067): the same drain with the shipped rounds against 4.5's locked
/// ones, at two and at four nodes.
/// </summary>
/// <remarks>
/// <para>
/// <b>A measurement, not a CI leg.</b> <c>LongRunning</c> keeps it out of every integration leg
/// (<c>build/Build.cs</c>, <c>GetTestFilter</c>). It asserts only that every one-off ran exactly once;
/// the verdict is written to <c>TestContext.Out</c>, so run it with detailed output. Each engine's fixture
/// says how to run it: <see cref="ClusteredOneOffDrainPostgresTest" /> against a PostgreSQL at its shipped
/// durability, <see cref="ClusteredOneOffDrainSqlServerTest" /> against SQL Server with
/// <c>READ_COMMITTED_SNAPSHOT</c> on, where a round without the lock fires apart (#4117).
/// </para>
/// <para>
/// <b>What passes.</b> The default's drain on two nodes is at least 1.2 times as fast as one trigger a
/// round; on four nodes it is not slower; the <c>TRIGGER_ACCESS</c> wait's 99th percentile stays under
/// 250 ms; and no node takes less than a fifth of the work. #3900 passed it with margin once completions
/// left the lock (#3863), which is what made the clustered default the pool. A fail says that default
/// should go back to one, and points at <c>SKIP LOCKED</c> claiming as what batching on a cluster needs
/// first.
/// </para>
/// <para>
/// <b>Why one trigger a round was lock-free and the pool was not.</b> Through 4.5 the ADO store took
/// <c>TRIGGER_ACCESS</c> for an acquisition that asked for more than one trigger, or when
/// <c>AcquireTriggersWithinLock</c> was on. So the batched arm bought fewer rounds and paid the
/// cluster-wide row lock for each, and whether that netted out was #3862's question. Those two arms run
/// with <c>LockFreeFiringRounds</c> off, which is that shape on 4.6's code.
/// </para>
/// <para>
/// <b>What #4067's arm asks.</b> With the shipped defaults a round of one-offs takes no lock at all, so
/// the nodes fire side by side instead of one at a time. The gate is that the lock-free drain is at
/// least as fast as the locked one at two nodes and at four, and that four nodes drain no slower than
/// two within noise; the report says the four-to-two ratio either way.
/// </para>
/// <para>
/// <b>The batched arms set nothing but the option.</b> The automatic default is what ships, so the gate
/// measures it, and asserts that each node resolved that default to the pool and acquired more than one
/// trigger a round.
/// </para>
/// <para>
/// All nodes are in this process, as in <see cref="ClusteredSoakTestBase" />: they share a thread pool and
/// a GC heap, which is the way this is not a four-machine cluster.
/// </para>
/// </remarks>
[NonParallelizable]
public abstract class ClusteredOneOffDrainTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "oneOffDrain";

    /// <summary>How many one-offs each arm drains, all due at the same instant.</summary>
    private const int Firings = 2_000;

    /// <summary>Each node's pool, and what the automatic arm's <c>MaxBatchSize</c> resolves to.</summary>
    private const int PoolSize = 10;

    /// <summary>
    /// How far out the work is due: long enough to write two thousand triggers and start four nodes.
    /// </summary>
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(20);

    protected ClusteredOneOffDrainTestBase(ClusteredTestDatabase database) : base(database)
    {
    }

    /// <summary>The engine as the report names it.</summary>
    protected abstract string Engine { get; }

    /// <summary>What the report calls the per-firing transaction count this engine publishes.</summary>
    protected virtual string CommitsColumn => "Commits/firing";

    protected override string SchedulerName => "ClusterOneOffDrain";

    [SetUp]
    public void ResetLedger() => DrainJob.Reset();

    [Test]
    public async Task TwoAndFourNodesDrainABacklog_OneTriggerARoundAgainstTheDefault()
    {
        List<Arm> arms = [];
        foreach (int nodes in (int[]) [2, 4])
        {
            // One is 4.2's behaviour, set explicitly, and the automatic default with every round locked is
            // 4.5's; both run with lock-free rounds off. The last arm sets nothing, which is what ships.
            foreach ((int? batch, bool lockFree) in (IEnumerable<(int?, bool)>) [(1, false), (null, false), (null, true)])
            {
                arms.Add(await Drain(nodes, batch, lockFree));
                await CleanUpDatabaseState();
            }
        }

        TestContext.Out.WriteLine(Report(arms, await Durability()));
    }

    /// <summary>
    /// What a commit costs this server, which decides what a batch saves, as the report's first line says it.
    /// </summary>
    protected abstract Task<string> Durability();

    /// <param name="nodeCount">How many nodes drain the backlog.</param>
    /// <param name="configuredBatchSize">The <c>MaxBatchSize</c> to set, or <see langword="null" /> to set nothing.</param>
    /// <param name="lockFreeRounds">Whether the rounds take the lock only when something in them needs it, which is what ships, or every round is locked, as 4.5's were.</param>
    private async Task<Arm> Drain(int nodeCount, int? configuredBatchSize, bool lockFreeRounds)
    {
        DrainJob.Reset();

        void Configure(NameValueCollection properties)
        {
            properties["quartz.threadPool.maxConcurrency"] = PoolSize.ToString(CultureInfo.InvariantCulture);
            if (configuredBatchSize is int explicitBatchSize)
            {
                properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = explicitBatchSize.ToString(CultureInfo.InvariantCulture);
            }

            if (!lockFreeRounds)
            {
                properties["quartz.jobStore.lockFreeFiringRounds"] = "false";
            }

            // The backlog is older than the default minute by the end of a slow drain, and a trigger past
            // the misfire threshold is the misfire handler's rather than acquisition's, so the arm would
            // be measuring misfire policy.
            properties["quartz.jobStore.misfireThreshold"] = "1800000";
        }

        // Each node in a container the fixture keeps, because the resolved batch size lives with the
        // scheduler's resources there, and nowhere a scheduler exposes.
        List<SchedulerHost> hosts = [];
        List<IScheduler> nodes = [];
        using LockWaitRecorder recorder = new();

        try
        {
            for (int i = 0; i < nodeCount; i++)
            {
                SchedulerHost host = await CreateSchedulerHost(
                    "drain-node-" + i.ToString(CultureInfo.InvariantCulture),
                    checkinIntervalMs: 5_000,
                    checkinMisfireThresholdMs: 30_000,
                    configure: Configure);
                hosts.Add(host);
                nodes.Add(host.Scheduler);
            }

            int maxBatchSize = configuredBatchSize ?? PoolSize;
            foreach (SchedulerHost host in hosts)
            {
                host.Services.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(maxBatchSize,
                    "a clustered store's automatic MaxBatchSize is its pool, as every persistent store's is (#3900), "
                    + "and an explicit value wins");
            }

            DateTimeOffset dueAt = DateTimeOffset.UtcNow + Lead;
            IJobDetail job = JobBuilder.Create<DrainJob>()
                .WithIdentity("drain", Group)
                .StoreDurably()
                .Build();

            // The one-off API's shape: a durable job per job type and a single-shot trigger per call, so
            // completing a firing deletes its trigger and leaves the job.
            ITrigger[] triggers = new ITrigger[Firings];
            for (int i = 0; i < Firings; i++)
            {
                triggers[i] = TriggerBuilder.Create()
                    .WithIdentity("one-off-" + i.ToString(CultureInfo.InvariantCulture), Group)
                    .ForJob(job)
                    .StartAt(dueAt)
                    .Build();
            }

            await nodes[0].ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = triggers }, ScheduleJobOptions.Replacing);

            foreach (IScheduler node in nodes)
            {
                await node.Start();
            }

            (DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1)).Should().BeBefore(dueAt,
                "writing the backlog and starting {0} nodes has to finish before the work is due, or part of the "
                + "drain happens outside the measurement", nodeCount);

            await WaitUntil(dueAt - TimeSpan.FromSeconds(1));
            long? commitsBefore = await PublishedCommits();
            recorder.Clear();
            await WaitUntil(dueAt);

            long due = Stopwatch.GetTimestamp();
            await WaitForCondition(
                () => Task.FromResult(DrainJob.Count >= Firings),
                timeoutMs: 600_000,
                async () => $"{Firings} one-offs to run on {nodeCount} nodes at MaxBatchSize {maxBatchSize}; "
                            + $"{DrainJob.Count} did. State:\n{await DumpDatabaseState()}");

            TimeSpan drain = Stopwatch.GetElapsedTime(due, DrainJob.LastStarted);

            foreach (IScheduler node in nodes)
            {
                await node.Shutdown(waitForJobsToComplete: true);
            }

            long? commits = await PublishedCommits() - commitsBefore;

            DrainJob.Runs.Should().HaveCount(Firings,
                "every one-off is a distinct trigger, and a cluster runs each exactly once whatever it batches");
            DrainJob.Runs.Values.Should().AllSatisfy(runs => runs.Should().Be(1,
                "two nodes running one one-off is the failure a cluster exists to prevent"));

            LockWaits waits = recorder.Snapshot();
            if (configuredBatchSize is null)
            {
                waits.TriggersPerRound.Should().BeGreaterThan(1,
                    "the default arm is the batched one: with two thousand one-offs due at once, a round that "
                    + "starts with more than one thread free takes more than one trigger");
            }

            if (lockFreeRounds)
            {
                waits.RoundsLockFree.Should().BeGreaterThan(0,
                    "with the shipped defaults a round of concurrent-allowed one-offs takes no lock once every node has checked in, "
                    + "so an arm in which no round said so never ran the path the gate is about");
            }
            else
            {
                waits.RoundsLockFree.Should().Be(0, "with LockFreeFiringRounds off every round is locked");
            }

            return new Arm(
                nodeCount,
                maxBatchSize,
                configuredBatchSize is null,
                lockFreeRounds,
                drain,
                DrainJob.PerNode.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                waits,
                commits);
        }
        finally
        {
            foreach (IScheduler node in nodes)
            {
                if (node.Status != SchedulerStatus.Shutdown)
                {
                    await node.Shutdown(waitForJobsToComplete: false);
                }
            }

            foreach (SchedulerHost host in hosts)
            {
                await host.Services.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// The server's count of committed transactions for the database, read once every connection has
    /// published its own; <see langword="null" /> where the server does not say.
    /// </summary>
    protected abstract Task<long?> PublishedCommits();

    private static async Task WaitUntil(DateTimeOffset instant)
    {
        TimeSpan remaining = instant - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    private string Report(List<Arm> arms, string durability)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Clustered one-off drain, {Firings} due at one instant, pool {PoolSize} a node, {Engine} with {durability}.");
        report.AppendLine();
        report.AppendLine("| Nodes | MaxBatchSize | Rounds | Drain (s) | Firings/s | Share per node | TRIGGER_ACCESS waits | p50 (ms) | p99 (ms) | Max (ms) | Rounds | Lock-free | Locked | Triggers/round | " + CommitsColumn + " |");
        report.AppendLine("|---:|---:|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (Arm arm in arms)
        {
            LockWaits waits = arm.Waits;
            string shares = string.Join(" / ", arm.PerNode
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => (100.0 * x.Value / Firings).ToString("F0", CultureInfo.InvariantCulture) + " %"));

            string batch = arm.Automatic
                ? "automatic (" + arm.MaxBatchSize.ToString(CultureInfo.InvariantCulture) + ")"
                : arm.MaxBatchSize.ToString(CultureInfo.InvariantCulture);

            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {arm.Nodes} | {batch} | {(arm.LockFree ? "lock-free (4.6)" : "locked (4.5)")} | {arm.Drain.TotalSeconds:F2} | {arm.FiringsPerSecond:F1} | {shares} | "
                + $"{waits.TriggerAccess.Count:N0} | {waits.Percentile(50):F1} | {waits.Percentile(99):F1} | {waits.Max:F1} | "
                + $"{waits.Rounds:N0} | {waits.RoundsLockFree:N0} | {waits.RoundsLocked:N0} | {waits.TriggersPerRound:F2} | {(arm.Commits is { } commits ? ((double) commits / Firings).ToString("F2", CultureInfo.InvariantCulture) : "n/a")} |");
        }

        report.AppendLine();
        report.AppendLine("Verdict against #4067's gate (the shipped rounds against 4.5's, automatic MaxBatchSize):");

        Arm twoLockFree = arms.Single(x => x.Nodes == 2 && x.Automatic && x.LockFree);
        Arm twoLocked = arms.Single(x => x.Nodes == 2 && x.Automatic && !x.LockFree);
        Arm fourLockFree = arms.Single(x => x.Nodes == 4 && x.Automatic && x.LockFree);
        Arm fourLocked = arms.Single(x => x.Nodes == 4 && x.Automatic && !x.LockFree);

        double twoLockFreeRatio = twoLockFree.FiringsPerSecond / twoLocked.FiringsPerSecond;
        double fourLockFreeRatio = fourLockFree.FiringsPerSecond / fourLocked.FiringsPerSecond;
        double fourToTwo = fourLockFree.FiringsPerSecond / twoLockFree.FiringsPerSecond;
        double fourToTwoLocked = fourLocked.FiringsPerSecond / twoLocked.FiringsPerSecond;

        bool twoLockFreePasses = twoLockFreeRatio >= 1.0;
        bool fourLockFreePasses = fourLockFreeRatio >= 1.0;
        bool scalesPasses = fourToTwo >= 0.9;

        report.AppendLine("| Criterion | Measured | Pass |");
        report.AppendLine("|---|---:|---|");
        report.AppendLine(CultureInfo.InvariantCulture, $"| 2 nodes, lock-free / locked >= 1.0x | {twoLockFreeRatio:F2}x | {(twoLockFreePasses ? "yes" : "no")} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| 4 nodes, lock-free / locked >= 1.0x | {fourLockFreeRatio:F2}x | {(fourLockFreePasses ? "yes" : "no")} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| 4 nodes / 2 nodes, lock-free, >= 0.9x (within noise) | {fourToTwo:F2}x | {(scalesPasses ? "yes" : "no")} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| 4 nodes / 2 nodes, locked, for comparison | {fourToTwoLocked:F2}x | — |");
        report.AppendLine();
        report.AppendLine(twoLockFreePasses && fourLockFreePasses && scalesPasses
            ? "PASS: a round that takes no lock drains at least as fast as a locked one, and four nodes are no slower than two."
            : "FAIL: the lock-free round is slower than the locked one, or four nodes are slower than two; see the per-arm rows.");

        report.AppendLine();
        report.AppendLine("Verdict against #3862's gate (every round locked, as on 4.5):");

        Arm two1 = arms.Single(x => x.Nodes == 2 && !x.Automatic);
        Arm twoDefault = twoLocked;
        Arm four1 = arms.Single(x => x.Nodes == 4 && !x.Automatic);
        Arm fourDefault = fourLocked;

        double twoRatio = twoDefault.FiringsPerSecond / two1.FiringsPerSecond;
        double fourRatio = fourDefault.FiringsPerSecond / four1.FiringsPerSecond;
        double worstP99 = Math.Max(twoDefault.Waits.Percentile(99), fourDefault.Waits.Percentile(99));
        double smallestShare = new[] { twoDefault, fourDefault }
            .SelectMany(x => x.PerNode.Values)
            .Select(x => 100.0 * x / Firings)
            .DefaultIfEmpty(0)
            .Min();

        bool twoPasses = twoRatio >= 1.2;
        bool fourPasses = fourRatio >= 1.0;
        bool lockPasses = worstP99 < 250;
        bool sharePasses = smallestShare >= 20;

        report.AppendLine(CultureInfo.InvariantCulture, $"| Criterion | Measured | Pass |");
        report.AppendLine("|---|---:|---|");
        report.AppendLine(CultureInfo.InvariantCulture, $"| 2 nodes, automatic / 1 >= 1.2x | {twoRatio:F2}x | {(twoPasses ? "yes" : "no")} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| 4 nodes, automatic / 1 >= 1.0x | {fourRatio:F2}x | {(fourPasses ? "yes" : "no")} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| TRIGGER_ACCESS p99 < 250 ms, automatic | {worstP99:F1} ms | {(lockPasses ? "yes" : "no")} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| No node under 20 %, automatic | {smallestShare:F0} % | {(sharePasses ? "yes" : "no")} |");
        report.AppendLine();
        report.AppendLine(twoPasses && fourPasses && lockPasses && sharePasses
            ? "PASS: the clustered automatic default, the pool, pays for its lock."
            : "FAIL: the clustered automatic default should go back to one; SKIP LOCKED claiming is what batching on a cluster needs first.");

        return report.ToString();
    }

    /// <param name="Nodes">How many nodes drained the backlog.</param>
    /// <param name="MaxBatchSize">What each node's <c>MaxBatchSize</c> resolved to.</param>
    /// <param name="Automatic">Whether that was the automatic default rather than a value the arm set.</param>
    /// <param name="LockFree">Whether the rounds took the lock only when something in them needed it (4.6) or every round was locked (4.5).</param>
    /// <param name="Drain">From the instant the backlog was due to the start of its last firing.</param>
    /// <param name="PerNode">Firings by node.</param>
    /// <param name="Waits">What the store's instruments said while it drained.</param>
    /// <param name="Commits">Commits the server published over the drain.</param>
    private sealed record Arm(
        int Nodes,
        int MaxBatchSize,
        bool Automatic,
        bool LockFree,
        TimeSpan Drain,
        Dictionary<string, int> PerNode,
        LockWaits Waits,
        long? Commits)
    {
        public double FiringsPerSecond => Firings / Drain.TotalSeconds;
    }

    /// <param name="TriggerAccess">Every wait on <c>TRIGGER_ACCESS</c>, in milliseconds.</param>
    /// <param name="Rounds">How many firing rounds the nodes ran.</param>
    /// <param name="RoundsLockFree">Of those, how many said they took no lock.</param>
    /// <param name="RoundsLocked">Of those, how many said they took <c>TRIGGER_ACCESS</c>.</param>
    /// <param name="Acquired">How many triggers the rounds came back with.</param>
    private sealed record LockWaits(List<double> TriggerAccess, long Rounds, long RoundsLockFree, long RoundsLocked, long Acquired)
    {
        public double Max => TriggerAccess.Count == 0 ? 0 : TriggerAccess.Max();

        public double TriggersPerRound => Rounds == 0 ? 0 : (double) Acquired / Rounds;

        public double Percentile(double percentile)
        {
            if (TriggerAccess.Count == 0)
            {
                return 0;
            }

            double[] sorted = [.. TriggerAccess.Order()];
            int rank = (int) Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
            return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
        }
    }

    /// <summary>
    /// Listens to every node's instruments at once: the nodes are in this process, and a
    /// <see cref="MeterListener" /> sees every <c>Quartz</c> meter in it.
    /// </summary>
    private sealed class LockWaitRecorder : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly object sync = new();
        private List<double> triggerAccess = [];
        private long rounds;
        private long roundsLockFree;
        private long roundsLocked;
        private long acquired;

        public LockWaitRecorder()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == QuartzInstrumentation.MeterName
                    && instrument.Name is QuartzInstrumentation.Instruments.JobStoreLockWaitDuration
                        or QuartzInstrumentation.Instruments.TriggerAcquired
                        or QuartzInstrumentation.Instruments.TriggerAcquisitionDuration)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };

            listener.SetMeasurementEventCallback<double>(OnDouble);
            listener.SetMeasurementEventCallback<long>(OnLong);
            listener.Start();
        }

        public void Clear()
        {
            lock (sync)
            {
                triggerAccess = [];
                rounds = 0;
                roundsLockFree = 0;
                roundsLocked = 0;
                acquired = 0;
            }
        }

        public LockWaits Snapshot()
        {
            lock (sync)
            {
                return new LockWaits([.. triggerAccess], rounds, roundsLockFree, roundsLocked, acquired);
            }
        }

        public void Dispose() => listener.Dispose();

        private void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
        {
            if (instrument.Name == QuartzInstrumentation.Instruments.TriggerAcquisitionDuration)
            {
                Interlocked.Increment(ref rounds);

                // The lock the round said it ran under (#4067).
                foreach (KeyValuePair<string, object> tag in tags)
                {
                    if (tag.Key == ActivityTags.JobStoreLock)
                    {
                        if ("none".Equals(tag.Value as string, StringComparison.Ordinal))
                        {
                            Interlocked.Increment(ref roundsLockFree);
                        }
                        else if ("TRIGGER_ACCESS".Equals(tag.Value as string, StringComparison.Ordinal))
                        {
                            Interlocked.Increment(ref roundsLocked);
                        }
                    }
                }

                return;
            }

            foreach (KeyValuePair<string, object> tag in tags)
            {
                if (tag.Key == ActivityTags.JobStoreLock && "TRIGGER_ACCESS".Equals(tag.Value as string, StringComparison.Ordinal))
                {
                    lock (sync)
                    {
                        triggerAccess.Add(value * 1000);
                    }

                    return;
                }
            }
        }

        private void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
        {
            Interlocked.Add(ref acquired, value);
        }
    }

    /// <summary>
    /// Counts each firing by trigger and by node, and stamps when the last one started.
    /// </summary>
    public sealed class DrainJob : IJob
    {
        private static int count;
        private static long lastStarted;

        public static ConcurrentDictionary<string, int> Runs { get; private set; } = new(StringComparer.Ordinal);

        public static ConcurrentDictionary<string, int> PerNode { get; private set; } = new(StringComparer.Ordinal);

        public static int Count => Volatile.Read(ref count);

        public static long LastStarted => Interlocked.Read(ref lastStarted);

        public static void Reset()
        {
            Runs = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            PerNode = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            Interlocked.Exchange(ref count, 0);
            Interlocked.Exchange(ref lastStarted, 0);
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Exchange(ref lastStarted, Stopwatch.GetTimestamp());
            Runs.AddOrUpdate(context.Trigger.Key.Name, 1, static (_, runs) => runs + 1);
            PerNode.AddOrUpdate(context.Scheduler.SchedulerInstanceId, 1, static (_, runs) => runs + 1);
            Interlocked.Increment(ref count);
            return default;
        }
    }
}
