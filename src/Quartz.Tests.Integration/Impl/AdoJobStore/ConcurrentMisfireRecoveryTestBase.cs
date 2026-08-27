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

using System.Globalization;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two nodes sweeping for misfires at the same moment, over one database. Every misfired trigger has
/// to be recovered exactly once: recovering it twice would apply its misfire policy twice, and
/// recovering it not at all would leave it stuck behind its own fire time forever.
/// </summary>
/// <remarks>
/// <para>
/// The nodes here are job stores rather than schedulers, and they are initialized but never started —
/// <c>SchedulerStarted()</c> is what spawns the <c>MisfireHandler</c> loop, and a loop sweeping on its
/// own schedule is precisely the thing that would make "exactly once" unobservable. The stores are
/// built with no lock handler so that each picks the one its own dialect calls for, which is the whole
/// mechanism under test: on PostgreSQL that is <c>PostgreSqlSelectForUpdateSemaphore</c>, and the
/// second node's sweep has to wait behind the first node's row lock and then find nothing left to do.
/// </para>
/// <para>
/// Real time, per <see cref="ClusteredJobStoreTestBase" />: the nodes share a database rather than a
/// clock. Nothing waits, because the triggers are stored twelve hours overdue rather than becoming
/// overdue while the test watches.
/// </para>
/// </remarks>
public abstract class ConcurrentMisfireRecoveryTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "concurrentMisfireRecovery";
    private const int TriggerCount = 50;

    /// <summary>Half of the triggers' one-day period; see <c>MisfireMatrixCases</c> for why.</summary>
    private static readonly TimeSpan HalfPeriod = TimeSpan.FromHours(12);

    private readonly List<LocalTransactionJobStore> nodes = [];

    protected ConcurrentMisfireRecoveryTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ConcurrentMisfireRecoveryTest";

    [TearDown]
    public async Task ShutDownNodes()
    {
        foreach (LocalTransactionJobStore node in nodes)
        {
            await node.Shutdown();
        }

        nodes.Clear();
    }

    [Test]
    public async Task TwoNodesSweepingAtOnceRecoverEachMisfiredTriggerExactlyOnce()
    {
        DateTimeOffset anchor = TimeProvider.System.GetUtcNow();
        DateTimeOffset scheduled = anchor - HalfPeriod;

        LocalTransactionJobStore nodeA = await CreateNode("nodeA");
        LocalTransactionJobStore nodeB = await CreateNode("nodeB");

        IJobDetail job = JobBuilder.Create<ConcurrentSweepJob>()
            .WithIdentity("concurrentSweepJob", Group)
            .StoreDurably()
            .Build();

        await nodeA.AddJob(job, replace: true);

        for (int i = 0; i < TriggerCount; i++)
        {
            IOperableTrigger trigger = BuildTrigger(anchor, "misfired-" + i.ToString(CultureInfo.InvariantCulture), job.Key);
            trigger.ComputeFirstFireTimeUtc(null);
            trigger.NextFireTimeUtc = scheduled;

            await nodeA.AddTrigger(trigger, replace: true);
        }

        (await CountTriggersDueAt(scheduled)).Should().Be(TriggerCount,
            "all {0} triggers have to be stored overdue before anything sweeps, or the race is not the one under test", TriggerCount);

        // What the policy arrives at. Every trigger carries the same schedule and DoNothing recomputes
        // from now rather than from the missed time, so all of them land on this one instant.
        IOperableTrigger detached = BuildTrigger(anchor, "expected", job.Key);
        detached.ComputeFirstFireTimeUtc(null);
        detached.NextFireTimeUtc = scheduled;
        detached.UpdateAfterMisfire(null);

        detached.NextFireTimeUtc.Should().NotBeNull("a DoNothing cron trigger always has a next slot to skip to");
        DateTimeOffset expected = detached.NextFireTimeUtc.Value;

        // Both sweeps are on threads of their own and both are waiting on the gate before either starts,
        // so the second one reaches the lock while the first still holds it. Starting them inline would
        // let the first run to completion before the second was ever scheduled, and the test would pass
        // without the two having contended at all.
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<RecoverMisfiredJobsResult> sweepA = Task.Run(() => Sweep(nodeA));
        Task<RecoverMisfiredJobsResult> sweepB = Task.Run(() => Sweep(nodeB));

        gate.SetResult();

        RecoverMisfiredJobsResult[] results = await Task.WhenAll(sweepA, sweepB);

        async Task<RecoverMisfiredJobsResult> Sweep(LocalTransactionJobStore node)
        {
            await gate.Task;
            return await node.DoRecoverMisfires(Guid.NewGuid());
        }

        TestContext.Out.WriteLine(
            $"Recovered per node: nodeA={results[0].ProcessedMisfiredTriggerCount}, nodeB={results[1].ProcessedMisfiredTriggerCount}");

        results.Sum(x => x.ProcessedMisfiredTriggerCount).Should().Be(TriggerCount,
            "the two sweeps have to partition the misfired triggers between them — a total above {0} means "
            + "both nodes handled the same rows and applied their misfire policies twice, and one below it "
            + "means rows were claimed and dropped", TriggerCount);

        results.Should().AllSatisfy(x => x.HasMoreMisfiredTriggers.Should().BeFalse(
            "the batch limit is above the number of misfired triggers, so neither sweep should report a remainder"));

        (await CountTriggersDueAt(expected)).Should().Be(TriggerCount,
            "every trigger has to end up on the instant its own misfire policy computed");
        (await CountTriggersDueAt(scheduled)).Should().Be(0,
            "a trigger still sitting on its missed fire time was never recovered by either node");
        (await CountRows(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_STATE <> 'WAITING'",
            ("schedulerName", SchedulerName))).Should().Be(0,
            "recovery leaves a trigger that still has fire times waiting, whichever node did the recovering");
    }

    /// <summary>
    /// A cron trigger firing once a day, half a period out from <paramref name="anchor" />, whose misfire
    /// instruction skips the missed firing rather than replaying it — so the instant it recomputes is a
    /// scheduled one and can be asserted exactly.
    /// </summary>
    private static IOperableTrigger BuildTrigger(DateTimeOffset anchor, string name, JobKey jobKey)
    {
        DateTime slot = (anchor + HalfPeriod).UtcDateTime;

        return (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(jobKey)
            .StartAt(anchor - HalfPeriod - TimeSpan.FromDays(1))
            .WithCronSchedule(
                string.Create(CultureInfo.InvariantCulture, $"{slot.Second} {slot.Minute} {slot.Hour} * * ?"),
                x => x.InTimeZone(TimeZoneInfo.Utc).WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .Build();
    }

    private Task<int> CountTriggersDueAt(DateTimeOffset dueAt)
    {
        return CountRows(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND NEXT_FIRE_TIME = @nextFireTime",
            ("schedulerName", SchedulerName),
            ("nextFireTime", dueAt.UtcTicks));
    }

    /// <summary>
    /// One clustered node, with no lock handler of its own so that the store installs the one its
    /// dialect calls for.
    /// </summary>
    private async Task<LocalTransactionJobStore> CreateNode(string instanceId)
    {
        LocalTransactionJobStore node = new(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, instanceId),
            storeOptions: TestJobStores.StoreOptions("default", "QRTZ_", options =>
            {
                // Comfortably above the trigger count, so a short sweep means contention rather than a
                // truncated batch.
                options.MaxMisfiresToHandleAtATime = TriggerCount * 2;
            }),
            clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = true),
            dbProvider: new DbProvider(Database.Provider, Database.ConnectionString),
            driverDelegate: Database.CreateDriverDelegate()) with
        {
            LockHandler = null,
        });

        // Initialized but never started, so that no MisfireHandler loop exists to sweep behind the
        // test's back — the two sweeps below are the only ones there are.
        await node.Initialize(TestJobStores.Identity(SchedulerName, instanceId));

        nodes.Add(node);
        return node;
    }

    /// <summary>A job that never runs: this fixture drives stores, not schedulers.</summary>
    public sealed class ConcurrentSweepJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
