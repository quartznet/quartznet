#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Collections.Concurrent;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A trigger's overlap policy across two nodes sharing one database: what one node decides about a
/// firing that runs on the other.
/// </summary>
/// <remarks>
/// SQLite refuses clustering, so the unit suite can only share a database between two unclustered
/// stores; this is the same claim with both nodes started, checking in and acquiring on their own.
/// </remarks>
public abstract class ClusteredOverlapPolicyTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterOverlap";
    private const string NodeA = "overlapNodeA";
    private const string NodeB = "overlapNodeB";

    protected ClusteredOverlapPolicyTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterOverlapTest";

    [SetUp]
    public void ResetJob() => HeldJob.Reset();

    [TearDown]
    public void LetGo() => HeldJob.Stop();

    /// <summary>
    /// A firing that comes due on another node starts at once and asks the node running the previous one,
    /// through the store, to interrupt it (#4071): that node cancels it after its next check-in.
    /// </summary>
    [Test]
    public async Task CancelPreviousStartsOnAnotherNodeAndThatFiringIsInterruptedThroughTheStore()
    {
        IScheduler nodeA = await CreateScheduler(NodeA);
        IScheduler nodeB = await CreateScheduler(NodeB);
        TriggerKey triggerKey = new("replacing", Group);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail job = JobBuilder.Create<HeldJob>().WithIdentity("replacing", Group).StoreDurably().Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            // Pinned to A for its first firing, so which node runs it is an observation. Six seconds apart,
            // so the next occurrence is outside A's two-second acquisition window when the pin moves to B;
            // A acquires ahead, and an occurrence it had already reserved would fire there.
            await nodeA.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .WithPreferredNode(PreferredNode.For(NodeA))
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(2))
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(6)).RepeatForever())
                .WithOverlapPolicy(OverlapPolicy.CancelPrevious)
                .Build());

            await WaitForCondition(
                () => Task.FromResult(HeldJob.Started.Count >= 1),
                timeoutMs: 60_000,
                async () => $"the first firing to start on {NodeA}. State:\n{await DumpDatabaseState()}");

            (string first, string firstNode) = HeldJob.Started.Single();
            firstNode.Should().Be(NodeA);

            // From here only B acquires the trigger, so the next firing comes due on the node that does not
            // run the previous one.
            await nodeA.UpdateTriggerDetails(triggerKey, new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.For(NodeB)));

            await WaitForCondition(
                () => Task.FromResult(HeldJob.Started.Count >= 2),
                timeoutMs: 60_000,
                async () => $"the next firing to start on {NodeB}. State:\n{await DumpDatabaseState()}");

            HeldJob.Started.Skip(1).First().Node.Should().Be(NodeB,
                "the firing that came due on B starts at once rather than waiting for A's to end");
            (await IsBlocked(triggerKey)).Should().BeFalse("a firing that can be interrupted is not waited for");

            // A checks in every second, and reads the request after its next check-in.
            await WaitForCondition(
                () => Task.FromResult(HeldJob.Cancelled.Contains(first)),
                timeoutMs: 10_000,
                async () => $"{NodeA} to interrupt the firing B replaced. State:\n{await DumpDatabaseState()}");
        }
        finally
        {
            HeldJob.Stop();
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Every occurrence that comes due while the firing runs is dropped once, by whichever node acquired
    /// it, and never by both.
    /// </summary>
    [Test]
    public async Task SkipDropsEachOverlappingOccurrenceOnceClusterWide()
    {
        IScheduler nodeA = await CreateScheduler(NodeA);
        IScheduler nodeB = await CreateScheduler(NodeB);
        TriggerKey triggerKey = new("skipping", Group);
        SkipRecorder skips = new();

        try
        {
            nodeA.ListenerManager.AddTriggerListener(skips, Matchers.Key(triggerKey));
            nodeB.ListenerManager.AddTriggerListener(skips, Matchers.Key(triggerKey));

            await nodeA.Start();
            await nodeB.Start();

            IJobDetail job = JobBuilder.Create<HeldJob>().WithIdentity("skipping", Group).StoreDurably().Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            await nodeA.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(2))
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                .WithOverlapPolicy(OverlapPolicy.Skip)
                .Build());

            await WaitForCondition(
                () => Task.FromResult(skips.Count >= 4),
                timeoutMs: 60_000,
                async () => $"four occurrences to be skipped while the first firing is held. Skipped: {skips.Count}. "
                            + $"State:\n{await DumpDatabaseState()}");

            HeldJob.Started.Should().ContainSingle("Skip starts nothing beside the running firing, on either node");
            skips.ScheduledTimes.Should().OnlyHaveUniqueItems(
                "each occurrence is acquired by one node, under the lock, and dropped once");
            skips.Nodes.Should().NotBeEmpty();
        }
        finally
        {
            HeldJob.Stop();
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A simple trigger measured from completion (#4140) never runs on both nodes at once, and each run
    /// starts an interval after the previous one completed, on whichever node ran either.
    /// </summary>
    [Test]
    public async Task AnIntervalMeasuredFromCompletionNeverRunsOnBothNodesAndIsMeasuredFromWhereverItEnded()
    {
        IScheduler nodeA = await CreateScheduler(NodeA);
        IScheduler nodeB = await CreateScheduler(NodeB);
        TriggerKey triggerKey = new("fixed-delay", Group);
        TimedJob.Reset();

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail job = JobBuilder.Create<TimedJob>().WithIdentity("fixed-delay", Group).StoreDurably().Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            await nodeA.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(2))
                .WithSimpleSchedule(x => x.WithInterval(TimedJob.Interval).RepeatForever().MeasureIntervalFromCompletion())
                .Build());

            await WaitForCondition(
                () => Task.FromResult(TimedJob.Runs.Count >= 5),
                timeoutMs: 60_000,
                async () => $"five runs. Runs: {TimedJob.Runs.Count}. State:\n{await DumpDatabaseState()}");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: true);
            await nodeB.Shutdown(waitForJobsToComplete: true);
        }

        List<(TimeSpan Start, TimeSpan End, string Node)> runs = [.. TimedJob.Runs.OrderBy(x => x.Start)];
        TimedJob.MaxConcurrent.Should().Be(1, "the trigger is held while a firing of it runs, on every node");

        for (int i = 1; i < runs.Count; i++)
        {
            (runs[i].Start - runs[i - 1].End).Should().BeGreaterThanOrEqualTo(TimedJob.Interval - TimeSpan.FromMilliseconds(20),
                $"run {i + 1} on {runs[i].Node} is due an interval after run {i} completed on {runs[i - 1].Node}");
        }
    }

    private async Task<bool> IsBlocked(TriggerKey key)
    {
        return await CountRows(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name AND TRIGGER_GROUP = @group AND TRIGGER_STATE = 'BLOCKED'",
            ("schedulerName", SchedulerName),
            ("name", key.Name),
            ("group", key.Group)) == 1;
    }

    /// <summary>
    /// Records every firing the overlap policy dropped, on whichever node dropped it.
    /// </summary>
    private sealed class SkipRecorder : ITriggerListener
    {
        private readonly ConcurrentQueue<(DateTimeOffset? Scheduled, string Node)> skipped = new();

        public string Name => nameof(SkipRecorder);

        public int Count => skipped.Count;

        public IReadOnlyList<DateTimeOffset?> ScheduledTimes => skipped.Select(x => x.Scheduled).ToList();

        public IReadOnlyList<string> Nodes => skipped.Select(x => x.Node).Distinct().ToList();

        public ValueTask TriggerSkipped(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            skipped.Enqueue((trigger.NextFireTimeUtc, scheduler.SchedulerInstanceId));
            return default;
        }
    }

    /// <summary>
    /// Runs for half a second, recording when and where on one monotonic clock, and how many ran at once.
    /// </summary>
    public sealed class TimedJob : IJob
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        private static readonly long origin = TimeProvider.System.GetTimestamp();
        private static int running;
        private static int maxConcurrent;

        public static ConcurrentQueue<(TimeSpan Start, TimeSpan End, string Node)> Runs { get; private set; } = new();

        public static int MaxConcurrent => Volatile.Read(ref maxConcurrent);

        public static void Reset()
        {
            Runs = new ConcurrentQueue<(TimeSpan, TimeSpan, string)>();
            running = 0;
            maxConcurrent = 0;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            TimeSpan start = TimeProvider.System.GetElapsedTime(origin);
            int now = Interlocked.Increment(ref running);
            int seen;
            while (now > (seen = Volatile.Read(ref maxConcurrent)) && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen)
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref running);
                Runs.Enqueue((start, TimeProvider.System.GetElapsedTime(origin), context.Scheduler.SchedulerInstanceId));
            }
        }
    }

    /// <summary>
    /// Runs until it is let go, interrupted, or the test ends, and records where it ran.
    /// </summary>
    public sealed class HeldJob : IJob
    {
        private static CancellationTokenSource stop = new();
        private static SemaphoreSlim letGo = new(0);

        public static ConcurrentQueue<(string FireInstanceId, string Node)> Started { get; private set; } = new();

        public static ConcurrentQueue<string> Cancelled { get; private set; } = new();

        public static void Reset()
        {
            stop = new CancellationTokenSource();
            letGo = new SemaphoreSlim(0);
            Started = new ConcurrentQueue<(string, string)>();
            Cancelled = new ConcurrentQueue<string>();
        }

        public static void LetOneGo() => letGo.Release();

        public static void Stop() => stop.Cancel();

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Started.Enqueue((context.FireInstanceId, context.Scheduler.SchedulerInstanceId));
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);

            try
            {
                await letGo.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.Enqueue(context.FireInstanceId);
            }
            catch (OperationCanceledException)
            {
                // The test is over.
            }
        }
    }
}
