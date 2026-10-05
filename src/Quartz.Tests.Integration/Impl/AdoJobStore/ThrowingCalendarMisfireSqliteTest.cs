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
            await store.RecoverMisfires();
            (await TriggerState(CalendaredKey)).Should().Be("WAITING", $"{failure} failure(s) in a row is short of the limit");
        }

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
