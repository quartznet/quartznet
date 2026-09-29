using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Quartz.Core;
using Quartz.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The gate a clustered store's automatic <c>MaxBatchSize</c> passed before it tracked the pool (#3862,
/// #3900): two and four nodes draining two thousand one-offs due at one instant, one trigger a round
/// against the automatic default, and what the cluster-wide lock does to that.
/// </summary>
/// <remarks>
/// <para>
/// <b>A measurement, not a CI leg.</b> <c>LongRunning</c> keeps it out of every integration leg
/// (<c>build/Build.cs</c>, <c>GetTestFilter</c>). It asserts only that every one-off ran exactly once;
/// the verdict is written to <c>TestContext.Out</c>, so run it with detailed output, and
/// against a PostgreSQL at its shipped durability — the Testcontainers one runs with <c>fsync</c> off,
/// where a commit costs nothing and a batch has nothing to save:
/// </para>
/// <code>
/// $env:QUARTZ_TEST_DATABASE = 'basic'   # start no container
/// $env:PG_CONNECTION_STRING = 'Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
/// dotnet test --project src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj `
///   --filter 'FullyQualifiedName~ClusteredOneOffDrainPostgresTest' `
///   --output Detailed
/// </code>
/// <para>
/// <b>What passes.</b> The default's drain on two nodes is at least 1.2 times as fast as one trigger a
/// round; on four nodes it is not slower; the <c>TRIGGER_ACCESS</c> wait's 99th percentile stays under
/// 250 ms; and no node takes less than a fifth of the work. #3900 passed it with margin once completions
/// left the lock (#3863), which is what made the clustered default the pool. A fail says that default
/// should go back to one, and points at <c>SKIP LOCKED</c> claiming as what batching on a cluster needs
/// first.
/// </para>
/// <para>
/// <b>Why one trigger a round is lock-free and the pool is not.</b> The ADO store takes
/// <c>TRIGGER_ACCESS</c> for an acquisition that asks for more than one trigger, or when
/// <c>AcquireTriggersWithinLock</c> is on, which it is not by default. So the batched arm buys fewer
/// rounds and pays the cluster-wide row lock for each, and whether that nets out is the question.
/// </para>
/// <para>
/// <b>The batched arm sets nothing.</b> It is the automatic default, so the gate measures what ships,
/// and it asserts that each node resolved that default to the pool and acquired more than one trigger a
/// round.
/// </para>
/// <para>
/// All nodes are in this process, as in <see cref="ClusteredSoakTestBase" />: they share a thread pool and
/// a GC heap, which is the way this is not a four-machine cluster.
/// </para>
/// </remarks>
[Category("db-postgres")]
[Category("LongRunning")]
[NonParallelizable]
public sealed class ClusteredOneOffDrainPostgresTest : ClusteredPostgresTestBase
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

    protected override string SchedulerName => "ClusterOneOffDrain";

    [SetUp]
    public void ResetLedger() => DrainJob.Reset();

    [Test]
    public async Task TwoAndFourNodesDrainABacklog_OneTriggerARoundAgainstTheDefault()
    {
        List<Arm> arms = [];
        foreach (int nodes in (int[]) [2, 4])
        {
            // One is 4.2's behaviour, set explicitly; null sets nothing, which is what ships.
            foreach (int? batch in (int?[]) [1, null])
            {
                arms.Add(await Drain(nodes, batch));
                await CleanUpDatabaseState();
            }
        }

        TestContext.Out.WriteLine(Report(arms, await Durability()));
    }

    /// <summary>
    /// What a commit costs this server, which decides what a batch saves.
    /// </summary>
    /// <remarks>
    /// Testcontainers starts PostgreSQL with <c>fsync</c> and <c>synchronous_commit</c> off, so a commit
    /// there costs next to nothing and the gate would be weighing a batch's lock against savings that
    /// do not exist. Run it against a server at its shipped durability for a verdict that means
    /// anything: set <c>QUARTZ_TEST_DATABASE=basic</c>, so no container starts, and
    /// <c>PG_CONNECTION_STRING</c> to a database created from <c>database/tables/tables_postgres.sql</c>.
    /// </remarks>
    private async Task<string> Durability()
    {
        await using NpgsqlConnection connection = new(Database.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand fsync = new("SHOW fsync", connection);
        string fsyncValue = (string) await fsync.ExecuteScalarAsync();
        await using NpgsqlCommand synchronousCommit = new("SHOW synchronous_commit", connection);
        string synchronousCommitValue = (string) await synchronousCommit.ExecuteScalarAsync();

        return $"fsync = {fsyncValue}, synchronous_commit = {synchronousCommitValue}";
    }

    /// <param name="nodeCount">How many nodes drain the backlog.</param>
    /// <param name="configuredBatchSize">The <c>MaxBatchSize</c> to set, or <see langword="null" /> to set nothing.</param>
    private async Task<Arm> Drain(int nodeCount, int? configuredBatchSize)
    {
        DrainJob.Reset();

        void Configure(NameValueCollection properties)
        {
            properties["quartz.threadPool.maxConcurrency"] = PoolSize.ToString(CultureInfo.InvariantCulture);
            if (configuredBatchSize is int explicitBatchSize)
            {
                properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = explicitBatchSize.ToString(CultureInfo.InvariantCulture);
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
            long commitsBefore = await PublishedCommits();
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

            long commits = await PublishedCommits() - commitsBefore;

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

            return new Arm(
                nodeCount,
                maxBatchSize,
                configuredBatchSize is null,
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
    /// <c>xact_commit</c> once every backend has published it.
    /// </summary>
    /// <remarks>
    /// PostgreSQL 15 publishes a backend's commits when it goes idle, at most once a second, and holds them
    /// for ten seconds when it goes idle within a second of its last publication (#3861). A backend
    /// publishes as it exits, so the pools are cleared and the read waits for the backends to go.
    /// </remarks>
    private async Task<long> PublishedCommits()
    {
        await using NpgsqlConnection connection = new(Database.ConnectionString);
        await connection.OpenAsync();

        List<int> backends = [];
        await using (NpgsqlCommand command = new(
                         "SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND backend_type = 'client backend'",
                         connection))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                backends.Add(reader.GetInt32(0));
            }
        }

        NpgsqlConnection.ClearAllPools();

        if (backends.Count > 0)
        {
            string remaining = "SELECT count(*) FROM pg_stat_activity WHERE pid IN ("
                               + string.Join(",", backends.Select(pid => pid.ToString(CultureInfo.InvariantCulture))) + ")";
            DateTimeOffset giveUp = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < giveUp)
            {
                await using NpgsqlCommand count = new(remaining, connection);
                if (Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                {
                    break;
                }

                await Task.Delay(20);
            }
        }

        await using NpgsqlCommand commits = new("SELECT xact_commit FROM pg_stat_database WHERE datname = current_database()", connection);
        return Convert.ToInt64(await commits.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task WaitUntil(DateTimeOffset instant)
    {
        TimeSpan remaining = instant - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    private static string Report(List<Arm> arms, string durability)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Clustered one-off drain, {Firings} due at one instant, pool {PoolSize} a node, PostgreSQL with {durability}.");
        report.AppendLine();
        report.AppendLine("| Nodes | MaxBatchSize | Drain (s) | Firings/s | Share per node | TRIGGER_ACCESS waits | p50 (ms) | p99 (ms) | Max (ms) | Rounds | Triggers/round | Commits/firing |");
        report.AppendLine("|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|");

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
                $"| {arm.Nodes} | {batch} | {arm.Drain.TotalSeconds:F2} | {arm.FiringsPerSecond:F1} | {shares} | "
                + $"{waits.TriggerAccess.Count:N0} | {waits.Percentile(50):F1} | {waits.Percentile(99):F1} | {waits.Max:F1} | "
                + $"{waits.Rounds:N0} | {waits.TriggersPerRound:F2} | {(double) arm.Commits / Firings:F2} |");
        }

        report.AppendLine();
        report.AppendLine("Verdict against #3862's gate:");

        Arm two1 = arms.Single(x => x.Nodes == 2 && !x.Automatic);
        Arm twoDefault = arms.Single(x => x.Nodes == 2 && x.Automatic);
        Arm four1 = arms.Single(x => x.Nodes == 4 && !x.Automatic);
        Arm fourDefault = arms.Single(x => x.Nodes == 4 && x.Automatic);

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
    /// <param name="Drain">From the instant the backlog was due to the start of its last firing.</param>
    /// <param name="PerNode">Firings by node.</param>
    /// <param name="Waits">What the store's instruments said while it drained.</param>
    /// <param name="Commits">Commits the server published over the drain.</param>
    private sealed record Arm(
        int Nodes,
        int MaxBatchSize,
        bool Automatic,
        TimeSpan Drain,
        Dictionary<string, int> PerNode,
        LockWaits Waits,
        long Commits)
    {
        public double FiringsPerSecond => Firings / Drain.TotalSeconds;
    }

    private sealed record LockWaits(List<double> TriggerAccess, long Rounds, long Acquired)
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
                acquired = 0;
            }
        }

        public LockWaits Snapshot()
        {
            lock (sync)
            {
                return new LockWaits([.. triggerAccess], rounds, acquired);
            }
        }

        public void Dispose() => listener.Dispose();

        private void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
        {
            if (instrument.Name == QuartzInstrumentation.Instruments.TriggerAcquisitionDuration)
            {
                Interlocked.Increment(ref rounds);
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
