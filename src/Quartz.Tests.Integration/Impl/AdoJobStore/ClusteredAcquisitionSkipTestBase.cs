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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A node acquiring one trigger at a time keeps firing its ordinary triggers on time while a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job runs on the other node (#3926).
/// </summary>
/// <remarks>
/// <para>
/// With <c>batchTriggerAcquisitionMaxCount</c> 1 — the default — the earliest due row a node read
/// belonged to a serial job executing on the other node. That row was <c>WAITING</c> because the node's
/// own reservation of it had been released to <c>WAITING</c> after the other node's fire had blocked it;
/// every acquisition then read that one row, skipped it as executing, and gave up, and the node idled
/// for its whole idle wait while its own triggers came due behind it. Firings arrived seconds late and
/// then in a burst.
/// </para>
/// <para>
/// <b>Both nodes are in this process</b>, so a firing on either is seen by the same static record.
/// The contention is induced: two triggers of a concurrent job are due every fifth of a second, which
/// keeps both loops acquiring rather than sleeping between firings, and several triggers of one short
/// serial job are due every second, so whenever the job is between executions both nodes reserve one
/// of them and one of the two loses to the other's fire — the interleaving the issue describes, many
/// times over a run. Each node also owns a trigger pinned to it and due every second, which is what is
/// measured: how late each of its firings was against the time it was scheduled for.
/// </para>
/// <para>
/// The idle wait is set long enough that a node idling behind a skipped row is unmistakable, and the
/// tolerance is half of it; with the fix reverted every firing after the first collision is late by the
/// idle wait.
/// </para>
/// <para>
/// Uses the assembly-wide database of the dialect a derived fixture names, with
/// <see cref="SchedulerName" /> as the only isolation axis within it.
/// </para>
/// </remarks>
[NonParallelizable]
public abstract class ClusteredAcquisitionSkipTestBase
{
    private const string SchedulerName = "ClusterAcquisitionSkipTest";
    private const string Group = "clusterAcquisitionSkip";
    private const string NodeA = "skip-node-a";
    private const string NodeB = "skip-node-b";
    private const int SerialTriggerCount = 6;

    /// <summary>
    /// Ten firings a second between them. Main runs four; at twenty firings a second, 3.x on a SQL
    /// Server container waited on locks long enough to make both nodes up to five seconds late at once,
    /// with no deadlock, which is not what this measures.
    /// </summary>
    private const int BusyTriggerCount = 2;

    /// <summary>How long both nodes run once everything is scheduled.</summary>
    private static readonly TimeSpan Observation = TimeSpan.FromSeconds(15);

    /// <summary>How late a pinned firing may be and still count as on time: half the idle wait.</summary>
    /// <remarks>
    /// Main allows a second. With the fix reverted a node here fired 7.3 to 9.0 s late at worst; with it,
    /// a fresh SQL Server container still stalled a node, or both at once, for up to 2.8 s with nothing
    /// logged, which is not what this measures.
    /// </remarks>
    private static readonly TimeSpan LateAllowed = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many firings of a trigger due every second each node has to show for the observation: the
    /// rest is start-up, the first acquisition and the shutdown.
    /// </summary>
    private static readonly int FiringsExpected = (int) Observation.TotalSeconds - 4;

    /// <summary>The <c>quartz.dataSource.default.provider</c> of the dialect.</summary>
    protected abstract string Provider { get; }

    /// <summary>The <c>quartz.jobStore.driverDelegateType</c> of the dialect.</summary>
    protected abstract string DriverDelegateType { get; }

    /// <summary>The assembly-wide database of the dialect.</summary>
    protected abstract string ConnectionString { get; }

    /// <summary>A connection to <see cref="ConnectionString" />, for the clean-up.</summary>
    protected abstract DbConnection CreateConnection();

    [SetUp]
    public void ResetRecords()
    {
        PinnedJob.Reset();
        SerialJob.Reset();
    }

    [TearDown]
    public async Task CleanUpDatabaseState()
    {
        // A clustered node's own SCHEDULER_STATE row survives Shutdown, and the triggers here repeat
        // forever, so every row of this fixture's scheduler goes, one statement per round trip.
        string[] tables =
        {
            "QRTZ_FIRED_TRIGGERS", "QRTZ_SIMPLE_TRIGGERS", "QRTZ_CRON_TRIGGERS", "QRTZ_SIMPROP_TRIGGERS",
            "QRTZ_BLOB_TRIGGERS", "QRTZ_TRIGGERS", "QRTZ_JOB_DETAILS", "QRTZ_CALENDARS",
            "QRTZ_PAUSED_TRIGGER_GRPS", "QRTZ_SCHEDULER_STATE",
        };

        using DbConnection connection = CreateConnection();
        await connection.OpenAsync();
        foreach (string table in tables)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM " + table + " WHERE SCHED_NAME = @schedulerName";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "schedulerName";
            parameter.Value = SchedulerName;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task ANodeAcquiringOneTriggerAtATimeFiresItsOwnTriggersOnTimeWhileASerialJobRunsElsewhere()
    {
        IScheduler nodeA = await CreateScheduler(NodeA);
        IScheduler nodeB = await CreateScheduler(NodeB);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail serialJob = JobBuilder.Create<SerialJob>()
                .WithIdentity("serial", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(serialJob, replace: true);

            IJobDetail pinnedJob = JobBuilder.Create<PinnedJob>()
                .WithIdentity("pinned", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(pinnedJob, replace: true);

            // One trigger per node, pinned to it, due every second: the firings whose lateness is
            // measured. Each is scheduled through its own node, because a scheduling change only wakes
            // the node it was made on: a node started with nothing due sleeps out its idle wait, and
            // that would be measured here as ten seconds of lateness that has nothing to do with the
            // serial job.
            foreach ((IScheduler node, string instanceId) in new[] { (nodeA, NodeA), (nodeB, NodeB) })
            {
                await node.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity("pinned-" + instanceId, Group)
                    .ForJob(pinnedJob)
                    .WithPreferredNode(instanceId)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                    .Build());
            }

            // Work that keeps both loops acquiring: a concurrent job due every fifth of a second, on
            // whichever node gets there first. A loop that only woke for its pinned trigger would be
            // asleep for the few milliseconds in which the serial job's rows are there to be reserved.
            IJobDetail busyJob = JobBuilder.Create<BusyJob>()
                .WithIdentity("busy", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(busyJob, replace: true);
            for (int i = 0; i < BusyTriggerCount; i++)
            {
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity($"busy-{i}", Group)
                    .ForJob(busyJob)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMilliseconds(200)).RepeatForever())
                    .Build());
            }

            // Several triggers of the serial job, all due every second and all overdue from the moment
            // the job's execution unblocks them, so that both nodes reserve one whenever the job is
            // between executions and one of them loses to the other's fire.
            for (int i = 0; i < SerialTriggerCount; i++)
            {
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity($"serial-{i}", Group)
                    .ForJob(serialJob)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                    .Build());
            }

            await Task.Delay(Observation);
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }

        List<PinnedFiring> firings = PinnedJob.Firings.ToList();
        string report = Report(firings);
        TestContext.Out.WriteLine(report);
        TestContext.Out.WriteLine($"Serial job: {SerialJob.Completed} completed, peak concurrency {SerialJob.PeakConcurrency}, "
                                  + "on " + string.Join(", ", SerialJob.Nodes.GroupBy(x => x, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Count()}")));

        SerialJob.Completed.Should().BeGreaterThan(1, "the premise: the serial job was running throughout");
        SerialJob.PeakConcurrency.Should().Be(1, "the fix must not buy liveness by letting the serial job overlap itself");

        foreach (string node in new[] { NodeA, NodeB })
        {
            List<PinnedFiring> onNode = firings.FindAll(x => x.Node == node);

            onNode.Count.Should().BeGreaterThanOrEqualTo(FiringsExpected,
                "'{0}' owns a trigger due every second, and a node that idles behind a row it skipped fires nothing while it idles:\n{1}",
                node, report);
            onNode.Max(x => x.Late).Should().BeLessThanOrEqualTo(LateAllowed,
                "a node reading one trigger at a time must read past the serial job's rows while the job runs elsewhere, "
                + "rather than read the first of them until its retries run out and wait out its idle time:\n{0}",
                report);
        }
    }

    private async Task<IScheduler> CreateScheduler(string instanceId)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = SchedulerName,
            ["quartz.scheduler.instanceId"] = instanceId,
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "1",
            // Long enough that idling behind a skipped row shows as seconds of lateness, and short
            // enough that the run is not.
            ["quartz.scheduler.idleWaitTime"] = "10000",
            ["quartz.threadPool.threadCount"] = "4",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.driverDelegateType"] = DriverDelegateType,
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.clusterCheckinInterval"] = "1000",
            ["quartz.jobStore.clusterCheckinMisfireThreshold"] = "2000",
            ["quartz.dataSource.default.provider"] = Provider,
            ["quartz.dataSource.default.connectionString"] = ConnectionString,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        StdSchedulerFactory factory = new StdSchedulerFactory(properties);
        IScheduler scheduler = await factory.GetScheduler();

        // Cluster nodes share the scheduler name, and the repository's lookup is name-only: a second
        // call would hand back the first node. Unbinding each one makes every call build its own.
        SchedulerRepository.Instance.Remove(SchedulerName, scheduler.SchedulerInstanceId);

        return scheduler;
    }

    private static string Report(List<PinnedFiring> firings)
    {
        return string.Join("\n", firings
            .GroupBy(x => x.Node, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} firings, {2:F0} ms late on average, {3:F0} ms at worst",
                x.Key,
                x.Count(),
                x.Average(f => f.Late.TotalMilliseconds),
                x.Max(f => f.Late.TotalMilliseconds))));
    }

    public sealed class PinnedFiring
    {
        public PinnedFiring(string node, TimeSpan late)
        {
            Node = node;
            Late = late;
        }

        public string Node { get; }

        public TimeSpan Late { get; }
    }

    /// <summary>
    /// Records, for every firing, the node it ran on and how far behind its scheduled time it was.
    /// </summary>
    public sealed class PinnedJob : IJob
    {
        private static volatile ConcurrentQueue<PinnedFiring> firings = new ConcurrentQueue<PinnedFiring>();

        public static ConcurrentQueue<PinnedFiring> Firings => firings;

        public static void Reset() => Interlocked.Exchange(ref firings, new ConcurrentQueue<PinnedFiring>());

        public Task Execute(IJobExecutionContext context)
        {
            TimeSpan late = DateTimeOffset.UtcNow - (context.ScheduledFireTimeUtc ?? context.FireTimeUtc);
            Firings.Enqueue(new PinnedFiring(context.Scheduler.SchedulerInstanceId, late));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Does nothing, on either node; the churn that keeps both loops awake.
    /// </summary>
    public sealed class BusyJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    /// <summary>
    /// Runs long enough that a node which lost its reservation to this execution reads the store again
    /// while it is still running, and short enough that its triggers are reserved many times over the
    /// run; records that it never ran twice at once.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        private static int running;
        private static int peak;
        private static int completed;
        private static volatile ConcurrentQueue<string> nodes = new ConcurrentQueue<string>();

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

        public async Task Execute(IJobExecutionContext context)
        {
            int inside = Interlocked.Increment(ref running);
            RecordPeak(inside);
            Nodes.Enqueue(context.Scheduler.SchedulerInstanceId);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), context.CancellationToken).ConfigureAwait(false);
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
