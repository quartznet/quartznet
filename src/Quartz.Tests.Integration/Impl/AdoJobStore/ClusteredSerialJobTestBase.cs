using System.Collections.Concurrent;
using System.Collections.Specialized;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A <see cref="DisallowConcurrentExecutionAttribute" /> job on two live clustered nodes, under
/// contention, never overlapping itself.
/// </summary>
/// <remarks>
/// <para>
/// The attribute is what a job says when running twice at once would corrupt something, and on a
/// cluster it is the job store that has to honour it: the two nodes share nothing but rows, and the
/// block is <c>BLOCKED</c> trigger states written under the trigger-access lock. Until this fixture
/// nothing here asserted it — <c>ClusteredExactlyOnceTestBase</c> says in as many words that its own
/// job is concurrent by design, and the only fixture that watched for an overlap was the half-hour
/// soak, which is a release gate rather than a CI leg.
/// </para>
/// <para>
/// <b>Both nodes are in this process</b>, which is what makes the detector possible: a static counter
/// sees a firing on either node, so "these two firings overlapped" is observed rather than inferred
/// from rows afterwards. The contention is induced rather than hoped for — several triggers of the one
/// job all become due at the same instant, with both nodes awake and acquiring in batches, so both
/// reach for the same rows.
/// </para>
/// </remarks>
public abstract class ClusteredSerialJobTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterSerial";
    private const int TriggerCount = 6;

    protected ClusteredSerialJobTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterSerialJobTest";

    [SetUp]
    public void ResetOverlapDetector() => SerialJob.Reset();

    [Test]
    public async Task ASerialJobNeverOverlapsItselfAcrossTwoNodes()
    {
        static void ConfigureNode(NameValueCollection properties)
        {
            // Batched acquisition with room to run what it acquires, so both nodes genuinely reach for
            // the same due rows rather than taking one each in turn.
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "6";
            properties["quartz.threadPool.maxConcurrency"] = "6";
        }

        IScheduler nodeA = await CreateScheduler("serialNodeA", configure: ConfigureNode);
        IScheduler nodeB = await CreateScheduler("serialNodeB", configure: ConfigureNode);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail job = JobBuilder.Create<SerialJob>()
                .WithIdentity("serialJob", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            // Far enough out that every trigger is stored before any is due, so all of them become
            // eligible at one instant with both nodes awake to see them.
            DateTimeOffset due = DateTimeOffset.UtcNow.AddSeconds(5);
            for (int i = 0; i < TriggerCount; i++)
            {
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity($"serial-{i}", Group)
                    .ForJob(job)
                    .StartAt(due)
                    .Build());
            }

            await WaitForCondition(
                () => Task.FromResult(SerialJob.Completed >= TriggerCount),
                timeoutMs: 120_000,
                async () => $"all {TriggerCount} firings of the serial job to complete; {SerialJob.Completed} did. "
                            + $"State:\n{await DumpDatabaseState()}");

            SerialJob.PeakConcurrency.Should().Be(1,
                "[DisallowConcurrentExecution] is the job saying two of it must never run at once, and on a "
                + "cluster the store is the only thing that can hold that — the nodes share nothing but rows");

            SerialJob.Nodes.Should().HaveCountGreaterThan(0);
            TestContext.Out.WriteLine("Firings per node: " + string.Join(", ", SerialJob.Nodes
                .GroupBy(x => x, StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Count()}")));
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Holds a slot long enough for an overlap to be visible, and records the highest number of firings
    /// that were ever inside it at once.
    /// </summary>
    [DisallowConcurrentExecution]
    private sealed class SerialJob : IJob
    {
        private static int running;
        private static int peak;
        private static int completed;
        private static volatile ConcurrentQueue<string> nodes = new();

        public static int PeakConcurrency => Volatile.Read(ref peak);

        public static int Completed => Volatile.Read(ref completed);

        public static ConcurrentQueue<string> Nodes => nodes;

        public static void Reset()
        {
            Interlocked.Exchange(ref running, 0);
            Interlocked.Exchange(ref peak, 0);
            Interlocked.Exchange(ref completed, 0);
            Interlocked.Exchange(ref nodes, new ConcurrentQueue<string>());
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            int inside = Interlocked.Increment(ref running);
            RecordPeak(inside);
            Nodes.Enqueue(context.Scheduler.SchedulerInstanceId);

            try
            {
                // Long enough that a second firing acquired anywhere in the cluster would land inside
                // this window, and short enough that six of them take a handful of seconds.
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref running);
                Interlocked.Increment(ref completed);
            }
        }

        private static void RecordPeak(int inside)
        {
            int observed = Volatile.Read(ref peak);
            while (inside > observed)
            {
                int previous = Interlocked.CompareExchange(ref peak, inside, observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }
}
