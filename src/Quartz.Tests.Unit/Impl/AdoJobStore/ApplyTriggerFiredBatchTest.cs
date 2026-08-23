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

    /// <summary>
    /// The column was dead schema until the fire-instance listing needed to read it back, and a trigger
    /// with no execution group has to write a null rather than leave whatever was there before.
    /// </summary>
    [Test]
    public async Task WritesTheExecutionGroupOntoTheFiredTriggerRow()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        SimpleTriggerImpl grouped = CreateTrigger();
        grouped.ExecutionGroup = "reports";
        await del.ApplyTriggerFired(conn, CreateUpdate(grouped));

        StubBatchingConnection ungroupedConnection = new();
        await del.ApplyTriggerFired(new ConnectionAndTransactionHolder(ungroupedConnection, null), CreateUpdate());

        FiredTriggerParameter(connection, "@executionGroup").Should().Be("reports");
        FiredTriggerParameter(ungroupedConnection, "@executionGroup").Should().Be(DBNull.Value,
            "a trigger with no execution group writes a null rather than leaving the column stale");
    }

    private static object FiredTriggerParameter(StubBatchingConnection connection, string name)
    {
        return connection.Batches[0].Commands
            .Single(x => x.CommandText.StartsWith("UPDATE QRTZ_FIRED_TRIGGERS", StringComparison.Ordinal))
            .Parameters.Cast<DbParameter>().Single(x => x.ParameterName == name).Value;
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

    /// <summary>
    /// Every trigger type Quartz ships describes its own schedule, so every one of them travels in the
    /// batch rather than costing a round trip of its own.
    /// </summary>
    [TestCase(AdoConstants.TriggerTypeSimple, "QRTZ_SIMPLE_TRIGGERS")]
    [TestCase(AdoConstants.TriggerTypeCron, "QRTZ_CRON_TRIGGERS")]
    [TestCase(AdoConstants.TriggerTypeCalendarInterval, "QRTZ_SIMPROP_TRIGGERS")]
    [TestCase(AdoConstants.TriggerTypeDailyTimeInterval, "QRTZ_SIMPROP_TRIGGERS")]
    public async Task DescribesTheScheduleOfEveryShippedTriggerType(string discriminator, string expectedTable)
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        IOperableTrigger trigger = CreateTriggerOfType(discriminator);

        await del.ApplyTriggerFired(conn, CreateUpdate(trigger, storedTriggerType: discriminator));

        connection.Batches.Should().HaveCount(1);
        connection.Batches[0].Commands.Should().ContainSingle(x => x.CommandText.Contains(expectedTable, StringComparison.Ordinal));
        del.PreparedCommands.Should().BeEmpty("a delegate that can describe its update must not also be given a round trip");
    }

    /// <summary>
    /// A persistence delegate written before the describe member existed does not implement it, and its
    /// default says so. The trigger's own row still batches; the schedule falls back to its own command.
    /// </summary>
    [Test]
    public async Task GivesTheScheduleItsOwnRoundTrip_WhenThePersistenceDelegateCannotDescribeIt()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        UndescribingPersistenceDelegate persistenceDelegate = new();
        del.AddTriggerPersistenceDelegate(persistenceDelegate);

        UndescribableTrigger trigger = new()
        {
            Key = new TriggerKey("t1", "g1"),
            JobKey = new JobKey("j1", "jg1"),
            StartTimeUtc = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero),
            FireInstanceId = "fire-1",
        };
        trigger.NextFireTimeUtc = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

        await del.ApplyTriggerFired(conn, CreateUpdate(trigger, storedTriggerType: UndescribingPersistenceDelegate.Discriminator));

        connection.Batches[0].Commands.Should().HaveCount(2, "the fired-trigger row and the trigger's own row still travel together");
        persistenceDelegate.UpdateCalls.Should().Be(1, "the delegate that could not describe its update must still be asked to issue it");
    }

    [Test]
    public async Task WritesTheJobDataMapOnlyWhenItIsDirty()
    {
        StubBatchingConnection clean = new();
        CountingDelegate del = CountingDelegate.Create();
        await del.ApplyTriggerFired(new ConnectionAndTransactionHolder(clean, null), CreateUpdate());

        SimpleTriggerImpl dirty = CreateTrigger();
        dirty.JobDataMap["changed"] = "yes";
        StubBatchingConnection dirtyConnection = new();
        await del.ApplyTriggerFired(new ConnectionAndTransactionHolder(dirtyConnection, null), CreateUpdate(dirty));

        TriggerRowUpdate(clean).Should().NotContain("JOB_DATA = @triggerJobJobDataMap",
            "serializing and shipping a blob that did not change is work for nothing");
        TriggerRowUpdate(dirtyConnection).Should().Contain("JOB_DATA = @triggerJobJobDataMap");
    }

    /// <summary>
    /// A trigger on the fire path carries the pin it was loaded with, and writing that back would clobber
    /// a concurrent re-pin. Only a pin this instance actually changed is written.
    /// </summary>
    [Test]
    public async Task WritesThePreferredNodeOnlyWhenThePinWasChangedHere()
    {
        StubBatchingConnection loaded = new();
        CountingDelegate del = CountingDelegate.Create();
        await del.ApplyTriggerFired(new ConnectionAndTransactionHolder(loaded, null), CreateUpdate());

        SimpleTriggerImpl claimed = CreateTrigger();
        claimed.SetPreferredNode(PreferredNode.ClaimedBy("NODE-01"), markDirty: true);
        StubBatchingConnection claimedConnection = new();
        await del.ApplyTriggerFired(new ConnectionAndTransactionHolder(claimedConnection, null), CreateUpdate(claimed));

        TriggerRowUpdate(loaded).Should().NotContain("PREFERRED_NODE = @triggerPreferredNode");
        TriggerRowUpdate(claimedConnection).Should().Contain("PREFERRED_NODE = @triggerPreferredNode");
    }

    [Test]
    public async Task IssuesEveryJobTriggerTransitionInOneBatch()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.UpdateTriggerStatesForJobFromOtherState(conn, new JobKey("j1", "jg1"),
        [
            new TriggerStateTransition(StoredTriggerState.Blocked, StoredTriggerState.Waiting),
            new TriggerStateTransition(StoredTriggerState.PausedBlocked, StoredTriggerState.Paused)
        ]);

        connection.Batches.Should().HaveCount(1);
        connection.Batches[0].Commands.Should().HaveCount(2);
        del.PreparedCommands.Should().BeEmpty();
    }

    [Test]
    public async Task IssuesNothingForAnEmptyTransitionList()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.UpdateTriggerStatesForJobFromOtherState(conn, new JobKey("j1", "jg1"), []);

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().BeEmpty();
    }

    private static string TriggerRowUpdate(StubBatchingConnection connection)
    {
        return connection.Batches[0].Commands
            .Single(x => x.CommandText.StartsWith("UPDATE QRTZ_TRIGGERS SET JOB_NAME", StringComparison.Ordinal))
            .CommandText;
    }

    private static IOperableTrigger CreateTriggerOfType(string discriminator)
    {
        DateTimeOffset start = new(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
        IOperableTrigger trigger = discriminator switch
        {
            AdoConstants.TriggerTypeSimple => CreateTrigger(),
            AdoConstants.TriggerTypeCron => new CronTriggerImpl
            {
                Key = new TriggerKey("t1", "g1"),
                JobKey = new JobKey("j1", "jg1"),
                StartTimeUtc = start,
                CronExpressionString = "0 0 * * * ?",
            },
            AdoConstants.TriggerTypeCalendarInterval => new CalendarIntervalTriggerImpl
            {
                Key = new TriggerKey("t1", "g1"),
                JobKey = new JobKey("j1", "jg1"),
                StartTimeUtc = start,
                RepeatIntervalUnit = IntervalUnit.Day,
                RepeatInterval = 1,
            },
            AdoConstants.TriggerTypeDailyTimeInterval => new DailyTimeIntervalTriggerImpl
            {
                Key = new TriggerKey("t1", "g1"),
                JobKey = new JobKey("j1", "jg1"),
                StartTimeUtc = start,
                StartTimeOfDay = new TimeOnly(9, 0),
                EndTimeOfDay = new TimeOnly(17, 0),
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 30,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(discriminator), discriminator, "unknown trigger type"),
        };

        trigger.FireInstanceId = "fire-1";
        trigger.NextFireTimeUtc = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        return trigger;
    }

    private static TriggerFiredUpdate CreateUpdate(
        IOperableTrigger trigger = null,
        DateTimeOffset? scheduledFireTimeUtc = null,
        bool clearMisfireOriginalFireTime = false,
        bool blockJobTriggers = false,
        string storedTriggerType = AdoConstants.TriggerTypeSimple)
    {
        trigger ??= CreateTrigger();

        return new TriggerFiredUpdate
        {
            Trigger = trigger,
            JobDetail = JobBuilder.Create<FireTestJob>().WithIdentity(trigger.JobKey).Build(),
            NewState = blockJobTriggers ? StoredTriggerState.Blocked : StoredTriggerState.Waiting,
            StoredTriggerType = storedTriggerType,
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

    /// <summary>
    /// A persistence delegate as one written before the describe member existed: it does not implement
    /// it, so it takes the interface's default and is issued its own command.
    /// </summary>
    private sealed class UndescribingPersistenceDelegate : ITriggerPersistenceDelegate
    {
        public const string Discriminator = "UNDESCRIBABLE";

        public int UpdateCalls { get; private set; }

        public void Initialize(TriggerPersistenceDelegateContext context)
        {
        }

        public bool CanHandleTriggerType(IOperableTrigger trigger) => trigger is UndescribableTrigger;

        public string GetHandledTriggerTypeDiscriminator() => Discriminator;

        public ValueTask<int> InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, StoredTriggerState state, IJobDetail jobDetail, CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<int> UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, StoredTriggerState state, IJobDetail jobDetail, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return new ValueTask<int>(1);
        }

        public ValueTask<int> DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs) => throw new NotSupportedException();
    }

    /// <summary>
    /// Carries something a <see cref="SimpleTriggerImpl" /> does not, which is what makes the built-in
    /// simple-trigger delegate decline it and the one above claim it.
    /// </summary>
    private sealed class UndescribableTrigger : SimpleTriggerImpl
    {
        public override bool HasAdditionalProperties => true;
    }
}
