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
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A calendar that throws while the database job store handles a misfire fails that trigger alone: the
/// transaction it runs in commits, and the failure counts toward
/// <see cref="JobStoreSupport.MaxConsecutiveFireFailures" /> as a failed fire does (#4006).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="DisallowConcurrentExecutionAttribute" /> job's completion handles the misfires of the
/// triggers it unblocks, inside its transaction and lock. A throw there rolled the whole completion back,
/// and the completion retries until it succeeds: the job stayed <c>BLOCKED</c>, its fired row stayed, and
/// the worker that ran it never returned. The misfire handler caught the throw, but never stored the
/// trigger <c>ERROR</c>, so it was logged on every scan and could hold the head of every batch.
/// </para>
/// <para>
/// A database failure in the same step is not the trigger's: it still rolls the work back to be retried.
/// </para>
/// <para>
/// One <see cref="JobStoreTX" /> on a SQLite file, initialized and never started, driven by hand on a
/// clock the test sets, with the shipped SQLite delegate handing out a calendar the test controls.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class ThrowingCalendarMisfireSqliteTest
{
    private const string SchedulerName = "throwing-calendar-misfire";
    private const string Group = "throwing-calendar";

    /// <summary>The name the delegate answers with the faulty calendar.</summary>
    private const string CalendarName = FireFault.FaultyCalendarName;

    /// <summary><c>JobStoreSupport.MaxConsecutiveFireFailures</c> as it ships.</summary>
    private const int DefaultMaxConsecutiveFireFailures = 5;

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset Epoch = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// <c>JobStoreSupport.MisfireThreshold</c> as it ships, which is also how long after one counted misfire
    /// failure the next one counts.
    /// </summary>
    private static readonly TimeSpan MisfireThreshold = TimeSpan.FromMinutes(1);

    private static readonly JobKey SerialJobKey = new JobKey("serial", Group);
    private static readonly JobKey OrdinaryJobKey = new JobKey("ordinary", Group);

    private static readonly TriggerKey FirstKey = new TriggerKey("first", Group);
    private static readonly TriggerKey CalendaredKey = new TriggerKey("calendared", Group);
    private static readonly TriggerKey MateKey = new TriggerKey("mate", Group);
    private static readonly TriggerKey LaterKey = new TriggerKey("later", Group);

    private Func<DateTimeOffset> originalUtcNow;
    private DateTimeOffset now;
    private string databaseFile;
    private string connectionString;
    private MisfirePassJobStore store;

    [SetUp]
    public async Task CreateStore()
    {
        originalUtcNow = SystemTime.UtcNow;
        now = Epoch;
        SystemTime.UtcNow = () => now;

        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-throwing-calendar-misfire-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        InstallSchemaFromFreshInstallScript();

        FireFault.Reset();
        CalendarSqliteDelegate.Reset();
        store = await BuildStore(configure: null);
    }

    [TearDown]
    public async Task DisposeStore()
    {
        await store.Shutdown();
        SystemTime.UtcNow = originalUtcNow;

        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A serial job's completion
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A serial job's completion unblocks its other triggers and handles their misfires. The calendar of
    /// one of them throws: the completion commits, carries out its instruction and lets go of the job's
    /// other triggers, and the one that threw is <c>WAITING</c> with its fire time as it was.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsAsACompletionLetsGoOfTheJobsTriggersFailsThatTriggerAlone()
    {
        TriggerFiredBundle firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        FireFault.CalendarFault.ThrowOnce();

        await CompleteWithin(firing, SchedulerInstruction.SetTriggerComplete);

        FireFault.CalendarFault.Thrown.Should().Be(1);
        (await TriggerState(FirstKey)).Should().Be("COMPLETE", "the completion carried out its instruction");
        (await FiredRowCount(FirstKey)).Should().Be(0, "and let go of its fired row");
        (await TriggerState(MateKey)).Should().Be("WAITING", "the job's other triggers are let go all the same");
        (await store.RetrieveTrigger(MateKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddHours(1), "and their misfires handled");
        (await TriggerState(CalendaredKey)).Should().Be("WAITING", "released, for the misfire handler");
        (await store.RetrieveTrigger(CalendaredKey)).GetNextFireTimeUtc().Should().Be(Epoch, "nothing of its misfire was written");

        await store.RecoverMisfires();

        (await store.RetrieveTrigger(CalendaredKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddHours(1),
            "the misfire handler handled the misfire once the calendar answered");
    }

    /// <summary>
    /// The failure at the completion counts toward the limit, in the ledger a failed fire counts in: the
    /// misfire handler's failures go on from it, and the one that reaches the limit stores the trigger
    /// <c>ERROR</c>.
    /// </summary>
    [Test]
    public async Task AFailureAsACompletionLetsGoOfTheJobsTriggersCountsTowardTheLimit()
    {
        TriggerFiredBundle firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        FireFault.CalendarFault.ThrowAlways();

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        for (int failure = 2; failure < DefaultMaxConsecutiveFireFailures; failure++)
        {
            // A misfire failure counts once per misfire threshold.
            now = now.Add(MisfireThreshold);
            await store.RecoverMisfires();
            (await TriggerState(CalendaredKey)).Should().Be("WAITING", $"{failure} failure(s) in a row is short of the limit");
        }

        now = now.Add(MisfireThreshold);
        await store.RecoverMisfires();

        (await TriggerState(CalendaredKey)).Should().Be("ERROR",
            "the completion's failure and the misfire handler's are one run of failures");
        FireFault.CalendarFault.Thrown.Should().Be(DefaultMaxConsecutiveFireFailures);

        await store.RecoverMisfires();
        FireFault.CalendarFault.Thrown.Should().Be(DefaultMaxConsecutiveFireFailures, "an ERROR trigger is not a misfire to handle");
    }

    /// <summary>
    /// The failure that reaches the limit at the completion stores the trigger <c>ERROR</c> in the
    /// completion's own transaction, which commits, and the trigger is kept.
    /// </summary>
    [Test]
    public async Task AFailureAsACompletionLetsGoOfTheJobsTriggersThatReachesTheLimitStoresTheTriggerError()
    {
        await RebuildStore(x => x.MaxConsecutiveFireFailures = 1);
        TriggerFiredBundle firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        FireFault.CalendarFault.ThrowOnce();

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        (await TriggerState(CalendaredKey)).Should().Be("ERROR", "kept, and ERROR");
        (await TriggerState(MateKey)).Should().Be("WAITING");
        (await FiredRowCount(FirstKey)).Should().Be(0, "the completion committed");
    }

    /// <summary>
    /// The database failing to read the calendar in the same step is not the trigger's failure. It rolls
    /// the completion back, which is retried, and nothing is counted: with a limit of one, the trigger is
    /// not stored <c>ERROR</c>, and its misfire is handled on the retry.
    /// </summary>
    [Test]
    public async Task ADatabaseFailureAsACompletionLetsGoOfTheJobsTriggersIsRetriedAndNotCounted()
    {
        await RebuildStore(x => x.MaxConsecutiveFireFailures = 1);
        TriggerFiredBundle firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        int readsBefore = FireFault.CalendarFault.Reads;
        FireFault.CalendarFault.FailNextRead();

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        (FireFault.CalendarFault.Reads - readsBefore).Should().Be(2, "the rolled-back attempt read the calendar, and so did the retry");
        (await TriggerState(CalendaredKey)).Should().Be("WAITING", "a database failure is not the trigger's, so nothing was counted");
        (await store.RetrieveTrigger(CalendaredKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddHours(1), "the retry handled its misfire");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The misfire handler
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The misfire handler already went on past a trigger whose calendar throws, but left it
    /// <c>WAITING</c> for good, logged on every scan. Now the scan whose failure reaches the limit stores
    /// it <c>ERROR</c>, and no scan reads it after that.
    /// </summary>
    [Test]
    public async Task TheMisfireHandlerStoresATriggerWhoseCalendarAlwaysThrowsErrorAtTheLimit()
    {
        await StoreJob(OrdinaryJobKey, serial: false);
        await Schedule(CalendaredKey, OrdinaryJobKey, onCalendar: true);
        now = now.AddMinutes(30);
        FireFault.CalendarFault.ThrowAlways();

        for (int failure = 1; failure < DefaultMaxConsecutiveFireFailures; failure++)
        {
            await store.RecoverMisfires();
            (await TriggerState(CalendaredKey)).Should().Be("WAITING", $"{failure} failure(s) in a row is short of the limit");
            (await store.RetrieveTrigger(CalendaredKey)).GetNextFireTimeUtc().Should().Be(Epoch, "nothing of the misfire is written");
            now = now.Add(MisfireThreshold);
        }

        await store.RecoverMisfires();

        (await TriggerState(CalendaredKey)).Should().Be("ERROR");

        await store.RecoverMisfires();
        FireFault.CalendarFault.Thrown.Should().Be(DefaultMaxConsecutiveFireFailures, "an ERROR trigger is not a misfire to handle");
    }

    /// <summary>
    /// A batch of one: the trigger whose calendar throws is first in the misfire order, and used to be the
    /// whole of every batch, so the trigger behind it was never handled. Once it is stored <c>ERROR</c>,
    /// the next scan reaches the one behind it.
    /// </summary>
    [Test]
    public async Task ATriggerWhoseCalendarAlwaysThrowsNoLongerHoldsTheHeadOfEveryMisfireBatch()
    {
        await RebuildStore(x => x.MaxMisfiresToHandleAtATime = 1);
        await StoreJob(OrdinaryJobKey, serial: false);
        await Schedule(CalendaredKey, OrdinaryJobKey, onCalendar: true);
        await Schedule(LaterKey, OrdinaryJobKey, startAt: Epoch.AddMinutes(1));
        now = now.AddMinutes(30);
        FireFault.CalendarFault.ThrowAlways();

        for (int scan = 0; scan < DefaultMaxConsecutiveFireFailures; scan++)
        {
            await store.RecoverMisfires();
            now = now.Add(MisfireThreshold);
        }

        (await TriggerState(CalendaredKey)).Should().Be("ERROR");
        (await store.RetrieveTrigger(LaterKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddMinutes(1), "behind it, and not reached yet");

        await store.RecoverMisfires();

        (await store.RetrieveTrigger(LaterKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddMinutes(61),
            "with the failing trigger out of the way, the batch of one is the trigger behind it");
    }

    /// <summary>
    /// A limit of zero never stores the trigger <c>ERROR</c>; the misfire handler goes on past it on every
    /// scan, as before.
    /// </summary>
    [Test]
    public async Task ALimitOfZeroLeavesATriggerWhoseCalendarThrowsToTheMisfireHandler()
    {
        await RebuildStore(x => x.MaxConsecutiveFireFailures = 0);
        await StoreJob(OrdinaryJobKey, serial: false);
        await Schedule(CalendaredKey, OrdinaryJobKey, onCalendar: true);
        now = now.AddMinutes(30);
        FireFault.CalendarFault.ThrowAlways();

        for (int scan = 0; scan < 2 * DefaultMaxConsecutiveFireFailures; scan++)
        {
            await store.RecoverMisfires();
        }

        (await TriggerState(CalendaredKey)).Should().Be("WAITING");
        FireFault.CalendarFault.Thrown.Should().Be(2 * DefaultMaxConsecutiveFireFailures);
    }

    /// <summary>
    /// A backlog of misfires has the misfire handler pass again at once, and a trigger whose calendar
    /// throws is first in every pass. Its failures count once per misfire threshold, so a calendar that is
    /// briefly unreachable does not park its trigger in a fraction of a second; one that stays unreachable
    /// still parks it, on the failure that reaches the limit.
    /// </summary>
    [Test]
    public async Task AMisfireBacklogDoesNotParkATriggerWhoseCalendarThrowsAtOnce()
    {
        await StoreJob(OrdinaryJobKey, serial: false);
        await Schedule(CalendaredKey, OrdinaryJobKey, onCalendar: true);
        for (int i = 1; i <= 25; i++)
        {
            await Schedule(new TriggerKey("backlog-" + i, Group), OrdinaryJobKey, startAt: Epoch.AddSeconds(i));
        }

        now = now.AddMinutes(30);
        FireFault.CalendarFault.ThrowAlways();

        int quickPasses = 4 * DefaultMaxConsecutiveFireFailures;
        for (int pass = 0; pass < quickPasses; pass++)
        {
            await store.RecoverMisfires();
        }

        FireFault.CalendarFault.Thrown.Should().Be(quickPasses, "the throwing trigger is first in every pass");
        (await TriggerState(CalendaredKey)).Should().Be("WAITING",
            "the passes came within one misfire threshold, so they count as one failure");
        (await store.RetrieveTrigger(new TriggerKey("backlog-25", Group))).GetNextFireTimeUtc().Should().Be(Epoch.AddHours(1).AddSeconds(25),
            "the backlog behind it was handled");

        for (int failure = 2; failure <= DefaultMaxConsecutiveFireFailures; failure++)
        {
            now = now.Add(MisfireThreshold);
            await store.RecoverMisfires();
        }

        (await TriggerState(CalendaredKey)).Should().Be("ERROR",
            "a misfire threshold apart, each failure counts, and the one that reaches the limit parks the trigger");
    }

    /// <summary>
    /// A misfire handled ends the trigger's run of failures: one failure before it and one after it are
    /// one each, not two in a row.
    /// </summary>
    [Test]
    public async Task AMisfireHandledBetweenTwoFailuresStartsTheCountAgain()
    {
        await RebuildStore(x => x.MaxConsecutiveFireFailures = 2);
        await StoreJob(OrdinaryJobKey, serial: false);
        await Schedule(CalendaredKey, OrdinaryJobKey, onCalendar: true);
        now = now.AddMinutes(30);

        FireFault.CalendarFault.ThrowOnce();
        await store.RecoverMisfires();

        // The calendar answers: the misfire is handled, and the trigger moves on to its next hourly fire.
        await store.RecoverMisfires();
        (await store.RetrieveTrigger(CalendaredKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddHours(1));

        // Late for that fire too, and the calendar throws again.
        now = now.AddHours(1);
        FireFault.CalendarFault.ThrowOnce();
        await store.RecoverMisfires();

        FireFault.CalendarFault.Thrown.Should().Be(2);
        (await TriggerState(CalendaredKey)).Should().Be("WAITING",
            "the handled misfire between the two failures started the count again, so this is one failure, short of the limit");
    }

    /// <summary>
    /// On a connection the application enlisted, what the store records after its part is done comes before
    /// the application commits or rolls back. A misfire failure there is logged and not counted, so one the
    /// application rolled back does not bring the trigger nearer to <c>ERROR</c>.
    /// </summary>
    [Test]
    public async Task AMisfireFailureOnAnEnlistedConnectionIsNotCounted()
    {
        await RebuildStore(x =>
        {
            x.MaxConsecutiveFireFailures = 2;
            x.AcceptEnlistedTransactions = true;
        });
        await store.SchedulerResumed();
        await StoreJob(OrdinaryJobKey, serial: false);
        await Schedule(CalendaredKey, OrdinaryJobKey, onCalendar: true);
        await store.PauseTrigger(CalendaredKey);
        now = now.AddMinutes(30);
        FireFault.CalendarFault.ThrowAlways();

        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            using DbTransaction transaction = connection.BeginTransaction();
            using (AmbientConnection.Enlist(SchedulerName, connection, transaction))
            {
                await store.ResumeTrigger(CalendaredKey);
            }

            transaction.Rollback();
        }

        FireFault.CalendarFault.Thrown.Should().Be(1);
        (await TriggerState(CalendaredKey)).Should().Be("PAUSED", "the application rolled the resume back");

        now = now.Add(MisfireThreshold);
        await store.ResumeTrigger(CalendaredKey);

        FireFault.CalendarFault.Thrown.Should().Be(2);
        (await TriggerState(CalendaredKey)).Should().Be("WAITING",
            "the failure the application rolled back was not counted, so this is the first, short of the limit; the trigger is resumed as it was");
    }

    /// <summary>
    /// A failure is counted only once the transaction it happened in has committed. The calendar throws,
    /// then a later statement of the same completion fails transiently, and the completion is rolled back
    /// and run again, meeting the calendar again: one failure, not two.
    /// </summary>
    [Test]
    public async Task AMisfireFailureInATransactionThatRolledBackIsNotCounted()
    {
        await RebuildStore(x => x.MaxConsecutiveFireFailures = 2);
        TriggerFiredBundle firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        FireFault.CalendarFault.ThrowAlways();

        // The retry comes two misfire thresholds later, so it is not the interval that keeps it from counting.
        CalendarSqliteDelegate.FailDeleteFiredTriggerOnce(() => now = now.Add(MisfireThreshold).Add(MisfireThreshold));

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        FireFault.CalendarFault.Thrown.Should().Be(2, "the rolled-back attempt met the calendar, and so did the retry");
        CalendarSqliteDelegate.DeleteFiredTriggerFailures.Should().Be(1);
        (await FiredRowCount(FirstKey)).Should().Be(0, "the retry committed");
        (await TriggerState(CalendaredKey)).Should().Be("WAITING",
            "the rolled-back attempt's failure was never counted, so the retry's is the first, short of the limit");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Resuming
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Resuming triggers handles the misfires they accrued while paused. A calendar that throws there
    /// fails that trigger alone: the rest of the group is resumed, the trigger is resumed as it was, and
    /// the caller is not handed the calendar's exception.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsAsATriggerIsResumedFailsThatTriggerAlone()
    {
        await store.SchedulerResumed();
        await StoreJob(OrdinaryJobKey, serial: false);
        await Schedule(CalendaredKey, OrdinaryJobKey, onCalendar: true);
        await Schedule(LaterKey, OrdinaryJobKey);
        await store.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(Group));
        now = now.AddMinutes(30);
        FireFault.CalendarFault.ThrowOnce();

        await store.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(Group));

        FireFault.CalendarFault.Thrown.Should().Be(1);
        (await TriggerState(LaterKey)).Should().Be("WAITING", "the rest of the group is resumed");
        (await store.RetrieveTrigger(LaterKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddHours(1), "with its misfire handled");
        (await TriggerState(CalendaredKey)).Should().Be("WAITING", "resumed as it was");
        (await store.RetrieveTrigger(CalendaredKey)).GetNextFireTimeUtc().Should().Be(Epoch);

        await store.RecoverMisfires();

        (await store.RetrieveTrigger(CalendaredKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddHours(1),
            "resumed into the schedule, its misfire is the misfire handler's");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// <c>first</c>, <c>calendared</c> and <c>mate</c>, in that order, on the serial job, all due at
    /// once: <c>first</c> fires, which blocks the other two, and the job runs half an hour, past their
    /// fire time and the misfire threshold. Answers <c>first</c>'s firing.
    /// </summary>
    private async Task<TriggerFiredBundle> GivenASerialJobRunningPastItsTriggersFireTime()
    {
        await StoreJob(SerialJobKey, serial: true);
        await Schedule(FirstKey, SerialJobKey, priority: 10);
        await Schedule(CalendaredKey, SerialJobKey, priority: 5, onCalendar: true);
        await Schedule(MateKey, SerialJobKey, priority: 1);

        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(now.AddSeconds(1), 10, TimeSpan.Zero);
        acquired.Select(x => x.Key).Should().Equal(new[] { FirstKey }, "a batch takes one trigger of a serial job");

        TriggerFiredResult fired = (await store.TriggersFired(acquired)).Single();
        fired.TriggerFiredBundle.Should().NotBeNull();
        (await TriggerState(CalendaredKey)).Should().Be("BLOCKED");
        (await TriggerState(MateKey)).Should().Be("BLOCKED");

        now = now.AddMinutes(30);
        return fired.TriggerFiredBundle;
    }

    /// <summary>
    /// Completes the firing, failing the test rather than hanging it if the completion does not return:
    /// one that rolls back is retried until it commits.
    /// </summary>
    private async Task CompleteWithin(TriggerFiredBundle firing, SchedulerInstruction instruction)
    {
        Task completing = store.TriggeredJobComplete(firing.Trigger, firing.JobDetail, instruction);
        Task finished = await Task.WhenAny(completing, Task.Delay(WaitLimit));
        finished.Should().BeSameAs(completing, "the completion commits; one that rolled back for the calendar's throw was retried for good");
        await completing;
    }

    private async Task StoreJob(JobKey key, bool serial)
    {
        IJobDetail job = serial
            ? JobBuilder.Create<SerialJob>().WithIdentity(key).StoreDurably().Build()
            : JobBuilder.Create<OrdinaryJob>().WithIdentity(key).StoreDurably().Build();
        await store.StoreJob(job, replaceExisting: true);
    }

    /// <summary>
    /// Hourly from <paramref name="startAt" />, or the epoch. A trigger that misfires has its misfire
    /// handled by the simple trigger's smart policy, which consults the calendar for the next fire time.
    /// </summary>
    private async Task Schedule(
        TriggerKey key,
        JobKey jobKey,
        int priority = TriggerConstants.DefaultPriority,
        bool onCalendar = false,
        DateTimeOffset? startAt = null)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(jobKey)
            .StartAt(startAt ?? Epoch)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithPriority(priority)
            .ModifiedByCalendar(onCalendar ? CalendarName : null)
            .Build();

        // Without the calendar, so the calendar is consulted only by the misfires the tests count.
        trigger.ComputeFirstFireTimeUtc(null);
        await store.StoreTrigger(trigger, replaceExisting: false);
    }

    private Task<string> TriggerState(TriggerKey key)
    {
        return ReadScalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", key.Name);
    }

    private async Task<long> FiredRowCount(TriggerKey key)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", key.Name);
        return (long) await command.ExecuteScalarAsync();
    }

    private async Task<string> ReadScalar(string sql, string name)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task RebuildStore(Action<MisfirePassJobStore> configure)
    {
        await store.Shutdown();
        store = await BuildStore(configure);
    }

    private async Task<MisfirePassJobStore> BuildStore(Action<MisfirePassJobStore> configure)
    {
        string dataSource = $"throwing-calendar-misfire-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        MisfirePassJobStore jobStore = new MisfirePassJobStore
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = "node",
            DriverDelegateType = typeof(CalendarSqliteDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
            // A failure the completion retries is retried at once rather than seconds later.
            TransientRetryInterval = TimeSpan.Zero,
            DbRetryInterval = TimeSpan.FromMilliseconds(100),
        };
        configure?.Invoke(jobStore);

        // Initialized and never started: nothing here is left to a scheduler thread, a misfire pass or a
        // startup recovery, each of which would move the rows these tests read.
        await jobStore.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        return jobStore;
    }

    private void InstallSchemaFromFreshInstallScript()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
        command.ExecuteNonQuery();
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// The shipped SQLite delegate, answering a read of the faulty calendar with
    /// <see cref="FireFault.CalendarFault" />'s calendar rather than one deserialized from a row.
    /// </summary>
    public sealed class CalendarSqliteDelegate : SQLiteDelegate
    {
        private static Action onDeleteFiredTriggerFailure;
        private static int deleteFiredTriggerFailures;

        /// <summary>How many times deleting a fired-trigger row failed.</summary>
        public static int DeleteFiredTriggerFailures => Volatile.Read(ref deleteFiredTriggerFailures);

        public static void Reset()
        {
            Volatile.Write(ref onDeleteFiredTriggerFailure, null);
            Volatile.Write(ref deleteFiredTriggerFailures, 0);
        }

        /// <summary>
        /// Fails the next deletion of a fired-trigger row as a busy database fails it, which the store
        /// retries, after calling <paramref name="onFailure" />.
        /// </summary>
        public static void FailDeleteFiredTriggerOnce(Action onFailure) => Volatile.Write(ref onDeleteFiredTriggerFailure, onFailure);

        public override Task<int> DeleteFiredTrigger(ConnectionAndTransactionHolder conn, string entryId, CancellationToken cancellationToken = default)
        {
            Action onFailure = Interlocked.Exchange(ref onDeleteFiredTriggerFailure, null);
            if (onFailure != null)
            {
                Interlocked.Increment(ref deleteFiredTriggerFailures);
                onFailure();
                throw new SqliteException("database is locked", 5 /* SQLITE_BUSY, which the store retries */);
            }

            return base.DeleteFiredTrigger(conn, entryId, cancellationToken);
        }

        public override Task<ICalendar> SelectCalendar(
            ConnectionAndTransactionHolder conn,
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    public sealed class OrdinaryJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
