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

using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A firing of a job that requests recovery, cancelled by a shutdown, is handed back for recovery when
/// the store is set to: a recovery trigger is stored in the completion's transaction, and the job runs
/// again when the scheduler starts (#4014).
/// </summary>
/// <remarks>
/// <para>
/// Without the setting the firing completes as cancelled, as it always has, and only a hard kill gets
/// the job run again. A user's interrupt stays a cancellation either way.
/// </para>
/// <para>
/// SQLite cannot be clustered, so "runs again" here is this node starting again. The clustered case,
/// where a peer runs it at once, is <c>ShutdownHandBackClusteredPostgresTest</c> in the integration
/// suite.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class ShutdownHandBackSqliteTest
{
    private const string SchedulerName = "shutdown-hand-back";

    /// <summary>Log event <c>AdoJobStoreLog.FiringHandedBack</c>.</summary>
    private const int FiringHandedBack = 3054;

    /// <summary>Log event <c>AdoJobStoreLog.FiringRowGoneBeforeHandBack</c>.</summary>
    private const int FiringRowGoneBeforeHandBack = 3055;

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private static readonly JobKey jobKey = new("long", "hand-back");
    private static readonly TriggerKey triggerKey = new("t-long", "hand-back");
    private static readonly TriggerKey siblingKey = new("t-sibling", "hand-back");
    private static readonly JobKey followUpJobKey = new("follow-up", "hand-back");
    private static readonly TriggerKey continuationKey = new("t-after", "hand-back");

    private SqliteTestDatabase database = null!;
    private RecordingLoggerProvider logs = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("shutdown-hand-back");
        logs = new RecordingLoggerProvider();
        LongJob.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        logs.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// The shutdown cancels the job, and the completion stores a recovery trigger in place of the
    /// fired-trigger row. When the scheduler starts again the job runs once more, told it is recovering
    /// and which firing it stands in for.
    /// </summary>
    [Test]
    public async Task AFiringTheShutdownCancelsIsHandedBackAndRunsAgainWhenTheSchedulerStarts()
    {
        Started cancelled;
        await using (ServiceProvider first = BuildContainer(recover: true))
        {
            IScheduler scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await ScheduleLongJob(scheduler);

            await scheduler.Start();
            cancelled = await LongJob.Started.WaitAsync(waitLimit);
            await scheduler.Shutdown(waitForJobsToComplete: true);

            (await ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS")).Should().BeEmpty(
                "the hand-back completes the firing: its row is replaced by the recovery trigger, in one transaction");
            (await RecoveryTriggerStates()).Should().Equal(["WAITING"],
                "one recovery trigger, waiting for whichever node fires next");

            ExecutionHistoryEntry row = await TheOneHistoryRow(first);
            row.Result.Should().Be(JobRunResult.Cancelled, "the firing was cancelled, and the history says what happened to it");
            row.Summary.Should().Be(JobRunClassifier.HandedBackSummary, "the summary is what tells a hand-back from any other cancellation");
            row.RetryScheduled.Should().BeFalse("a hand-back is not a retry");
            row.RetryAttempt.Should().Be(0);

            logs.Entries.Should().ContainSingle(x => x.EventId.Id == FiringHandedBack)
                .Which.Message.Should().Contain(cancelled.FireInstanceId).And.Contain(jobKey.ToString());
        }

        await using ServiceProvider restarted = BuildContainer(recover: true);
        IScheduler again = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await again.Start();

        Recovered recovered = await LongJob.Recovered.WaitAsync(waitLimit);
        await again.Shutdown(waitForJobsToComplete: true);

        recovered.RecoveringTriggerKey.Should().Be(triggerKey, "the recovery run names the trigger whose firing it stands in for");
        recovered.TriggerGroup.Should().Be(SchedulerConstants.DefaultRecoveryGroup);
        recovered.OriginalFireTime.Should().BeCloseTo(cancelled.FireTimeUtc, TimeSpan.FromSeconds(1),
            "the marker carries the fire time of the firing that was handed back, to the second");
        recovered.OriginalScheduledFireTime.Should().BeCloseTo(cancelled.ScheduledFireTimeUtc, TimeSpan.FromSeconds(1),
            "and its scheduled fire time, as the startup recovery's markers always have");

        (await RecoveryTriggerStates()).Should().BeEmpty("the recovery trigger fires once and is gone");
    }

    /// <summary>
    /// A user's interrupt is a cancellation whatever the store is set to: nothing is handed back, and the
    /// shutdown that follows does not turn it into one.
    /// </summary>
    [Test]
    public async Task AnInterruptIsStillACancellation()
    {
        await using ServiceProvider container = BuildContainer(recover: true);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await ScheduleLongJob(scheduler);

        await scheduler.Start();
        await LongJob.Started.WaitAsync(waitLimit);

        (await scheduler.Interrupt(jobKey)).Should().BeTrue();
        await LongJob.Ended.WaitAsync(waitLimit);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await RecoveryTriggerStates()).Should().BeEmpty("an interrupt asks the job to stop, not to run again");
        (await ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS")).Should().BeEmpty("the firing completed as cancelled");

        ExecutionHistoryEntry row = await TheOneHistoryRow(container);
        row.Result.Should().Be(JobRunResult.Cancelled);
        row.Summary.Should().BeNull("only a hand-back says it was handed back");
        logs.Entries.Should().NotContain(x => x.EventId.Id == FiringHandedBack);
    }

    /// <summary>
    /// Off, the default: a firing the shutdown cancels completes as cancelled, as it did before the
    /// setting existed, and nothing runs it again.
    /// </summary>
    [Test]
    public async Task WithTheSettingOffAFiringTheShutdownCancelsIsCompletedAsCancelled()
    {
        await using ServiceProvider container = BuildContainer(recover: false);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await ScheduleLongJob(scheduler);

        await scheduler.Start();
        await LongJob.Started.WaitAsync(waitLimit);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await RecoveryTriggerStates()).Should().BeEmpty();
        (await ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS")).Should().BeEmpty();

        ExecutionHistoryEntry row = await TheOneHistoryRow(container);
        row.Result.Should().Be(JobRunResult.Cancelled);
        row.Summary.Should().BeNull();
    }

    /// <summary>
    /// A job that does not request recovery has nothing to hand back, whatever the store is set to.
    /// </summary>
    [Test]
    public async Task AJobThatDoesNotRequestRecoveryIsCompletedAsCancelled()
    {
        await using ServiceProvider container = BuildContainer(recover: true);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await ScheduleLongJob(scheduler, requestsRecovery: false);

        await scheduler.Start();
        await LongJob.Started.WaitAsync(waitLimit);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await RecoveryTriggerStates()).Should().BeEmpty();
        (await TheOneHistoryRow(container)).Summary.Should().BeNull();
    }

    /// <summary>
    /// The job's other trigger, blocked behind the firing, is let go as any completion lets it go; the
    /// recovery trigger is never stored blocked.
    /// </summary>
    [Test]
    public async Task ASerialJobsOtherTriggerIsReleased()
    {
        await using ServiceProvider container = BuildContainer(recover: true);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await ScheduleLongJob(scheduler, serial: true);
        await scheduler.ScheduleJob(Hourly(siblingKey, jobKey, priority: 1));

        await scheduler.Start();
        Started started = await LongJob.Started.WaitAsync(waitLimit);
        started.TriggerKey.Should().Be(triggerKey, "the premise: the higher-priority trigger fires first");
        (await TriggerStateOf(siblingKey)).Should().Be("BLOCKED", "the premise: the serial job's other trigger waits behind the firing");

        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await TriggerStateOf(siblingKey)).Should().Be("WAITING",
            "the hand-back completes the firing, and a completion lets go of a serial job's other triggers");
        (await RecoveryTriggerStates()).Should().Equal(["WAITING"]);
    }

    /// <summary>
    /// A trigger waiting on the firing's cancellation is settled when the firing completes as cancelled,
    /// and left waiting when it is handed back: the occurrence has not ended.
    /// </summary>
    [TestCase(true, "AWAITING")]
    [TestCase(false, "WAITING")]
    public async Task AContinuationIsSettledOnlyByAFiringThatIsNotHandedBack(bool recover, string expectedState)
    {
        await using ServiceProvider container = BuildContainer(recover);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await ScheduleLongJob(scheduler);
        await scheduler.ScheduleJob(
            JobBuilder.Create<FollowUpJob>().WithIdentity(followUpJobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(continuationKey)
                .StartAfter(triggerKey, ContinuationCondition.OnCancellation)
                .Build());

        await scheduler.Start();
        await LongJob.Started.WaitAsync(waitLimit);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await TriggerStateOf(continuationKey)).Should().Be(expectedState, recover
            ? "a firing handed back has not ended, so nothing waiting on how it ends is settled"
            : "a firing completed as cancelled releases what waits on its cancellation");

        if (recover)
        {
            (await ParentOf(continuationKey)).Should().Be(await TheRecoveryTriggerName(),
                "it now waits for the replay, not for the repeating trigger's next occurrence");
        }
    }

    /// <summary>
    /// A one-shot trigger whose firing is handed back is deleted as a spent trigger is, and what waits on
    /// it moves to the recovery trigger rather than being settled for a parent that is gone. The replay's
    /// own outcome then settles it: the continuation that outcome names runs once, the other is discarded.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TheReplaysOutcomeSettlesWhatWaitedOnAOneShotFiringHandedBack(bool replaySucceeds)
    {
        LongJob.ReplayFails = !replaySucceeds;
        TriggerKey onSuccess = new("t-on-success", "hand-back");
        TriggerKey onFailure = new("t-on-failure", "hand-back");

        await using (ServiceProvider first = BuildContainer(recover: true))
        {
            IScheduler scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.ScheduleJob(
                JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().Build(),
                TriggerBuilder.Create().WithIdentity(triggerKey).StartNow().Build());
            await scheduler.AddJob(JobBuilder.Create<FollowUpJob>().WithIdentity(followUpJobKey).StoreDurably().Build());
            await scheduler.ScheduleJob(Continuation(onSuccess, ContinuationCondition.OnSuccess));
            await scheduler.ScheduleJob(Continuation(onFailure, ContinuationCondition.OnFailure));

            await scheduler.Start();
            await LongJob.Started.WaitAsync(waitLimit);
            await scheduler.Shutdown(waitForJobsToComplete: true);

            (await ReadColumn($"SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = '{triggerKey.Name}'")).Should().BeEmpty(
                "the one-shot trigger is spent, and its completion deletes it as it always has");

            string recoveryTrigger = await TheRecoveryTriggerName();
            foreach (TriggerKey continuation in new[] { onSuccess, onFailure })
            {
                (await TriggerStateOf(continuation)).Should().Be("AWAITING",
                    "deleting the parent would have parked it, or released it for an outcome the replay has not had yet");
                (await ParentOf(continuation)).Should().Be(recoveryTrigger, "it waits for the replay instead");
            }
        }

        await using ServiceProvider restarted = BuildContainer(recover: true);
        IScheduler again = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await again.Start();

        await LongJob.Recovered.WaitAsync(waitLimit);
        TriggerKey expected = replaySucceeds ? onSuccess : onFailure;
        TriggerKey other = replaySucceeds ? onFailure : onSuccess;
        await FollowUpJob.FirstRun.WaitAsync(waitLimit);
        await again.Shutdown(waitForJobsToComplete: true);

        FollowUpJob.RunsOf(expected).Should().Be(1, "the replay's outcome released it, once");
        FollowUpJob.RunsOf(other).Should().Be(0, "the replay's outcome is not the one it waited for");
        (await ReadColumn($"SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = '{other.Name}'")).Should().BeEmpty(
            "a continuation whose condition the outcome does not meet is discarded");
    }

    /// <summary>
    /// A store wrapped in a <see cref="DelegatingJobStore" /> is still the persistent store underneath,
    /// and hands back as it does unwrapped.
    /// </summary>
    [Test]
    public async Task AStoreWrappedInADelegatingJobStoreStillHandsBack()
    {
        await using ServiceProvider container = BuildContainer(recover: true, wrapped: true);
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        container.GetRequiredService<IJobStore>().Should().BeOfType<WrappingJobStore>("the premise: the store is wrapped");

        await ScheduleLongJob(scheduler);
        await scheduler.Start();
        await LongJob.Started.WaitAsync(waitLimit);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await RecoveryTriggerStates()).Should().Equal(["WAITING"], "the wrapper forwards the completion to the store that hands it back");
        (await TheOneHistoryRow(container)).Summary.Should().Be(JobRunClassifier.HandedBackSummary);
    }

    /// <summary>
    /// The replay starts from the job data the cancelled run started with, as a replay after a crash does:
    /// what a <see cref="PersistJobDataAfterExecutionAttribute" /> job changed on its way to being
    /// cancelled is not written. A firing completed as cancelled writes it, as it always has.
    /// </summary>
    [TestCase(true, null)]
    [TestCase(false, "halfway")]
    public async Task AFiringHandedBackDoesNotPersistTheJobDataItChanged(bool recover, string? expectedProgress)
    {
        await using (ServiceProvider first = BuildContainer(recover))
        {
            IScheduler scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.ScheduleJob(
                JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().PersistJobDataAfterExecution().Build(),
                Hourly(triggerKey, jobKey, priority: 10));

            await scheduler.Start();
            await LongJob.Started.WaitAsync(waitLimit);
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        await using ServiceProvider reread = BuildContainer(recover);
        IScheduler unstarted = await reread.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobDetail stored = (await unstarted.GetJobDetail(jobKey))!;

        stored.JobDataMap.GetString(LongJob.ProgressKey).Should().Be(expectedProgress);
    }

    /// <summary>
    /// The replay of a firing handed back is itself cancelled by a shutdown and handed back. The second
    /// recovery trigger stands in for the original firing, as the first did, not for the first recovery
    /// trigger's firing.
    /// </summary>
    [Test]
    public async Task ARecoveryFiringHandedBackAgainKeepsTheOriginalFiringsMarkers()
    {
        Started original;
        await using (ServiceProvider nodeA = BuildContainer(recover: true))
        {
            IScheduler scheduler = await nodeA.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await ScheduleLongJob(scheduler);
            await scheduler.Start();
            original = await LongJob.Started.WaitAsync(waitLimit);
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        // The first replay runs long too, and its shutdown hands it back in turn.
        LongJob.RunLongWhenRecovering(times: 1);
        LongJob.ResetSignals();
        await using (ServiceProvider nodeB = BuildContainer(recover: true))
        {
            IScheduler scheduler = await nodeB.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Start();
            Started replay = await LongJob.Started.WaitAsync(waitLimit);
            replay.TriggerKey.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup, "the premise: what runs long now is the first replay");
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        LongJob.ResetSignals();
        await using ServiceProvider restartedB = BuildContainer(recover: true);
        IScheduler again = await restartedB.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await again.Start();
        Recovered recovered = await LongJob.Recovered.WaitAsync(waitLimit);
        await again.Shutdown(waitForJobsToComplete: true);

        recovered.RecoveringTriggerKey.Should().Be(triggerKey, "the replay still stands in for the original trigger's firing");
        recovered.OriginalFireTime.Should().BeCloseTo(original.FireTimeUtc, TimeSpan.FromSeconds(1),
            "and for that firing's fire time, not the first replay's");
        recovered.OriginalScheduledFireTime.Should().BeCloseTo(original.ScheduledFireTimeUtc, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A clock that does not move names two recovery triggers alike if the name comes from the clock alone,
    /// and the second store of one is refused for good inside the completion's retry loop. Each gets a name
    /// of its own.
    /// </summary>
    [Test]
    public async Task TwoHandBacksOnAFrozenClockGetARecoveryTriggerEach()
    {
        FakeTimeProvider frozen = new(TimeProvider.System.GetUtcNow());
        await using ServiceProvider container = BuildContainer(recover: true, clock: frozen);
        await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = container.GetRequiredService<IJobStore>();

        IJobDetail job = JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().StoreDurably().Build();
        await store.AddJob(job);
        foreach (TriggerKey key in new[] { triggerKey, siblingKey })
        {
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(frozen).WithIdentity(key).ForJob(jobKey).StartAt(frozen.GetUtcNow()).Build();
            trigger.ComputeFirstFireTimeUtc(calendar: null);
            await store.AddTrigger(trigger);
        }

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = frozen.GetUtcNow().AddMinutes(1),
            MaxCount = 2,
            TimeWindow = TimeSpan.FromMinutes(1)
        });
        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        fired.Should().HaveCount(2, "the premise: both firings are in flight");

        foreach (TriggerFiredResult result in fired)
        {
            await store.FiringComplete(HandedBack(result.TriggerFiredBundle!, SchedulerInstruction.DeleteTrigger))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        }

        (await ReadColumn($"SELECT TRIGGER_NAME FROM QRTZ_TRIGGERS WHERE TRIGGER_GROUP = '{SchedulerConstants.DefaultRecoveryGroup}'"))
            .Should().HaveCount(2).And.OnlyHaveUniqueItems("each firing handed back is stood in for by a trigger of its own");
    }

    /// <summary>
    /// A peer that judged this node failed may have recovered the firing while the job ran, deleting its
    /// fired-trigger row and storing a recovery trigger of its own. The hand-back then stores none, so
    /// the job does not run twice.
    /// </summary>
    [Test]
    public async Task AFiringAlreadyRecoveredIsNotHandedBackASecondTime()
    {
        await using ServiceProvider container = BuildContainer(recover: true);
        await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = container.GetRequiredService<IJobStore>();
        TriggerFiredBundle bundle = await StoreAndFire(store);

        await ExecuteNonQuery("DELETE FROM QRTZ_FIRED_TRIGGERS");

        await store.FiringComplete(HandedBack(bundle));

        (await RecoveryTriggerStates()).Should().BeEmpty("the firing was recovered once already");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FiringRowGoneBeforeHandBack);
    }

    /// <summary>
    /// A completion marked for hand-back is a cancellation to a store that is not set to recover such
    /// firings, and so is one marked for a firing that did not end cancelled.
    /// </summary>
    [TestCase(false, ExecutionOutcome.Cancelled)]
    [TestCase(true, ExecutionOutcome.Succeeded)]
    public async Task AMarkedCompletionTheStoreDoesNotHandBackCompletesAsBefore(bool recover, ExecutionOutcome outcome)
    {
        await using ServiceProvider container = BuildContainer(recover);
        await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = container.GetRequiredService<IJobStore>();
        TriggerFiredBundle bundle = await StoreAndFire(store);

        TriggeredJobCompleteContext marked = HandedBack(bundle);
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = marked.Trigger,
            JobDetail = marked.JobDetail,
            Instruction = marked.Instruction,
            Outcome = outcome,
            HandBackForRecovery = true
        });

        (await RecoveryTriggerStates()).Should().BeEmpty();
        (await ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS")).Should().BeEmpty("the firing completed");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    private static async Task ScheduleLongJob(IScheduler scheduler, bool requestsRecovery = true, bool serial = false)
    {
        IJobDetail job = serial
            ? JobBuilder.Create<SerialLongJob>().WithIdentity(jobKey).RequestRecovery(requestsRecovery).Build()
            : JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery(requestsRecovery).Build();

        await scheduler.ScheduleJob(job, Hourly(triggerKey, jobKey, priority: 10));
    }

    /// <remarks>
    /// Repeating, so the trigger row is still there to read once its firing is over.
    /// </remarks>
    private static ITrigger Hourly(TriggerKey key, JobKey job, int priority)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartNow()
            .WithPriority(priority)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
    }

    /// <summary>A trigger of the follow-up job that waits on the long job's trigger.</summary>
    private static ITrigger Continuation(TriggerKey key, ContinuationCondition condition)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(followUpJobKey)
            .StartAfter(triggerKey, condition)
            .Build();
    }

    /// <summary>
    /// Stores the long job and its trigger, then acquires and fires it through the store, as the
    /// scheduler thread would. Answers the bundle a run shell would be handed.
    /// </summary>
    private static async Task<TriggerFiredBundle> StoreAndFire(IJobStore store)
    {
        IJobDetail job = JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().Build();
        IOperableTrigger trigger = (IOperableTrigger) Hourly(triggerKey, jobKey, priority: 10);
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero
        });

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        return fired.Should().ContainSingle().Which.TriggerFiredBundle!;
    }

    private static TriggeredJobCompleteContext HandedBack(
        TriggerFiredBundle bundle,
        SchedulerInstruction instruction = SchedulerInstruction.NoInstruction)
    {
        return new TriggeredJobCompleteContext
        {
            Trigger = bundle.Trigger,
            JobDetail = bundle.JobDetail,
            Instruction = instruction,
            Outcome = ExecutionOutcome.Cancelled,
            HandBackForRecovery = true
        };
    }

    private static async Task<ExecutionHistoryEntry> TheOneHistoryRow(IServiceProvider container)
    {
        PagedResult<ExecutionHistoryEntry> rows = await container.GetRequiredService<IExecutionHistoryStore>()
            .QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName, Take = PagedQuery.All });

        return rows.Items.Should().ContainSingle("the long job ran once").Subject;
    }

    private Task<List<string>> RecoveryTriggerStates()
    {
        return ReadColumn($"SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_GROUP = '{SchedulerConstants.DefaultRecoveryGroup}'");
    }

    private async Task<string> TheRecoveryTriggerName()
    {
        List<string> names = await ReadColumn($"SELECT TRIGGER_NAME FROM QRTZ_TRIGGERS WHERE TRIGGER_GROUP = '{SchedulerConstants.DefaultRecoveryGroup}'");
        return names.Should().ContainSingle("one firing was handed back").Subject;
    }

    /// <summary>The name of the trigger a continuation waits on, as its row says.</summary>
    private async Task<string> ParentOf(TriggerKey key)
    {
        List<string> parents = await ReadColumn($"SELECT {AdoConstants.ColumnContinuesTriggerName} FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = '{key.Name}'");
        return parents.Should().ContainSingle().Subject;
    }

    private async Task<string> TriggerStateOf(TriggerKey key)
    {
        List<string> states = await ReadColumn($"SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = '{key.Name}'");
        return states.Should().ContainSingle().Subject;
    }

    private async Task<List<string>> ReadColumn(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<string> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task ExecuteNonQuery(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <param name="recover">Whether the store hands back what the shutdown cancels.</param>
    /// <param name="wrapped">Whether the persistent store is wrapped in a <see cref="DelegatingJobStore" />.</param>
    /// <param name="clock">The scheduler's clock, when it is not the system's.</param>
    private ServiceProvider BuildContainer(bool recover, bool wrapped = false, TimeProvider? clock = null)
    {
        ServiceCollection services = new();
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "one";

                // The shutdown asks running jobs to stop, and then waits for them to.
                options.ShutdownJobInterruption = ShutdownJobInterruption.WhenWaitingForJobs;
            });

            if (wrapped)
            {
                // First wins: the persistent store below is configured, and this is the store built.
                q.UseJobStore(provider => new WrappingJobStore(ActivatorUtilities.CreateInstance<LocalTransactionJobStore>(provider)));
            }

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options => options.RecoverFiringsCancelledByShutdown = recover);
            });
        });

        return services.BuildServiceProvider();
    }

    public sealed record Started(TriggerKey TriggerKey, string FireInstanceId, DateTimeOffset FireTimeUtc, DateTimeOffset ScheduledFireTimeUtc);

    public sealed record Recovered(
        string TriggerGroup,
        TriggerKey? RecoveringTriggerKey,
        DateTimeOffset OriginalFireTime,
        DateTimeOffset OriginalScheduledFireTime);

    /// <summary>
    /// Runs until it is cancelled, and lets the cancellation out, unless it is recovering, when it records
    /// what it was told and returns, or throws when the replay is to fail.
    /// </summary>
    /// <remarks>
    /// A job that persists its data writes <see cref="ProgressKey" /> before it waits, so a test can see
    /// whether the store kept it.
    /// </remarks>
    public class LongJob : IJob
    {
        public const string ProgressKey = "progress";

        private static TaskCompletionSource<Started> started = NewSource<Started>();
        private static TaskCompletionSource<Recovered> recovered = NewSource<Recovered>();
        private static TaskCompletionSource<bool> ended = NewSource<bool>();
        private static int longRecoveries;
        private static volatile bool replayFails;

        public static Task<Started> Started => started.Task;

        public static Task<Recovered> Recovered => recovered.Task;

        public static Task Ended => ended.Task;

        /// <summary>Whether a recovering run throws once it has recorded itself.</summary>
        public static bool ReplayFails
        {
            set => replayFails = value;
        }

        public static void Reset()
        {
            ResetSignals();
            Interlocked.Exchange(ref longRecoveries, 0);
            replayFails = false;
            FollowUpJob.Reset();
        }

        public static void ResetSignals()
        {
            started = NewSource<Started>();
            recovered = NewSource<Recovered>();
            ended = NewSource<bool>();
        }

        /// <summary>Runs the next <paramref name="times" /> recovering runs long, as a first run does.</summary>
        public static void RunLongWhenRecovering(int times) => Interlocked.Exchange(ref longRecoveries, times);

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.Recovering && Interlocked.Decrement(ref longRecoveries) < 0)
            {
                JobDataMap map = context.MergedJobDataMap;
                recovered.TrySetResult(new Recovered(
                    context.Trigger.Key.Group,
                    context.RecoveringTriggerKey,
                    DateTimeOffset.Parse(map.GetString(SchedulerConstants.FailedJobOriginalTriggerFireTime)!, CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(map.GetString(SchedulerConstants.FailedJobOriginalTriggerScheduledFireTime)!, CultureInfo.InvariantCulture)));

                if (replayFails)
                {
                    throw new JobExecutionException("The replay failed.");
                }

                return;
            }

            if (context.JobDetail.PersistJobDataAfterExecution)
            {
                context.JobDetail.JobDataMap[ProgressKey] = "halfway";
            }

            started.TrySetResult(new Started(context.Trigger.Key, context.FireInstanceId, context.FireTimeUtc, context.ScheduledFireTimeUtc!.Value));
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ended.TrySetResult(true);
            }
        }

        private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [DisallowConcurrentExecution]
    public sealed class SerialLongJob : LongJob;

    /// <summary>Counts its runs by trigger.</summary>
    public sealed class FollowUpJob : IJob
    {
        private static ConcurrentDictionary<TriggerKey, int> runs = new();
        private static TaskCompletionSource firstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task FirstRun => firstRun.Task;

        public static void Reset()
        {
            runs = new ConcurrentDictionary<TriggerKey, int>();
            firstRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static int RunsOf(TriggerKey key) => runs.TryGetValue(key, out int count) ? count : 0;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            runs.AddOrUpdate(context.Trigger.Key, 1, static (_, count) => count + 1);
            firstRun.TrySetResult();
            return default;
        }
    }

    /// <summary>A decorator that adds nothing, as an application's own might.</summary>
    private sealed class WrappingJobStore(IJobStore inner) : DelegatingJobStore(inner);
}
