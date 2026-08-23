using System.Data.Common;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Covers the batched fire write. The fire path decides everything it decides before it writes
/// anything, so all of its writes travel in one round trip where the provider can batch — and in
/// exactly the statements they always were where it cannot.
/// </summary>
public class ApplyTriggerFiredBatchTest
{
    [Test]
    public async Task IssuesTheWholeFireAsOneBatch_WhenProviderSupportsBatching()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.ApplyTriggerFired(conn, CreateUpdate());

        connection.Batches.Should().HaveCount(1, "a fire that reads nothing back has no reason to take more than one round trip");
        connection.Batches[0].ExecuteCount.Should().Be(1);
        connection.Batches[0].Commands.Should().HaveCount(3, "a plain fire writes the fired-trigger row, the trigger's own row and its schedule");
        del.PreparedCommands.Should().BeEmpty("nothing should have been issued as a standalone command");
    }

    [Test]
    public async Task WritesTheFiredTriggerRowTheTriggerRowAndItsSchedule()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.ApplyTriggerFired(conn, CreateUpdate());

        string[] statements = [.. connection.Batches[0].Commands.Select(x => x.CommandText)];
        statements.Should().ContainSingle(x => x.StartsWith("UPDATE QRTZ_FIRED_TRIGGERS", StringComparison.Ordinal));
        statements.Should().ContainSingle(x => x.StartsWith("UPDATE QRTZ_TRIGGERS", StringComparison.Ordinal));

        // The schedule follows in the same batch, which is what the persistence delegate's describe
        // member exists for.
        del.PreparedCommands.Should().BeEmpty();
        statements.Should().ContainSingle(x => x.Contains("QRTZ_SIMPLE_TRIGGERS", StringComparison.Ordinal));
    }

    /// <summary>
    /// The fired-trigger row records the fire that just happened, and by the time the write goes out the
    /// trigger has already moved on to the following one. It is the caller's value that must be stored.
    /// </summary>
    [Test]
    public async Task RecordsTheScheduledTimeItWasGivenRatherThanTheTriggersNextFireTime()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        DateTimeOffset scheduled = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        SimpleTriggerImpl trigger = CreateTrigger();
        trigger.NextFireTimeUtc = scheduled.AddHours(1);

        await del.ApplyTriggerFired(conn, CreateUpdate(trigger, scheduledFireTimeUtc: scheduled));

        StubBatchCommand firedTriggerUpdate = connection.Batches[0].Commands
            .Single(x => x.CommandText.StartsWith("UPDATE QRTZ_FIRED_TRIGGERS", StringComparison.Ordinal));

        object bound = firedTriggerUpdate.Parameters.Cast<DbParameter>().Single(x => x.ParameterName == "@scheduledTime").Value;
        bound.Should().Be(scheduled.UtcTicks,
            "the fired-trigger row has to say when this fire was due, not when the next one is");
    }

    [Test]
    public async Task BlocksTheJobsOtherTriggersBeforeWritingItsOwnRow()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.ApplyTriggerFired(conn, CreateUpdate(blockJobTriggers: true));

        string[] statements = [.. connection.Batches[0].Commands.Select(x => x.CommandText)];
        statements.Should().HaveCount(6, "three sibling transitions join the fired-trigger row, the trigger's own row and its schedule");

        int lastTransition = Array.FindLastIndex(statements, x => x.Contains("JOB_NAME = @jobName", StringComparison.Ordinal));
        int ownRow = Array.FindIndex(statements, x => x.StartsWith("UPDATE QRTZ_TRIGGERS SET JOB_NAME", StringComparison.Ordinal));
        lastTransition.Should().BeLessThan(ownRow,
            "the trigger is still ACQUIRED when the transitions run, so its own row must be written over the top of them, exactly as when these were separate round trips");
    }

    [Test]
    public async Task ClearsTheMisfireOriginalFireTimeWhenAskedTo()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.ApplyTriggerFired(conn, CreateUpdate(clearMisfireOriginalFireTime: true));

        connection.Batches[0].Commands.Should().ContainSingle(
            x => x.CommandText.Contains("MISFIRE_INSTR = @misfireOrigFireTime", StringComparison.Ordinal) ||
                 x.CommandText.Contains("MISFIRE_ORIG_FIRE_TIME = @misfireOrigFireTime", StringComparison.Ordinal));
    }

    /// <summary>
    /// A provider without <see cref="DbConnection.CanCreateBatch" /> — which is the default, and what
    /// SQLite and several other drivers report — has to keep issuing the statements it always did.
    /// </summary>
    [Test]
    public async Task FallsBackToIndividualStatements_WhenProviderCannotBatch()
    {
        StubBatchingConnection connection = new() { SupportsBatching = false };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.ApplyTriggerFired(conn, CreateUpdate(blockJobTriggers: true));

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().HaveCount(6, "every statement the batch would have carried still has to be issued");
        del.PreparedCommands[0].Should().StartWith("UPDATE QRTZ_FIRED_TRIGGERS");
        del.PreparedCommands.Last().Should().Contain("QRTZ_SIMPLE_TRIGGERS");
    }

    /// <summary>
    /// A batch fails as a unit, so a failure that is not the connection's fault is replayed statement by
    /// statement — both so the exception names the statement that actually failed, and because every
    /// statement a fire writes is an UPDATE with an absolute value, which replays to the same row state.
    /// </summary>
    [Test]
    public async Task ReplaysIndividually_WhenTheBatchFailsForANonTransientReason()
    {
        StubBatchingConnection connection = new() { FailBatchExecution = true };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.ApplyTriggerFired(conn, CreateUpdate());

        connection.Batches.Should().HaveCount(1, "the batch should have been attempted");
        del.PreparedCommands.Should().HaveCount(3, "every statement should still have been issued after the batch failed");
    }

    /// <summary>
    /// The store's retry only recognises a transient failure from the exception it is handed, and a
    /// replay against a dropped connection — or a transaction the server has already doomed — hands it
    /// something else entirely. So a transient batch failure surfaces as itself.
    /// </summary>
    [Test]
    public async Task DoesNotReplay_WhenTheBatchFailedForATransientReason()
    {
        StubBatchingConnection connection = new() { BatchFailure = () => new TimeoutException("connection reset") };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        Func<Task> act = async () => await del.ApplyTriggerFired(conn, CreateUpdate());

        await act.Should().ThrowAsync<TimeoutException>(
            "the caller decides what to do about a transient failure and can only do so if it sees one");
        del.PreparedCommands.Should().BeEmpty("replaying onto a connection that just dropped can only produce a different, unrecognisable failure");
    }

    private static TriggerFiredUpdate CreateUpdate(
        SimpleTriggerImpl trigger = null,
        DateTimeOffset? scheduledFireTimeUtc = null,
        bool clearMisfireOriginalFireTime = false,
        bool blockJobTriggers = false)
    {
        trigger ??= CreateTrigger();

        return new TriggerFiredUpdate
        {
            Trigger = trigger,
            JobDetail = JobBuilder.Create<FireTestJob>().WithIdentity(trigger.JobKey).Build(),
            NewState = blockJobTriggers ? StoredTriggerState.Blocked : StoredTriggerState.Waiting,
            StoredTriggerType = AdoConstants.TriggerTypeSimple,
            ScheduledFireTimeUtc = scheduledFireTimeUtc ?? trigger.NextFireTimeUtc,
            ClearMisfireOriginalFireTime = clearMisfireOriginalFireTime,
            BlockJobTriggers = blockJobTriggers,
        };
    }

    private static SimpleTriggerImpl CreateTrigger()
    {
        SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey("t1", "g1"),
            JobKey = new JobKey("j1", "jg1"),
            StartTimeUtc = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            RepeatInterval = TimeSpan.FromMinutes(1),
            FireInstanceId = "fire-1",
        };
        trigger.NextFireTimeUtc = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        return trigger;
    }

    private sealed class FireTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
