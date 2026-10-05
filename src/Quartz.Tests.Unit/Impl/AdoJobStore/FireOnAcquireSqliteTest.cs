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

using System.Data.Common;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Triggers already due are acquired and fired in one transaction (#3864), and the rows that transaction
/// leaves are the rows acquiring and then firing them left: each fired trigger's row <c>EXECUTING</c>
/// with its job named, each trigger moved on, and every trigger still <c>ACQUIRED</c> reserved.
/// </summary>
/// <remarks>
/// <para>
/// The failure cases are the ones a combined transaction makes harder. A fire that fails for good rolls
/// the round back, acquisition and all (#3931): the round runs again with the failed trigger claimed and
/// reserved but not fired, its job-mates fire once, and it is counted towards
/// <see cref="AdoJobStoreOptions.MaxConsecutiveFireFailures" /> and stored <c>ERROR</c> at the limit as a
/// failed fire of an acquired trigger is (#3963).
/// </para>
/// <para>
/// One store on a SQLite file, which acquires within the lock and so takes the combined round for any
/// batch size, driven by hand, with the shipped SQLite delegate recording and failing the fires it is
/// told to after their writes have gone out.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class FireOnAcquireSqliteTest
{
    private const string Group = "fire-on-acquire";
    private static readonly JobKey serialJobKey = new("serial", Group);
    private static readonly JobKey ordinaryJobKey = new("ordinary", Group);
    private static readonly JobKey brokenJobKey = new("broken", Group);

    /// <summary><c>AdoJobStoreOptions.MaxConsecutiveFireFailures</c> as it ships.</summary>
    private const int DefaultMaxConsecutiveFireFailures = 5;

    /// <summary>Log event <c>AdoJobStoreLog.FailingTriggerParkedInError</c>.</summary>
    private const int FailingTriggerParkedInError = 3050;

    /// <summary>Log event <c>AdoJobStoreLog.TriggerHasNoNextFireTime</c>.</summary>
    private const int TriggerHasNoNextFireTime = 3028;

    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    private SqliteTestDatabase database = null!;
    private RecordingLoggerProvider logs = null!;
    private ServiceProvider node = null!;
    private IScheduler scheduler = null!;
    private IJobStore store = null!;

    [SetUp]
    public async Task CreateNode()
    {
        database = new SqliteTestDatabase("fire-on-acquire");
        RecordingSqliteDelegate.Reset();
        RecordingJob.Reset();
        logs = new RecordingLoggerProvider();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "fire-on-acquire";
                options.InstanceId = "node";
                options.MaxBatchSize = 5;
            });

            q.UsePersistentStore(persistent =>
            {
                // Before UseSqlite, which registers the delegate it names: the registrations are try-add.
                persistent.UseDriverDelegate<RecordingSqliteDelegate>();
                persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                persistent.ProvisionSchema();
                persistent.ConfigureStore(options => options.TransientRetryInterval = TimeSpan.Zero);
            });
        });

        node = services.BuildServiceProvider();
        scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        store = node.GetRequiredService<IJobStore>();

        await scheduler.AddJob(JobBuilder.Create<SerialJob>().WithIdentity(serialJobKey).StoreDurably().Build());
        await scheduler.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(ordinaryJobKey).StoreDurably().Build());
        await scheduler.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(brokenJobKey).StoreDurably().Build());
    }

    [TearDown]
    public async Task DisposeNode()
    {
        await node.DisposeAsync();
        logs.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// Two triggers due now and one due later in the window: the two are fired in the round and write
    /// their rows as <c>EXECUTING</c> naming the job; the third is reserved, and fires later as an
    /// acquired trigger always has.
    /// </summary>
    [Test]
    public async Task DueTriggersAreFiredInTheTransactionThatAcquiresThemAndTheRestAreReserved()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("due-1", ordinaryJobKey, now, priority: 10);
        await Schedule("due-2", ordinaryJobKey, now, priority: 5);
        await Schedule("later", ordinaryJobKey, now.AddSeconds(3));

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 5));

        round.Due.Select(x => x.Key.Name).Should().Equal(["due-1", "due-2"]);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        round.Pending.Select(x => x.Key.Name).Should().Equal(["later"]);

        (await FiredRow("due-1")).Should().Be(("EXECUTING", "ordinary"), "the fire inserts the row a 4.3 fire leaves: EXECUTING, naming the job");
        (await FiredRow("due-2")).Should().Be(("EXECUTING", "ordinary"));
        (await FiredRow("later")).Should().Be(("ACQUIRED", null), "not due, so reserved as acquisition has always reserved it, naming no job");
        (await TriggerState("due-1")).Should().Be("COMPLETE", "a one-off that fired has nothing left to fire");
        (await AcquiredWithoutAFiredRow()).Should().Be(0);

        RecordingSqliteDelegate.Fires.Should().Equal([("due-1", true), ("due-2", true)], "each fire writes its row, rather than updating a reservation");
        RecordingSqliteDelegate.HeaderReads.Should().Be(0, "the round reads the headers of all its triggers at once");

        List<TriggerFiredResult> later = await store.TriggersFired(round.Pending);

        later.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull("a pending trigger fires as an acquired one always did");
        (await FiredRow("later")).Should().Be(("EXECUTING", "ordinary"));
        RecordingSqliteDelegate.Fires.Should().EndWith(("later", false), "and updates the reservation its acquisition wrote");
    }

    /// <summary>
    /// A round of three whose middle fire fails for good (#3931): the round is rolled back, acquisition
    /// and all, and run again with the failed trigger claimed and reserved but not fired. The two beside
    /// it fire once each, its job-mate is not left <c>BLOCKED</c>, and nothing is left <c>ACQUIRED</c>
    /// without a fired row.
    /// </summary>
    [Test]
    public async Task AFailedFireRollsTheRoundBackAndTheRestOfItFiresOnce()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("ordinary-1", ordinaryJobKey, now, priority: 10);
        await Schedule("poison", serialJobKey, now, priority: 9);
        await Schedule("ordinary-2", ordinaryJobKey, now, priority: 8);
        await Schedule("sibling", serialJobKey, now, priority: 7);
        RecordingSqliteDelegate.FailFireOf = "poison";

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 4));

        round.Due.Select(x => x.Key.Name).Should().Equal(["ordinary-1", "poison", "ordinary-2"],
            "a round takes one trigger of a serial job, so the sibling stays behind");
        round.Fired[0].TriggerFiredBundle.Should().NotBeNull();
        round.Fired[1].TriggerFiredBundle.Should().BeNull("the poison fire failed");
        round.Fired[1].IsDeclined.Should().BeFalse();
        round.Fired[1].Exception.Should().BeOfType<JobPersistenceException>()
            .Which.InnerException.Should().BeAssignableTo<DbException>("the driver's own exception, which the scheduler thread releases the trigger on");
        round.Fired[2].TriggerFiredBundle.Should().NotBeNull();

        RecordingSqliteDelegate.Fires.Select(x => x.Trigger).Should().Equal(["ordinary-1", "poison", "ordinary-1", "ordinary-2"],
            "the attempt that met the failure is rolled back whole, and the round runs again without firing the failed trigger");
        (await FiredRowCount("ordinary-1")).Should().Be(1, "rolled back and fired again is one firing, not two");
        (await FiredRow("ordinary-1")).Should().Be(("EXECUTING", "ordinary"));
        (await FiredRow("ordinary-2")).Should().Be(("EXECUTING", "ordinary"));
        (await TriggerState("poison")).Should().Be("ACQUIRED", "claimed again by the run after the failure, and the scheduler's to release");
        (await FiredRow("poison")).Should().Be(("ACQUIRED", null), "reserved as acquisition reserves a trigger it does not fire");
        (await TriggerState("sibling")).Should().Be("WAITING",
            "the poison fire's BLOCKED of its job-mates went with the rollback; left BLOCKED, nothing executing would ever let go of it");
        (await AcquiredWithoutAFiredRow()).Should().Be(0, "nothing the round claimed is left ACQUIRED with no fired row");

        // What the scheduler thread does with a failed result.
        await store.ReleaseAcquiredTrigger(round.Due[1]);

        (await TriggerState("poison")).Should().Be("WAITING", "released, for the next round to pick up");
        (await FiredRowCount("poison")).Should().Be(0);
    }

    /// <summary>
    /// A trigger whose every fire fails, first in every round (#3963): the round in which it reaches the
    /// limit stores it <c>ERROR</c>, says so once, and its serial job-mate fires in the round after.
    /// </summary>
    [Test]
    public async Task ATriggerWhoseEveryFireFailsIsStoredErrorOnTheLimitAndItsJobMateFires()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("poison", serialJobKey, now, priority: 10);
        await Schedule("sibling", serialJobKey, now, priority: 1);
        ISchedulerListener listener = A.Fake<ISchedulerListener>();
        A.CallTo(() => listener.Name).Returns("error-listener");
        scheduler.ListenerManager.AddSchedulerListener(listener);
        RecordingSqliteDelegate.FailFireOf = "poison";

        for (int failure = 1; failure <= DefaultMaxConsecutiveFireFailures; failure++)
        {
            TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

            round.Due.Select(x => x.Key.Name).Should().Equal(["poison"], "one trigger of the serial job a round, and the poison leads");
            round.Fired[0].Exception.Should().NotBeNull();

            await store.ReleaseAcquiredTrigger(round.Due[0]);

            string expected = failure < DefaultMaxConsecutiveFireFailures ? "WAITING" : "ERROR";
            (await TriggerState("poison")).Should().Be(expected, $"after {failure} failure(s) in a row");
        }

        A.CallTo(() => listener.TriggerInError(A<IScheduler>._, new TriggerKey("poison", Group), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerParkedInError);
        RecordingSqliteDelegate.Fires.Count(x => x.Trigger == "poison").Should().Be(DefaultMaxConsecutiveFireFailures,
            "stored ERROR on the last failure it allows, it is not fired again");

        TriggerAcquisitionResult next = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));
        next.Due.Select(x => x.Key.Name).Should().Equal(["sibling"], "the job-mate the poison kept out of every round is first now");
        next.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
    }

    /// <summary>
    /// A transient failure is the transaction wrapper's to retry, and it retries the round whole: every
    /// trigger in it fires once.
    /// </summary>
    [Test]
    public async Task ATransientFailureRetriesTheWholeRound()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("ordinary-1", ordinaryJobKey, now, priority: 10);
        await Schedule("ordinary-2", ordinaryJobKey, now, priority: 5);
        RecordingSqliteDelegate.FailFireOfTransientlyOnce = "ordinary-2";

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

        round.Fired.Should().HaveCount(2).And.OnlyContain(x => x.TriggerFiredBundle != null);
        RecordingSqliteDelegate.Fires.Select(x => x.Trigger).Should().Equal(["ordinary-1", "ordinary-2", "ordinary-1", "ordinary-2"]);
        (await FiredRowCount("ordinary-1")).Should().Be(1);
        (await FiredRowCount("ordinary-2")).Should().Be(1);
    }

    /// <summary>
    /// A job whose stored data will not read is settled in the round as the fire of an acquired trigger
    /// settles it: its trigger stored <c>ERROR</c>, and the fire beside it committed.
    /// </summary>
    [Test]
    public async Task AJobThatWillNotLoadIsStoredErrorAndTheRoundCommitsThat()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("ordinary-1", ordinaryJobKey, now, priority: 10);
        await Schedule("broken-1", brokenJobKey, now, priority: 5);

        // Its data map no longer deserializes, as a serializer change underneath stored rows would make it.
        await ExecuteNonQuery("UPDATE QRTZ_JOB_DETAILS SET JOB_DATA = X'00FF' WHERE JOB_NAME = @name", "broken");

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

        round.Due.Select(x => x.Key.Name).Should().Equal(["ordinary-1", "broken-1"]);
        round.Fired[0].TriggerFiredBundle.Should().NotBeNull();
        round.Fired[1].TriggerFiredBundle.Should().BeNull();
        round.Fired[1].Exception.Should().BeOfType<JobPersistenceException>()
            .Which.InnerException.Should().NotBeAssignableTo<DbException>("the database refused nothing; the job's stored data is what failed");
        (await TriggerState("broken-1")).Should().Be("ERROR", "the store settled the trigger, and the round committed it");
        (await FiredRow("ordinary-1")).Should().Be(("EXECUTING", "ordinary"));
        RecordingSqliteDelegate.Fires.Select(x => x.Trigger).Should().Equal(["ordinary-1"], "nothing was rolled back, so nothing was fired twice");
    }

    /// <summary>
    /// Two due triggers of one job that disallows concurrent execution, both let into one round, with the
    /// round's fire writes deferred until every fire is decided. The first fires; the second is held back
    /// although the fired-trigger table cannot say the job is executing yet, and is left <c>BLOCKED</c> by
    /// the first one's fire, as a fire of an acquired trigger leaves it.
    /// </summary>
    /// <remarks>
    /// Acquisition keeps a second trigger of such a job out of the round by the flag the delegate reads
    /// with each candidate. A delegate that does not read it leaves acquisition asking the job's type,
    /// which a job made serial by its builder does not carry — so this is how two get in.
    /// </remarks>
    [Test]
    public async Task TwoTriggersOfASerialJobInOneRoundFireOnceAndTheOtherIsLeftBlocked()
    {
        JobKey builderSerialJobKey = new("builder-serial", Group);
        await scheduler.AddJob(JobBuilder.Create<RecordingJob>()
            .WithIdentity(builderSerialJobKey)
            .DisallowConcurrentExecution()
            .StoreDurably()
            .Build());

        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("serial-a", builderSerialJobKey, now, priority: 10);
        await Schedule("serial-b", builderSerialJobKey, now, priority: 5);
        RecordingSqliteDelegate.ForgetConcurrencyFlag = true;

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

        round.Due.Select(x => x.Key.Name).Should().Equal(["serial-a", "serial-b"],
            "without the stored flag, acquisition asks the job's type, which carries no attribute");
        round.Fired[0].TriggerFiredBundle.Should().NotBeNull();
        round.Fired[1].TriggerFiredBundle.Should().BeNull(
            "the job is running from the first fire on, though that fire's row is not written until the round's writes go out");
        round.Fired[1].IsDeclined.Should().BeFalse();
        round.Fired[1].Exception.Should().BeNull("held back is not failed, and is not counted towards parking the trigger");

        RecordingSqliteDelegate.Fires.Should().Equal([("serial-a", true)], "one fire of the job is written, and only one");
        (await FiredRow("serial-a")).Should().Be(("EXECUTING", "builder-serial"));
        (await FiredRow("serial-b")).Should().Be(("ACQUIRED", null), "reserved as acquisition reserves a trigger it does not fire");
        (await TriggerState("serial-b")).Should().Be("BLOCKED", "the first fire blocks every other trigger of its job");
        (await AcquiredWithoutAFiredRow()).Should().Be(0);

        // What the scheduler thread does with a trigger that did not fire.
        await store.ReleaseAcquiredTrigger(round.Due[1]);

        (await TriggerState("serial-b")).Should().Be("BLOCKED", "a release lets go of a reservation, not of the block");
        (await FiredRowCount("serial-b")).Should().Be(0);

        await store.TriggeredJobComplete(round.Fired[0].TriggerFiredBundle!.Trigger, round.Fired[0].TriggerFiredBundle!.JobDetail, SchedulerInstruction.NoInstruction);

        (await TriggerState("serial-b")).Should().Be("WAITING", "the completion lets the job's other trigger go");
    }

    /// <summary>
    /// A candidate another node fired between this round's read of the candidates and its read-back of their
    /// rows. Fired as it was acquired, a one-off is <c>COMPLETE</c> with no fire time until its completion
    /// deletes it, so the read-back finds no fire time: a race lost, not a trigger with bad data. The round
    /// takes the rest and says nothing.
    /// </summary>
    [Test]
    public async Task ACandidateAnotherNodeFiredMeanwhileIsARaceLostNotABadTrigger()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("taken", ordinaryJobKey, now, priority: 10);
        await Schedule("ours", ordinaryJobKey, now, priority: 5);
        RecordingSqliteDelegate.FireElsewhereBeforeReadBack = "taken";

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

        round.Due.Select(x => x.Key.Name).Should().Equal(["ours"], "the other node has the one it fired");
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        logs.Entries.Should().NotContain(x => x.EventId.Id == TriggerHasNoNextFireTime,
            "the warning is for a waiting trigger with no fire time, which an operator has to fix by hand; this one is spent and its completion deletes it");
    }

    /// <summary>
    /// A waiting row that reads back with no fire time is what the warning is for: a trigger whose stored
    /// form has lost its schedule, which nothing but an operator will clear.
    /// </summary>
    [Test]
    public async Task AWaitingTriggerThatReadsBackWithNoFireTimeIsStillWarnedAbout()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("broken-schedule", ordinaryJobKey, now, priority: 10);
        await Schedule("ours", ordinaryJobKey, now, priority: 5);
        RecordingSqliteDelegate.LoseFireTimeOnReadBack = "broken-schedule";

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestFor(maxCount: 2));

        round.Due.Select(x => x.Key.Name).Should().Equal(["ours"]);
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerHasNoNextFireTime, "the row is still waiting, so this is bad data and not a race");
        (await TriggerState("broken-schedule")).Should().Be("WAITING");
    }

    /// <summary>
    /// A running scheduler handed a round of triggers that are already due fires them without the second
    /// transaction.
    /// </summary>
    [Test]
    public async Task ARunningSchedulerFiresADueRoundInTheTransactionThatAcquiresIt()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        for (int i = 0; i < 5; i++)
        {
            await Schedule("one-off-" + i, ordinaryJobKey, now);
        }

        await scheduler.Start();
        try
        {
            DateTimeOffset giveUp = DateTimeOffset.UtcNow + observationDeadline;
            while (RecordingJob.Runs < 5 && DateTimeOffset.UtcNow < giveUp)
            {
                await Task.Delay(20);
            }
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        RecordingJob.Runs.Should().Be(5, "every one-off runs once");
        RecordingSqliteDelegate.Fires.Should().HaveCount(5).And.OnlyContain(x => x.FiredOnAcquire,
            "each was due when the scheduler acquired it, so each was fired in the acquisition's transaction; a fire "
            + "by TriggersFired, the second transaction, updates a reservation and would be recorded as one");
    }

    private static TriggerAcquisitionRequest RequestFor(int maxCount)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(5),
            MaxCount = maxCount,
            TimeWindow = TimeSpan.FromSeconds(5),
        };
    }

    private async Task Schedule(string name, JobKey job, DateTimeOffset at, int priority = TriggerConstants.DefaultPriority)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .WithPriority(priority)
            .Build());
    }

    private Task<string?> TriggerState(string triggerName)
    {
        return ReadScalar<string>("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    private async Task<(string? State, string? JobName)> FiredRow(string triggerName)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT STATE, JOB_NAME FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", triggerName);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return (null, null);
        }

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<long> FiredRowCount(string triggerName)
    {
        return await ReadScalar<long>("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    /// <summary>
    /// Triggers stored <c>ACQUIRED</c> with no fired-trigger row behind them, which nothing — no release,
    /// no recovery — would ever let go of.
    /// </summary>
    private async Task<long> AcquiredWithoutAFiredRow()
    {
        return await ReadScalar<long>(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS t WHERE t.TRIGGER_STATE = 'ACQUIRED' AND NOT EXISTS "
            + "(SELECT 1 FROM QRTZ_FIRED_TRIGGERS f WHERE f.TRIGGER_NAME = t.TRIGGER_NAME AND f.TRIGGER_GROUP = t.TRIGGER_GROUP)",
            name: null);
    }

    private async Task<T?> ReadScalar<T>(string sql, string? name)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (name is not null)
        {
            command.Parameters.AddWithValue("@name", name);
        }

        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T) value;
    }

    private async Task ExecuteNonQuery(string sql, string name)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    /// <summary>
    /// The shipped SQLite delegate, recording every fire it writes and every header it reads one trigger
    /// at a time, and failing the fire it is told to after the fire's writes have gone out.
    /// </summary>
    /// <remarks>
    /// Static, because the container builds the delegate; the fixture zeroes it before each test.
    /// </remarks>
    public sealed class RecordingSqliteDelegate : SQLiteDelegate
    {
        private static readonly List<(string Trigger, bool FiredOnAcquire)> fires = [];
        private static int headerReads;

        /// <summary>The trigger whose fire fails, or <see langword="null" /> for none.</summary>
        public static string? FailFireOf { get; set; }

        /// <summary>The trigger whose next fire fails as a busy database would make it fail, once.</summary>
        public static string? FailFireOfTransientlyOnce { get; set; }

        /// <summary>
        /// Whether acquisition candidates come back without the job's stored concurrency flag, as from a
        /// delegate whose acquisition query does not read it.
        /// </summary>
        public static bool ForgetConcurrencyFlag { get; set; }

        /// <summary>
        /// The trigger another node fires as it acquires it, between the acquisition's read of the candidates
        /// and its read-back of their rows; once.
        /// </summary>
        public static string? FireElsewhereBeforeReadBack { get; set; }

        /// <summary>
        /// The trigger whose read-back comes without a fire time while its row still waits, as a trigger
        /// whose stored form lost its schedule would; once.
        /// </summary>
        public static string? LoseFireTimeOnReadBack { get; set; }

        /// <summary>Every fire the delegate was asked to write, in order, a rolled-back attempt's included.</summary>
        public static List<(string Trigger, bool FiredOnAcquire)> Fires
        {
            get
            {
                lock (fires)
                {
                    return [.. fires];
                }
            }
        }

        /// <summary>How many times one trigger's header was read on its own.</summary>
        public static int HeaderReads => Volatile.Read(ref headerReads);

        public static void Reset()
        {
            lock (fires)
            {
                fires.Clear();
            }

            Volatile.Write(ref headerReads, 0);
            FailFireOf = null;
            FailFireOfTransientlyOnce = null;
            ForgetConcurrencyFlag = false;
            FireElsewhereBeforeReadBack = null;
            LoseFireTimeOnReadBack = null;
        }

        public override async ValueTask<List<IOperableTrigger>> SelectTriggers(ConnectionAndTransactionHolder conn, IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            if (FireElsewhereBeforeReadBack is { } name && triggerKeys.Any(key => key.Name == name))
            {
                FireElsewhereBeforeReadBack = null;

                // What the other node's commit leaves: a spent one-off, still there for its completion to delete.
                using DbCommand command = conn.Connection.CreateCommand();
                conn.Attach(command);
                command.CommandText = "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'COMPLETE', NEXT_FIRE_TIME = NULL WHERE TRIGGER_NAME = @name";
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = "@name";
                parameter.Value = name;
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            List<IOperableTrigger> triggers = await base.SelectTriggers(conn, triggerKeys, cancellationToken);
            if (LoseFireTimeOnReadBack is { } lost && triggers.Find(trigger => trigger.Key.Name == lost) is { } broken)
            {
                LoseFireTimeOnReadBack = null;
                broken.NextFireTimeUtc = null;
            }

            return triggers;
        }

        // Every override below calls the base implementation, so the round's own members leave none of them
        // bypassed, and the round takes its shipped shape.
        public override bool SupportsFireOnAcquire => true;

        public override async ValueTask<List<TriggerAcquireResult>> SelectTriggersToAcquire(ConnectionAndTransactionHolder conn, TriggerAcquisitionCriteria criteria, CancellationToken cancellationToken = default)
        {
            List<TriggerAcquireResult> candidates = await base.SelectTriggersToAcquire(conn, criteria, cancellationToken);
            return ForgetConcurrencyFlag
                ? candidates.ConvertAll(candidate => candidate with { ConcurrentExecutionDisallowed = null })
                : candidates;
        }

        public override ValueTask<StoredTriggerHeader?> SelectTriggerHeader(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref headerReads);
            return base.SelectTriggerHeader(conn, triggerKey, cancellationToken);
        }

        public override async ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
        {
            string triggerName = update.Trigger.Key.Name;
            lock (fires)
            {
                fires.Add((triggerName, update.FiredOnAcquire));
            }

            await base.ApplyTriggerFired(conn, update, cancellationToken);

            if (string.Equals(triggerName, FailFireOf, StringComparison.Ordinal))
            {
                // A real exception from the driver, and one nothing classifies as transient.
                using DbCommand command = conn.Connection.CreateCommand();
                conn.Attach(command);
                command.CommandText = "SELECT 1 FROM QRTZ_NO_SUCH_TABLE";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (string.Equals(triggerName, FailFireOfTransientlyOnce, StringComparison.Ordinal))
            {
                FailFireOfTransientlyOnce = null;
                throw new SqliteException("database is locked", 5 /* SQLITE_BUSY, which the store retries */);
            }
        }
    }

    /// <summary>Counts its runs.</summary>
    public sealed class RecordingJob : IJob
    {
        private static int runs;

        public static int Runs => Volatile.Read(ref runs);

        public static void Reset() => Volatile.Write(ref runs, 0);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref runs);
            return default;
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
