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

#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A recovery serves the occurrence of the firing it runs again, and keeps its origin, on each of the three
/// paths that build a recovery trigger: cluster recovery of a dead node, a firing a shutdown hands back, and
/// a node recovering its own firing when it starts again (#4246).
/// </summary>
/// <remarks>
/// <para>
/// Each path copies the original trigger's data onto the recovery trigger, which is what carries a manual
/// run's mark and a backfill's slot across. The firing left behind ran ten seconds late, inside the misfire
/// threshold, so its fire time and its occurrence differ.
/// </para>
/// <para>
/// The harness is <see cref="RecoveredExecutionGroupSqliteTest" />'s: SQLite refuses to be clustered, so the
/// dead node is a second container on the same file, and the survivor is told it is clustered after it is
/// built. Every node runs on one <see cref="FakeTimeProvider" /> that only the test moves.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class FiringOccurrenceRecoverySqliteTest
{
    public enum RecoveryPath
    {
        ClusterRecovery,
        ShutdownHandBack,
        StartupRecovery
    }

    private const string SchedulerName = "firing-occurrence-recovery";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan late = TimeSpan.FromSeconds(10);
    private static readonly JobKey jobKey = new("publish", "occurrence");
    private static readonly TriggerKey nightlyKey = new("nightly", "occurrence");

    /// <summary>When the firing left behind is due: a quarter second past the hour, as a run asked for now is.</summary>
    private static readonly DateTimeOffset due = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero).AddMilliseconds(250);

    /// <summary>The slot a backfill stands in for, ten days earlier.</summary>
    private static readonly DateTimeOffset slot = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("firing-occurrence-recovery");
        Recordings.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task ARecoveryServesTheOccurrenceOfTheFiringItRunsAgain(
        [Values] FiringOrigin origin,
        [Values] RecoveryPath path,
        [Values] bool subSecond)
    {
        DateTimeOffset dueAt = subSecond ? due : due.AddMilliseconds(-due.Millisecond);
        FakeTimeProvider clock = new(dueAt);

        (FiringOccurrence original, FiringOccurrence recovered) = path switch
        {
            RecoveryPath.ClusterRecovery => await RecoveredByAPeer(origin, clock),
            RecoveryPath.ShutdownHandBack => await HandedBackAtShutdown(origin, clock),
            _ => await RecoveredAtStartup(origin, clock)
        };

        original.Should().Be(new FiringOccurrence(origin == FiringOrigin.Backfill ? slot : dueAt, origin),
            "the premise: the firing left behind serves its own occurrence");
        recovered.Should().Be(original with { Recovering = true },
            "a recovery runs that firing again: the same occurrence, for the same reason");
    }

    /// <summary>
    /// The gap, pinned as it is today: when the firing a recovery runs again was itself a retry or a misfire
    /// catch-up, the recovery serves the time that attempt was due rather than the occurrence, and starts its
    /// attempts again from <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The fired-trigger row's <c>SCHED_TIME</c> is the trigger's next fire time when it fired: a retry's
    /// instant, or the time a misfire instruction moved a catch-up to. That is all a recovery trigger is built
    /// from. Fixing it needs the occurrence carried on the row or in the recovery trigger's markers.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ARecoveredRetryOrCatchUpServesTheTimeThatAttemptWasDue(bool catchUp)
    {
        FakeTimeProvider clock = new(due);
        await using ServiceProvider dead = BuildNode("node-a", clock);
        IScheduler deadScheduler = await dead.GetRequiredService<ISchedulerFactory>().GetScheduler();
        AdoJobStoreBase deadStore = (AdoJobStoreBase) dead.GetRequiredService<IJobStore>();

        IJobDetail job = JobBuilder.Create<RecordingJob>().WithIdentity(jobKey).StoreDurably().RequestRecovery().Build();
        await deadScheduler.ScheduleJob(job, TriggerBuilder.Create(clock)
            .WithIdentity("hourly", "occurrence")
            .ForJob(jobKey)
            .StartAt(due)
            .WithRetryPolicy(RetryPolicy.Fixed(2, TimeSpan.FromMinutes(5)))
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build());

        DateTimeOffset attemptDue;
        if (catchUp)
        {
            // Ten minutes late: the misfire pass moves the trigger to now, and the catch-up fires then.
            clock.Advance(TimeSpan.FromMinutes(10));
            await deadStore.RecoverMisfires(Guid.NewGuid());
            attemptDue = clock.GetUtcNow();
        }
        else
        {
            // The occurrence fails on time, and its retry is due five minutes later.
            TriggerFiredBundle first = await Fire(deadStore, clock);
            using JobExecutionContextImpl context = new(deadScheduler, first, new RecordingJob());
            SchedulerInstruction instruction = first.Trigger.ExecutionComplete(context, new JobExecutionException(new InvalidOperationException("boom")));
            instruction.Should().Be(SchedulerInstruction.RetryTrigger, "the premise: a failure with attempts left is retried");
            await deadStore.TriggeredJobComplete(first.Trigger, first.JobDetail, instruction);

            clock.Advance(TimeSpan.FromMinutes(5));
            attemptDue = clock.GetUtcNow();
        }

        FiringOccurrence original = OccurrenceOf(deadScheduler, await Fire(deadStore, clock));
        original.Should().Be(new FiringOccurrence(due, FiringOrigin.Schedule) { RetryAttempt = catchUp ? 0 : 1 },
            "the premise: the attempt left behind serves the occurrence due at nine");

        // No completion: the node dies here, and a peer recovers the attempt.
        await using ServiceProvider survivor = BuildNode("node-b", clock);
        IScheduler scheduler = await survivor.GetRequiredService<ISchedulerFactory>().GetScheduler();
        AdoJobStoreBase store = (AdoJobStoreBase) survivor.GetRequiredService<IJobStore>();
        store.Clustered = true;
        (await store.CheckIn(Guid.NewGuid())).Should().BeTrue("the premise: the check-in recovers the dead node's attempt");

        FiringOccurrence recovered = OccurrenceOf(scheduler, await Fire(store, clock));
        recovered.Should().Be(new FiringOccurrence(attemptDue, FiringOrigin.Schedule) { Recovering = true },
            "today a recovery serves the time the attempt it runs again was due, which for a retry or a catch-up is not the occurrence");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The three paths
    //////////////////////////////////////////////////////////////////////////////////////////////

    private async Task<(FiringOccurrence Original, FiringOccurrence Recovered)> RecoveredByAPeer(FiringOrigin origin, FakeTimeProvider clock)
    {
        await using ServiceProvider dead = BuildNode("node-a", clock);
        (FiringOccurrence original, _) = await LeaveAFiringBehind(dead, origin, clock);

        await using ServiceProvider survivor = BuildNode("node-b", clock);
        IScheduler scheduler = await survivor.GetRequiredService<ISchedulerFactory>().GetScheduler();

        // A SQLite store refuses to be configured clustered, so it is told after it is built. Its first
        // check-in finds the dead node's row with no scheduler state beside it, and recovers it.
        AdoJobStoreBase store = (AdoJobStoreBase) survivor.GetRequiredService<IJobStore>();
        store.Clustered = true;
        (await store.CheckIn(Guid.NewGuid())).Should().BeTrue("the premise: the check-in recovers the dead node's firing");

        return (original, OccurrenceOf(scheduler, await Fire(store, clock)));
    }

    private async Task<(FiringOccurrence Original, FiringOccurrence Recovered)> HandedBackAtShutdown(FiringOrigin origin, FakeTimeProvider clock)
    {
        await using ServiceProvider node = BuildNode("one", clock, handBack: true);
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = node.GetRequiredService<IJobStore>();
        (FiringOccurrence original, TriggerFiredBundle bundle) = await LeaveAFiringBehind(node, origin, clock);

        // What the run shell reports for a firing the shutdown cancelled, to a store that hands such firings back.
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = bundle.Trigger,
            JobDetail = bundle.JobDetail,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Cancelled,
            HandBackForRecovery = true
        });

        return (original, OccurrenceOf(scheduler, await Fire(store, clock)));
    }

    private async Task<(FiringOccurrence Original, FiringOccurrence Recovered)> RecoveredAtStartup(FiringOrigin origin, FakeTimeProvider clock)
    {
        FiringOccurrence original;
        await using (ServiceProvider killed = BuildNode("one", clock))
        {
            (original, _) = await LeaveAFiringBehind(killed, origin, clock);
        }

        // A real start: the store recovers the node's own firing, and the scheduler runs the recovery.
        await using ServiceProvider restarted = BuildNode("one", clock);
        IScheduler scheduler = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        FiringOccurrence recovered = await Recordings.Recovered.WaitAsync(waitLimit);
        await scheduler.Shutdown(waitForJobsToComplete: true);
        return (original, recovered);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// What a node killed mid-execution leaves in the database: a firing of the given origin, due at the
    /// clock's time and fired ten seconds later, that asks to be recovered and is never completed.
    /// </summary>
    private static async Task<(FiringOccurrence Occurrence, TriggerFiredBundle Bundle)> LeaveAFiringBehind(
        ServiceProvider node,
        FiringOrigin origin,
        FakeTimeProvider clock)
    {
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(jobKey).StoreDurably().RequestRecovery().Build());

        switch (origin)
        {
            case FiringOrigin.Schedule:
                // Hourly, so the firing moves the trigger an hour on and the recovery trigger is the only one due.
                await scheduler.ScheduleJob(TriggerBuilder.Create(clock)
                    .WithIdentity("hourly", "occurrence")
                    .ForJob(jobKey)
                    .StartAt(clock.GetUtcNow())
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .Build());
                break;

            case FiringOrigin.Manual:
                await scheduler.TriggerJob(jobKey);
                break;

            default:
                // A nightly trigger backfilled for its first night, then paused so that only the backfill is due.
                await scheduler.ScheduleJob(TriggerBuilder.Create(clock)
                    .WithIdentity(nightlyKey)
                    .ForJob(jobKey)
                    .StartAt(slot)
                    .WithCronSchedule("0 0 0 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
                    .Build());
                BackfillResult backfill = await scheduler.Backfill(nightlyKey, slot, slot.AddDays(1));
                backfill.Scheduled.Should().Be(1, "the premise: one night, one backfilled firing");
                await scheduler.PauseTriggers([nightlyKey]);
                break;
        }

        clock.Advance(late);
        TriggerFiredBundle bundle = await Fire(node.GetRequiredService<IJobStore>(), clock);
        bundle.FireTimeUtc.Should().Be(clock.GetUtcNow(), "the premise: it fired ten seconds after it was due");

        // No completion: this is where the node dies.
        return (OccurrenceOf(scheduler, bundle), bundle);
    }

    /// <summary>Acquires and fires the one trigger due, as the scheduler thread would.</summary>
    private static async Task<TriggerFiredBundle> Fire(IJobStore store, FakeTimeProvider clock)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero
        });
        acquired.Should().ContainSingle("one trigger is due");

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        return fired.Should().ContainSingle().Which.TriggerFiredBundle!;
    }

    private static FiringOccurrence OccurrenceOf(IScheduler scheduler, TriggerFiredBundle bundle)
    {
        using JobExecutionContextImpl context = new(scheduler, bundle, new RecordingJob());
        return context.GetOccurrence();
    }

    /// <param name="instanceId">The node's instance id; two ids on one file are two nodes.</param>
    /// <param name="clock">The clock every node runs on.</param>
    /// <param name="handBack">Whether the store hands back what a shutdown cancels.</param>
    private ServiceProvider BuildNode(string instanceId, FakeTimeProvider clock, bool handBack = false)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.UseTimeProvider(clock);
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = instanceId;
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options => options.RecoverFiringsCancelledByShutdown = handBack);
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>The recovery run, once it happens.</summary>
    private static class Recordings
    {
        private static TaskCompletionSource<FiringOccurrence> recovered = NewSource();

        public static Task<FiringOccurrence> Recovered => recovered.Task;

        public static void Reset()
        {
            recovered = NewSource();
        }

        public static void Record(IJobExecutionContext context)
        {
            if (context.Recovering)
            {
                recovered.TrySetResult(context.GetOccurrence());
            }
        }

        private static TaskCompletionSource<FiringOccurrence> NewSource()
        {
            return new TaskCompletionSource<FiringOccurrence>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public sealed class RecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Recordings.Record(context);
            return default;
        }
    }
}
