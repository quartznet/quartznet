using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics.Metrics;
using System.Globalization;

using Quartz.Diagnostics;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The property a clustered job store exists to provide, and the only one every engine has to be shown
/// to hold: two nodes, one set of due triggers, every trigger fired exactly once.
/// </summary>
/// <remarks>
/// <para>
/// It is a base of its own rather than part of <see cref="ClusteredHardeningTestBase" /> because of
/// what the two need from an engine. This case is all product code — two configured schedulers racing
/// for rows the store wrote — so it runs anywhere there is a fixture. The hardening cases write a dead
/// node's residue by hand, which means the fixture itself has to spell a boolean and a timestamp the
/// way each engine stores them, and they are carried on the two engines where that is written.
/// </para>
/// <para>
/// The split also decides what a ten-minute CI leg pays for. MySQL, Oracle and Firebird carry this one
/// case; PostgreSQL and SQL Server carry it and the residue cases both.
/// </para>
/// </remarks>
public abstract class ClusteredExactlyOnceTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterExactlyOnce";

    protected ClusteredExactlyOnceTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterExactlyOnceTest";

    [SetUp]
    public void ResetFirings() => FiringRecordingJob.Reset();

    /// <summary>
    /// The property the clustered job store exists to provide: two nodes, one set of triggers, every
    /// trigger fired exactly once. With the shipped defaults, so since 4.6 the rounds that race for these
    /// rows take no <c>TRIGGER_ACCESS</c> lock (#4067) — the claim's compare-and-swap is then all that keeps
    /// the two nodes apart — except on SQL Server, whose rounds are 4.5's in this release (#4117).
    /// </summary>
    [Test]
    public Task TwoNodes_EveryOneShotTriggerFiresExactlyOnce()
    {
        return AssertNoDoubleFire(configure: null);
    }

    /// <summary>
    /// The same race with 4.5's rounds, every one under the lock, which <c>LockFreeFiringRounds = false</c>
    /// restores: the arm an operator falls back to keeps the property too.
    /// </summary>
    [Test]
    public Task TwoNodes_EveryOneShotTriggerFiresExactlyOnceWithEveryRoundLocked()
    {
        return AssertNoDoubleFire(
            configure: properties => properties["quartz.jobStore.lockFreeFiringRounds"] = "false",
            lockedFor: "option_off");
    }

    /// <summary>
    /// Starts two contending nodes, schedules thirty one-shot triggers due at the same instant, and
    /// asserts each fired exactly once. Both nodes acquire in batches so they genuinely reach for
    /// overlapping sets of rows rather than politely taking turns, which is the only arrangement in
    /// which a broken lock or a lost <c>WHERE TRIGGER_STATE = 'WAITING'</c> guard shows up at all.
    /// </summary>
    /// <param name="configure">Adjusts each node's flat properties.</param>
    /// <param name="lockedFor">
    /// The reason every round is expected to take the lock for, as the acquisition instruments tag it, or
    /// <see langword="null" /> for the shipped expectation: rounds without the lock, and on SQL Server every
    /// round locked for <c>sql_server</c>.
    /// </param>
    protected async Task AssertNoDoubleFire(Action<NameValueCollection> configure, string lockedFor = null)
    {
        lockedFor ??= Database.AllowsLockFreeRounds ? null : "sql_server";

        const int TriggerCount = 30;

        void ConfigureNode(NameValueCollection properties)
        {
            // A node that acquires ten at a time reaches for overlapping sets of rows rather than
            // politely taking one each; the thread pool has to grow with it, because the scheduler
            // refuses a batch larger than the number of threads that could run it.
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "10";
            properties["quartz.threadPool.maxConcurrency"] = "10";
            configure?.Invoke(properties);
        }

        // Both nodes are in this process, so one listener sees every round either runs.
        using RoundLockRecorder rounds = new();

        IScheduler nodeA = await CreateScheduler("nodeA", configure: ConfigureNode);
        IScheduler nodeB = await CreateScheduler("nodeB", configure: ConfigureNode);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail job = JobBuilder.Create<FiringRecordingJob>()
                .WithIdentity("noDoubleFireJob", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            // Far enough out that every trigger is stored before any of them is due, so all thirty
            // become eligible at the same instant with both nodes awake to see them.
            DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(5);
            string[] expected = new string[TriggerCount];
            for (int i = 0; i < TriggerCount; i++)
            {
                expected[i] = "oneShot-" + i.ToString(CultureInfo.InvariantCulture);
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity(expected[i], Group)
                    .ForJob(job)
                    .StartAt(start)
                    .Build());
            }

            await WaitForCondition(
                () => Task.FromResult(FiringRecordingJob.Firings.Count >= TriggerCount),
                timeoutMs: 90_000,
                async () =>
                {
                    string[] missing = expected.Except(FiredTriggerNames()).ToArray();
                    return $"all {TriggerCount} one-shot triggers to fire; {missing.Length} never did "
                           + $"([{string.Join(", ", missing)}]). State:\n{await DumpDatabaseState()}";
                });

            // Absence cannot be polled for, only waited out: a duplicate acquisition that lost the race by
            // a few hundred milliseconds arrives after the thirtieth firing, not before it.
            await SettleForRepeatFirings();

            TestContext.Out.WriteLine("Firings per node: " + string.Join(", ", FiringRecordingJob.Firings
                .GroupBy(x => x.InstanceId)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Count()}")));
            TestContext.Out.WriteLine("Rounds by lock: " + rounds.Describe());

            FiredTriggerNames().Should().BeEquivalentTo(expected,
                "a clustered store hands each one-shot trigger to exactly one node — a repeated name means "
                + "two nodes acquired the same row, and a missing one means a row was acquired and dropped");

            if (lockedFor is null)
            {
                rounds.LockFree.Should().BeGreaterThan(0,
                    "a round of concurrent-allowed one-shots takes no lock once both nodes have checked in, so a run "
                    + "in which no round said so never exercised the lock-free path (rounds by lock: {0})", rounds.Describe());
            }
            else
            {
                rounds.LockFree.Should().Be(0, "every round is locked, for {0} (rounds by lock: {1})", lockedFor, rounds.Describe());
                rounds.Locked.Should().BeGreaterThan(0);
                rounds.LockedFor(lockedFor).Should().Be(rounds.Locked,
                    "the reason is the store's configuration, so every round gives it (rounds by lock: {0})", rounds.Describe());
            }
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Counts the firing rounds every node in this process runs, by the lock each said it ran under
    /// (#4067): the <c>quartz.jobstore.lock</c> attribute on <c>quartz.trigger.acquisition.duration</c>,
    /// and the reason for a locked one.
    /// </summary>
    protected sealed class RoundLockRecorder : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly ConcurrentDictionary<string, int> reasons = new(StringComparer.Ordinal);
        private int lockFree;
        private int locked;
        private int untagged;

        public RoundLockRecorder()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == QuartzInstrumentation.MeterName
                    && instrument.Name == QuartzInstrumentation.Instruments.TriggerAcquisitionDuration)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>(OnRound);
            listener.Start();
        }

        public int LockFree => Volatile.Read(ref lockFree);

        public int Locked => Volatile.Read(ref locked);

        /// <summary>How many rounds took the lock for <paramref name="reason" />, as the reason tag spells it.</summary>
        public int LockedFor(string reason) => reasons.TryGetValue(reason, out int count) ? count : 0;

        public string Describe()
        {
            string byReason = reasons.Count == 0
                ? ""
                : " (" + string.Join(", ", reasons.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}")) + ")";
            return $"none={LockFree}, TRIGGER_ACCESS={Locked}{byReason}, untagged={Volatile.Read(ref untagged)}";
        }

        public void Dispose() => listener.Dispose();

        private void OnRound(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
        {
            string lockName = null;
            string reason = null;
            foreach (KeyValuePair<string, object> tag in tags)
            {
                if (tag.Key == ActivityTags.JobStoreLock)
                {
                    lockName = tag.Value as string;
                }
                else if (tag.Key == ActivityTags.JobStoreLockReason)
                {
                    reason = tag.Value as string;
                }
            }

            switch (lockName)
            {
                case "none":
                    Interlocked.Increment(ref lockFree);
                    break;
                case "TRIGGER_ACCESS":
                    Interlocked.Increment(ref locked);
                    reasons.AddOrUpdate(reason ?? "unknown", 1, static (_, count) => count + 1);
                    break;
                default:
                    Interlocked.Increment(ref untagged);
                    break;
            }
        }
    }

    protected static string[] FiredTriggerNames() => FiringRecordingJob.Firings.Select(x => x.TriggerKey.Name).ToArray();

    protected Task WaitForFirings(int count, int timeoutMs, string what)
    {
        return WaitForCondition(
            () => Task.FromResult(FiringRecordingJob.Firings.Count >= count),
            timeoutMs,
            async () => $"{what}. State:\n{await DumpDatabaseState()}");
    }

    /// <summary>
    /// Gives a repeat firing time to arrive before the caller asserts there was none. Recovery is driven
    /// by the check-in loop, so a recovery that failed to clean up after itself would run again on the
    /// next check-in — three of those, at this fixture's one-second interval, pass inside this wait.
    /// </summary>
    protected static Task SettleForRepeatFirings() => Task.Delay(3000);

    protected sealed record FiringRecord(TriggerKey TriggerKey, string InstanceId, string OriginalTriggerName);

    /// <summary>
    /// Records the trigger, the node, and — for a recovered firing — the trigger whose firing is being
    /// replayed. Concurrent by design: the exactly-once property under test belongs to trigger
    /// acquisition, and <c>[DisallowConcurrentExecution]</c> would hide it behind a queue.
    /// </summary>
    protected sealed class FiringRecordingJob : IJob
    {
        private static volatile ConcurrentQueue<FiringRecord> firings = new();

        public static ConcurrentQueue<FiringRecord> Firings => firings;

        public static void Reset() => Interlocked.Exchange(ref firings, new ConcurrentQueue<FiringRecord>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            JobDataMap map = context.MergedJobDataMap;
            string originalTriggerName = map.ContainsKey(SchedulerConstants.FailedJobOriginalTriggerName)
                ? map.GetString(SchedulerConstants.FailedJobOriginalTriggerName)
                : null;

            Firings.Enqueue(new FiringRecord(context.Trigger.Key, context.Scheduler.SchedulerInstanceId, originalTriggerName));
            return default;
        }
    }
}
