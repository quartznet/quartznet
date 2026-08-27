using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

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
    /// trigger fired exactly once.
    /// </summary>
    [Test]
    public Task TwoNodes_EveryOneShotTriggerFiresExactlyOnce()
    {
        return AssertNoDoubleFire(configure: null);
    }

    /// <summary>
    /// Starts two contending nodes, schedules thirty one-shot triggers due at the same instant, and
    /// asserts each fired exactly once. Both nodes acquire in batches so they genuinely reach for
    /// overlapping sets of rows rather than politely taking turns, which is the only arrangement in
    /// which a broken lock or a lost <c>WHERE TRIGGER_STATE = 'WAITING'</c> guard shows up at all.
    /// </summary>
    protected async Task AssertNoDoubleFire(Action<NameValueCollection> configure)
    {
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

            FiredTriggerNames().Should().BeEquivalentTo(expected,
                "a clustered store hands each one-shot trigger to exactly one node — a repeated name means "
                + "two nodes acquired the same row, and a missing one means a row was acquired and dropped");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
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
