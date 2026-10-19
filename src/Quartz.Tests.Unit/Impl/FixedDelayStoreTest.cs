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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Calendar;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// A simple trigger that measures its interval from the previous run's completion (#4140), asserted
/// against the in-memory store and against a SQLite-backed ADO store.
/// </summary>
/// <remarks>
/// Driven against the store on a clock the test owns, as <c>OverlapPolicyStoreTest</c> is: a firing
/// "runs" from the moment <see cref="IJobStore.TriggersFired" /> committed it until the test completes it,
/// with the instruction the trigger itself gives the scheduler for how it ended.
/// </remarks>
[TestFixture(OverlapStoreKind.InMemory)]
[TestFixture(OverlapStoreKind.Sqlite)]
public sealed class FixedDelayStoreTest
{
    private const string Group = "fixed-delay";
    private const string SchedulerName = "FixedDelayStoreTest";
    private const string DataSourceName = "fixed-delay";

    /// <summary>The name the stores answer with a calendar the test can make throw.</summary>
    private const string FaultyCalendarName = "faulty";

    /// <summary>Log events <c>RAMJobStoreLog.FixedDelayFireTimeFailed</c> and <c>AdoJobStoreLog.FixedDelayFireTimeFailed</c>.</summary>
    private static readonly int[] fixedDelayFireTimeFailed = [2011, 3061];

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan interval = TimeSpan.FromHours(1);

    private readonly OverlapStoreKind kind;

    private SqliteTestDatabase? database;
    private IDbProvider? dbProvider;

    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private ThrowingCalendar faultyCalendar = null!;
    private IJobStore store = null!;

    public FixedDelayStoreTest(OverlapStoreKind kind)
    {
        this.kind = kind;
    }

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        if (kind != OverlapStoreKind.Sqlite)
        {
            return;
        }

        database = new SqliteTestDatabase("fixed-delay");

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
    }

    [OneTimeTearDown]
    public void DropDatabase()
    {
        database?.Dispose();
    }

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(epoch);
        signals = new RecordingSignaler();
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        faultyCalendar = new ThrowingCalendar();
        store = kind == OverlapStoreKind.InMemory ? await InMemoryStore() : await SqliteStore();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        loggerFactory.Dispose();
        logs.Dispose();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The hold and the delay
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AFiringHoldsItsTriggerAndItsCompletionMeasuresTheNextRunFromNow()
    {
        IOperableTrigger trigger = await Schedule("trigger");
        IOperableTrigger firing = await FireOne(trigger.Key);

        await StoredStateShouldBe(trigger.Key, AdoConstants.StateBlocked, "the firing holds its trigger as a BufferOne firing does");

        clock.Advance(TimeSpan.FromMinutes(90));
        (await AcquireKeys()).Should().BeEmpty(
            "no node acquires the trigger while its firing runs, past the time a fixed rate would have fired it");

        await CompleteAsTheSchedulerWould(firing);

        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddMinutes(90) + interval,
            "the next run is one interval after the completion, not after the run's scheduled time");
        await StoredStateShouldBe(trigger.Key, AdoConstants.StateWaiting, "the completion lets go of the trigger");

        clock.Advance(interval - TimeSpan.FromMinutes(1));
        (await AcquireKeys()).Should().BeEmpty("a minute before the delay is up nothing is due");

        clock.Advance(TimeSpan.FromMinutes(1));
        await FireOne(trigger.Key);
        signals.Misfired.Should().BeEmpty(
            "a run longer than the interval is not a misfire when the interval is measured from its end");
    }

    [Test]
    public async Task AFixedRateTriggerIsNeitherHeldNorMovedByItsCompletion()
    {
        IOperableTrigger trigger = await Schedule("trigger", fixedDelay: false);
        IOperableTrigger firing = await FireOne(trigger.Key);

        clock.Advance(TimeSpan.FromMinutes(70));
        await CompleteAsTheSchedulerWould(firing);

        (await NextFireTime(trigger.Key)).Should().Be(epoch + interval,
            "a fixed-rate trigger keeps the fire time its start time lays down, whenever its firing ends");
    }

    [Test]
    public async Task TheRepeatCountCountsRunsWhenTheRunsDriftOffTheStartTimesGrid()
    {
        // Forty-minute runs an hour apart: the third run starts 3h20m after the start time, which a count
        // read off the start time's grid would take for a fourth.
        IOperableTrigger trigger = await Schedule("trigger", repeatCount: 3);
        int runs = 0;

        while (await store.GetTrigger(trigger.Key) is not null && runs < 10)
        {
            IOperableTrigger firing = await FireOne(trigger.Key);
            runs++;

            clock.Advance(TimeSpan.FromMinutes(40));
            await CompleteAsTheSchedulerWould(firing);

            if (await store.GetTrigger(trigger.Key) is { } stored)
            {
                stored.NextFireTimeUtc.Should().Be(clock.GetUtcNow() + interval, "each run is measured from the one before it");
                clock.Advance(interval);
            }
        }

        runs.Should().Be(4, "a repeat count of three is four runs, however far they drift from the start time's grid");
    }

    [Test]
    public async Task TheEndTimeEndsTheTriggerAtTheCompletionThatPassesIt()
    {
        IOperableTrigger trigger = await Schedule("trigger", endTime: epoch.AddHours(2));
        IOperableTrigger firing = await FireOne(trigger.Key);

        clock.Advance(TimeSpan.FromMinutes(90));
        await CompleteAsTheSchedulerWould(firing);

        (await store.GetTrigger(trigger.Key)).Should().BeNull(
            "one interval after the completion is past the end time, so the trigger has no run left");
        signals.Finalized.Should().Contain(trigger.Key,
            "the run shell announced nothing, because the fire time the firing left said there was a run left");
    }

    [Test]
    public async Task AVetoedFiringCountsAsCompletedNow()
    {
        IOperableTrigger trigger = await Schedule("trigger");
        IOperableTrigger firing = await FireOne(trigger.Key);

        clock.Advance(TimeSpan.FromMinutes(1));
        await Complete(firing, firing.ExecutionComplete(A.Fake<IJobExecutionContext>(), result: null), ExecutionOutcome.Vetoed);

        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddMinutes(1) + interval,
            "a firing a listener vetoed ended when the store was told, and the delay runs from then");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Misfires and retries
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AMisfiredTriggerFiresOnceNowAndMeasuresFromThatRun()
    {
        IOperableTrigger trigger = await Schedule("trigger");
        IOperableTrigger firing = await FireOne(trigger.Key);
        clock.Advance(TimeSpan.FromMinutes(10));
        await CompleteAsTheSchedulerWould(firing);

        // The node was down for hours past the run measured from that completion.
        clock.Advance(TimeSpan.FromHours(5));
        (await RecoverMisfires()).Should().Be(1);

        signals.Misfired.Should().ContainSingle().Which.Should().Be(trigger.Key);
        (await NextFireTime(trigger.Key)).Should().Be(clock.GetUtcNow(),
            "the smart policy fires a trigger that measures from completion once, now: there is no backlog to catch up on");

        IOperableTrigger late = await FireOne(trigger.Key);
        clock.Advance(TimeSpan.FromMinutes(5));
        await CompleteAsTheSchedulerWould(late);

        (await NextFireTime(trigger.Key)).Should().Be(clock.GetUtcNow() + interval, "and measures from that run's completion");
    }

    [Test]
    public async Task ARetryFiresAtItsRetryTimeAndTheDelayFollowsTheLastAttempt()
    {
        // A retry half an hour after a failure half an hour in lands on the fire time the firing left,
        // which a fixed-rate trigger's next occurrence would take over.
        IOperableTrigger trigger = await Schedule("trigger", retry: RetryPolicy.Fixed(3, TimeSpan.FromMinutes(30)));
        IOperableTrigger firing = await FireOne(trigger.Key);

        clock.Advance(TimeSpan.FromMinutes(30));
        SchedulerInstruction failed = await CompleteAsTheSchedulerWould(firing, new JobExecutionException("failed"));

        failed.Should().Be(SchedulerInstruction.RetryTrigger,
            "a trigger that measures from completion has no next occurrence for the retry to give way to");
        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddHours(1), "the retry fires at its retry time");
        await StoredStateShouldBe(trigger.Key, AdoConstants.StateWaiting, "the retry waits as any retry does");

        clock.Advance(TimeSpan.FromMinutes(30));
        IOperableTrigger retry = await FireOne(trigger.Key);
        retry.RetryAttempt.Should().Be(1);
        await StoredStateShouldBe(trigger.Key, AdoConstants.StateBlocked, "an attempt is held while it runs, as the first was");

        clock.Advance(TimeSpan.FromMinutes(10));
        await CompleteAsTheSchedulerWould(retry);

        IOperableTrigger stored = (await store.GetTrigger(trigger.Key))!;
        stored.NextFireTimeUtc.Should().Be(epoch.AddMinutes(70) + interval, "the delay is measured from the last attempt's completion");
        stored.RetryAttempt.Should().Be(0, "the occurrence is over");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // What else may happen to the trigger while its firing runs
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ATriggerPausedWhileItsFiringRunsStaysPausedWithItsNextRunMeasuredFromCompletion()
    {
        IOperableTrigger trigger = await Schedule("trigger");
        IOperableTrigger firing = await FireOne(trigger.Key);

        await store.PauseTrigger(trigger.Key);
        await StoredStateShouldBe(trigger.Key, AdoConstants.StatePausedBlocked, "paused, and still held");

        clock.Advance(TimeSpan.FromMinutes(20));
        await CompleteAsTheSchedulerWould(firing);

        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused, "the completion lets go of the hold, not of the pause");
        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddMinutes(20) + interval);

        (await store.ResumeTrigger(trigger.Key)).Should().BeTrue();
        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddMinutes(20) + interval, "resumed before it came due, nothing misfired");
    }

    [Test]
    public async Task ATriggerResumedWhileItsFiringRunsIsStillHeld()
    {
        IOperableTrigger trigger = await Schedule("trigger");
        IOperableTrigger firing = await FireOne(trigger.Key);

        await store.PauseTrigger(trigger.Key);
        (await store.ResumeTrigger(trigger.Key)).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(150));
        (await AcquireKeys()).Should().BeEmpty("a resume must not let a second firing start beside the first");

        await CompleteAsTheSchedulerWould(firing);
        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddMinutes(150) + interval);
    }

    [Test]
    public async Task AReplacementStoredWhileTheFiringRunsKeepsItsOwnFireTime()
    {
        IOperableTrigger trigger = await Schedule("trigger");
        IOperableTrigger firing = await FireOne(trigger.Key);

        IOperableTrigger replacement = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(trigger.Key)
            .ForJob(trigger.JobKey)
            .StartAt(epoch.AddHours(3))
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();
        replacement.ComputeFirstFireTimeUtc(null);
        (await store.ReplaceTrigger(trigger.Key, replacement)).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(20));
        await CompleteAsTheSchedulerWould(firing);

        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddHours(3),
            "the trigger was replaced while the firing ran, and the replacement is nobody's to move");
    }

    [Test]
    public async Task TurningItOffWhileTheFiringRunsLetsGoWithTheFixedRateFireTime()
    {
        IOperableTrigger trigger = await Schedule("trigger");
        IOperableTrigger firing = await FireOne(trigger.Key);

        (await store.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithMeasureIntervalFromCompletion(false))).Should().BeTrue();
        ((ISimpleTrigger) (await store.GetTrigger(trigger.Key))!).MeasureIntervalFromCompletion.Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(20));
        await CompleteAsTheSchedulerWould(firing);

        (await NextFireTime(trigger.Key)).Should().Be(epoch + interval,
            "the stored trigger runs at a fixed rate now, so the completion only lets go of the hold");
        await StoredStateShouldBe(trigger.Key, AdoConstants.StateWaiting, "and lets go of it");
    }

    [Test]
    public async Task ASerialJobsTriggerIsNotMisfiredByARunLongerThanItsInterval()
    {
        IOperableTrigger trigger = await Schedule("trigger", nonConcurrent: true);
        IOperableTrigger firing = await FireOne(trigger.Key);

        clock.Advance(TimeSpan.FromHours(3));
        await CompleteAsTheSchedulerWould(firing);

        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddHours(3) + interval,
            "the next run is measured before the job's triggers are let go, which applies the misfire policy to them");
        signals.Misfired.Should().BeEmpty("nothing was due while the job ran");
    }

    [TestCase(OverlapPolicy.AllowAll)]
    [TestCase(OverlapPolicy.Skip)]
    [TestCase(OverlapPolicy.CancelPrevious)]
    public async Task TheOverlapPolicyIsNeverAsked(OverlapPolicy policy)
    {
        IOperableTrigger trigger = await Schedule("trigger", policy: policy);
        IOperableTrigger firing = await FireOne(trigger.Key);

        clock.Advance(TimeSpan.FromMinutes(150));
        (await AcquireKeys()).Should().BeEmpty($"no firing comes due while one runs, so {policy} has nothing to decide");
        signals.Skipped.Should().BeEmpty();

        await CompleteAsTheSchedulerWould(firing);
        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddMinutes(150) + interval);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Calendars
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ACalendarMovesTheNextRunOnByWholeIntervalsPastWhatItExcludes()
    {
        await store.AddCalendar("lunch", new DailyCalendar(new TimeOnly(11, 0), new TimeOnly(12, 15)) { TimeZone = TimeZoneInfo.Utc });
        IOperableTrigger trigger = await Schedule("trigger", calendarName: "lunch");
        IOperableTrigger firing = await FireOne(trigger.Key);

        clock.Advance(TimeSpan.FromMinutes(30));
        await CompleteAsTheSchedulerWould(firing);

        (await NextFireTime(trigger.Key)).Should().Be(epoch.AddMinutes(150),
            "11:30 is excluded, so the run moves on an interval to 12:30, which is not");
    }

    [Test]
    public async Task ACalendarThatThrowsAtTheCompletionLeavesTheTriggerToTheOrdinaryRelease()
    {
        if (kind == OverlapStoreKind.InMemory)
        {
            await store.AddCalendar(FaultyCalendarName, faultyCalendar);
        }

        IOperableTrigger trigger = await Schedule("trigger", calendarName: FaultyCalendarName);
        IOperableTrigger firing = await FireOne(trigger.Key);

        faultyCalendar.Throwing = true;
        clock.Advance(TimeSpan.FromMinutes(10));
        await CompleteAsTheSchedulerWould(firing);

        (await NextFireTime(trigger.Key)).Should().Be(epoch + interval,
            "the trigger keeps the fire time its firing left rather than failing a completion that would be retried for good");
        await StoredStateShouldBe(trigger.Key, AdoConstants.StateWaiting, "and is let go of all the same");
        logs.Entries.Should().ContainSingle(x => fixedDelayFireTimeFailed.Contains(x.EventId.Id))
            .Which.Exception.Should().BeSameAs(faultyCalendar.LastThrown);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Persistence
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task TheSettingIsStoredWithTheTriggerAndOnlyWhenSet()
    {
        IOperableTrigger fixedDelay = await Schedule("fixed-delay");
        IOperableTrigger fixedRate = await Schedule("fixed-rate", fixedDelay: false);

        ((ISimpleTrigger) (await store.GetTrigger(fixedDelay.Key))!).MeasureIntervalFromCompletion.Should().BeTrue();
        ((ISimpleTrigger) (await store.GetTrigger(fixedRate.Key))!).MeasureIntervalFromCompletion.Should().BeFalse();

        if (kind == OverlapStoreKind.Sqlite)
        {
            (await ReadColumn(AdoConstants.ColumnFixedDelay, fixedDelay.Key)).Should().NotBeNull();
            (await ReadColumn(AdoConstants.ColumnFixedDelay, fixedRate.Key)).Should().BeNull(
                "a fixed-rate trigger writes NULL, which is what every row a node older than 4.7 holds");
        }
    }

    [Test]
    public async Task TheSettingCanBeTurnedOnAndRefusedForATriggerThatIsNotASimpleOne()
    {
        IOperableTrigger trigger = await Schedule("trigger", fixedDelay: false);
        (await store.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithMeasureIntervalFromCompletion(true))).Should().BeTrue();
        ((ISimpleTrigger) (await store.GetTrigger(trigger.Key))!).MeasureIntervalFromCompletion.Should().BeTrue();

        IJobDetail job = JobBuilder.Create<OrdinaryJob>().WithIdentity("cron", Group).StoreDurably().Build();
        IOperableTrigger cron = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("cron", Group)
            .ForJob(job)
            .StartAt(epoch)
            .WithCronSchedule("0 0 * * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();
        cron.ComputeFirstFireTimeUtc(null);
        await store.ScheduleJob(job, cron);

        Func<Task> act = async () => await store.UpdateTriggerDetails(cron.Key, new TriggerDetailsUpdate().WithMeasureIntervalFromCompletion(true));
        await act.Should().ThrowAsync<JobPersistenceException>().WithMessage("*not a simple trigger*");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    private async Task StoredStateShouldBe(TriggerKey key, string state, string because)
    {
        if (kind == OverlapStoreKind.Sqlite)
        {
            (await ReadColumn("TRIGGER_STATE", key)).Should().Be(state, because);
        }
    }

    private async Task<DateTimeOffset?> NextFireTime(TriggerKey key) => (await store.GetTrigger(key))!.NextFireTimeUtc;

    private async ValueTask<IJobStore> InMemoryStore()
    {
        RAMJobStore ram = new(loggerFactory, signals, clock);
        await ram.Initialize(TestJobStores.Identity(instanceName: SchedulerName));
        return ram;
    }

    private async ValueTask<IJobStore> SqliteStore()
    {
        LocalTransactionJobStore ado = new(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node"),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: new FaultyCalendarSqliteDelegate(faultyCalendar),
            loggerFactory: loggerFactory));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await ado.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node"));
        await ado.Clear();
        return ado;
    }

    private async Task<IOperableTrigger> Schedule(
        string name,
        bool fixedDelay = true,
        int repeatCount = -1,
        DateTimeOffset? endTime = null,
        bool nonConcurrent = false,
        OverlapPolicy policy = OverlapPolicy.Default,
        RetryPolicy? retry = null,
        string? calendarName = null)
    {
        IJobDetail job = nonConcurrent
            ? JobBuilder.Create<SerialJob>().WithIdentity(name, Group).StoreDurably().Build()
            : JobBuilder.Create<OrdinaryJob>().WithIdentity(name, Group).StoreDurably().Build();

        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .EndAt(endTime)
            .WithOverlapPolicy(policy)
            .WithCalendarName(calendarName)
            .WithSimpleSchedule(x =>
            {
                x.WithInterval(interval).MeasureIntervalFromCompletion(fixedDelay);
                if (repeatCount < 0)
                {
                    x.RepeatForever();
                }
                else
                {
                    x.WithRepeatCount(repeatCount);
                }
            });

        if (retry is not null)
        {
            builder = builder.WithRetryPolicy(retry);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await store.ScheduleJob(job, trigger);
        return trigger;
    }

    /// <summary>
    /// Acquires and fires the one trigger due, which must be <paramref name="key" />.
    /// </summary>
    private async Task<IOperableTrigger> FireOne(TriggerKey key)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        acquired.Select(x => x.Key).Should().Equal([key], "the trigger under test is the one due");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull("a firing has to start before it can run");
        return results[0].TriggerFiredBundle!.Trigger;
    }

    /// <summary>
    /// What an acquisition would take right now, handed straight back so it changes nothing.
    /// </summary>
    private async Task<List<TriggerKey>> AcquireKeys()
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        foreach (IOperableTrigger trigger in acquired)
        {
            await store.ReleaseAcquiredTrigger(trigger);
        }

        return acquired.Select(x => x.Key).ToList();
    }

    /// <summary>
    /// A misfire pass over whatever is overdue: the database store's handler, or the in-memory store's
    /// acquisition, which is where that store applies misfire instructions.
    /// </summary>
    private async Task<int> RecoverMisfires()
    {
        if (store is LocalTransactionJobStore ado)
        {
            return (await ado.RecoverMisfires(Guid.NewGuid())).ProcessedMisfiredTriggerCount;
        }

        int before = signals.Misfired.Count;
        await AcquireKeys();
        return signals.Misfired.Count - before;
    }

    /// <summary>
    /// Completes a firing with the instruction the trigger gives the scheduler for how it ended.
    /// </summary>
    private async Task<SchedulerInstruction> CompleteAsTheSchedulerWould(IOperableTrigger firing, JobExecutionException? failure = null)
    {
        SchedulerInstruction instruction = firing.ExecutionComplete(A.Fake<IJobExecutionContext>(), failure);
        await Complete(firing, instruction, failure is null ? ExecutionOutcome.Succeeded : ExecutionOutcome.Failed);
        return instruction;
    }

    private async Task Complete(IOperableTrigger firing, SchedulerInstruction instruction, ExecutionOutcome outcome)
    {
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = (await store.GetJob(firing.JobKey))!,
            Instruction = instruction,
            Outcome = outcome
        });
    }

    private async Task<object?> ReadColumn(string column, TriggerKey key)
    {
        await using SqliteConnection connection = new(database!.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The shipped SQLite delegate, handing out the test's calendar for <see cref="FaultyCalendarName" />
    /// as though it had read it.
    /// </summary>
    private sealed class FaultyCalendarSqliteDelegate(ThrowingCalendar calendar) : SQLiteDelegate
    {
        public override async ValueTask<ICalendar?> SelectCalendar(
            ConnectionAndTransactionHolder conn,
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            return string.Equals(calendarName, FaultyCalendarName, StringComparison.Ordinal)
                ? calendar
                : await base.SelectCalendar(conn, calendarName, cancellationToken);
        }
    }

    /// <summary>
    /// A calendar that includes every instant until it is told to throw.
    /// </summary>
    private sealed class ThrowingCalendar : ICalendar
    {
        public bool Throwing { get; set; }

        public Exception? LastThrown { get; private set; }

        public string? Description { get; set; }

        public ICalendar? CalendarBase { get; set; }

        public bool IsTimeIncluded(DateTimeOffset timeUtc)
        {
            if (Throwing)
            {
                LastThrown = new InvalidOperationException("The calendar could not be read.");
                throw LastThrown;
            }

            return true;
        }

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc) => timeUtc;

        public ICalendar Clone() => this;
    }

    /// <summary>
    /// Records what the store told the scheduler about.
    /// </summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> Skipped { get; } = [];

        public List<TriggerKey> Misfired { get; } = [];

        public List<TriggerKey> Finalized { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Misfired.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifyTriggerListenersSkipped(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Skipped.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Finalized.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class OrdinaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A job that forbids concurrent execution, which holds every trigger of it back while it runs.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
