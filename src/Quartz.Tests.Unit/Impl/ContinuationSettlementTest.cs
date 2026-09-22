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
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Which store <see cref="ContinuationSettlementTest" /> is run against.
/// </summary>
public enum ContinuationStoreKind
{
    InMemory,
    Sqlite
}

/// <summary>
/// What happens to a continuation at the moment it is settled, and to the triggers around it, asserted
/// against the in-memory store and against a SQLite-backed ADO store.
/// </summary>
/// <remarks>
/// <para>
/// <c>JobStoreContractTest</c> makes the same claims of every store on every dialect leg, but it lives in
/// the integration project, whose dialect legs need Docker. SQLite is a file, so a whole persistent store
/// is a temporary path, and the unit run sees both stores through the same assertions.
/// </para>
/// <para>
/// Driven against the store rather than through a scheduler, on a clock the test owns, with the store
/// initialized but never started — so no background loop can move a trigger between an arrangement and
/// its assertion (#3303), and "now" is an instant rather than a race.
/// </para>
/// </remarks>
[TestFixture(ContinuationStoreKind.InMemory)]
[TestFixture(ContinuationStoreKind.Sqlite)]
public sealed class ContinuationSettlementTest
{
    private const string Group = "settlement";
    private const string SchedulerName = "ContinuationSettlementTest";
    private const string DataSourceName = "continuation-settlement";

    /// <summary>
    /// Mid-morning UTC, on a date no test machine's own clock is near, so nothing here can agree with
    /// real time by accident.
    /// </summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly ContinuationStoreKind kind;

    private SqliteTestDatabase? database;
    private IDbProvider? dbProvider;

    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private IJobStore store = null!;

    public ContinuationSettlementTest(ContinuationStoreKind kind)
    {
        this.kind = kind;
    }

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        if (kind != ContinuationStoreKind.Sqlite)
        {
            return;
        }

        database = new SqliteTestDatabase("continuation-settlement");

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
        store = kind == ContinuationStoreKind.InMemory ? await InMemoryStore() : await SqliteStore();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A released continuation joins the schedule the way any trigger stored at that moment would
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AContinuationReleasedWhileItsJobRunsIsBlockedUntilTheJobFinishes()
    {
        IJobDetail busy = Job("busy", nonConcurrent: true);
        IOperableTrigger running = Hourly("running", busy.Key);
        await store.ScheduleJob(busy, running);

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation("continuation", parent.Key, ContinuationCondition.OnSuccess, busy.Key);

        List<IOperableTrigger> fired = await Fire(running.Key, parent.Key);

        await Complete(Firing(fired, parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Blocked,
            "its job disallows concurrent execution and is running under another trigger, so the release puts it "
            + "where any trigger of that job stored now would go — behind the execution, not beside it");

        (await AcquireKeys()).Should().NotContain(continuation.Key,
            "a blocked trigger is not schedulable, and firing it now would run the job twice at once");

        await Complete(Firing(fired, running.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal,
            "the execution it was blocked behind has finished, which unblocks it like every other trigger of the job");

        (await AcquireKeys()).Should().Contain(continuation.Key,
            "the release gave it the fire time it would have had, and nothing holds it back any more");
    }

    [Test]
    public async Task AContinuationReleasedIntoAPausedGroupWhileItsJobRunsIsHeldByBoth()
    {
        IJobDetail busy = Job("busy", nonConcurrent: true);
        IOperableTrigger running = Hourly("running", busy.Key);
        await store.ScheduleJob(busy, running);

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "continuation", parent.Key, ContinuationCondition.OnSuccess, busy.Key, group: "paused-group");

        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("paused-group"));

        List<IOperableTrigger> fired = await Fire(running.Key, parent.Key);
        await Complete(Firing(fired, parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Paused,
            "the release is not somebody resuming the group");

        await store.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("paused-group"));

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Blocked,
            "resuming the group while the job still runs leaves the trigger behind the execution — the pause was "
            + "one of two things holding it, and the other is still true");

        await Complete(Firing(fired, running.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A released continuation is an ordinary trigger: the release clears what it waited for
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AReleasedContinuationWaitsForNothingAnyMore()
    {
        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation("released", parent.Key, ContinuationCondition.OnFailure);

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Failed);

        IOperableTrigger released = (await store.GetTrigger(continuation.Key))!;

        released.Continuation.Should().Be(Continuation.None,
            "the wait is over, and a trigger that is still said to wait for its parent is one every later path "
            + "has to remember to treat as released");

        released.GetTriggerBuilder().Build().Continuation.Should().Be(Continuation.None,
            "rebuilding a trigger is how a reschedule keeps what the caller did not change, and a wait that has "
            + "ended is not part of the definition any more — rebuilt, it would wait again for a firing that has been");

        TriggerHeader header = (await store.QueryTriggers(new TriggerQuery { Group = GroupMatcher<TriggerKey>.GroupEquals(Group) }))
            .Items.Single(x => x.Key.Equals(continuation.Key));

        header.ContinuesAfter.Should().BeNull("a listing says what a trigger waits for, and this one waits for nothing");
        header.ContinuationCondition.Should().BeNull();
    }

    [Test]
    public async Task AReleasedCronContinuationResetFromErrorKeepsItsSchedule()
    {
        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "nightly",
            parent.Key,
            ContinuationCondition.OnSuccess,
            configure: x => x.WithCronSchedule("0 0 2 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc)));

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Succeeded);

        // The release fires it at once, and that firing moves it on to its own schedule.
        IOperableTrigger firing = Firing(await Fire(continuation.Key), continuation.Key);

        DateTimeOffset nextNightly = new(2031, 6, 18, 2, 0, 0, TimeSpan.Zero);
        (await store.GetTrigger(continuation.Key))!.NextFireTimeUtc.Should().Be(nextNightly,
            "a released cron continuation keeps the schedule it was given, from the firing the release made on");

        await Complete(firing, ExecutionOutcome.Failed, SchedulerInstruction.SetTriggerError);
        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Error);

        (await store.ResetTriggerFromErrorState(continuation.Key)).Should().BeTrue();

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal);
        (await store.GetTrigger(continuation.Key))!.NextFireTimeUtc.Should().Be(nextNightly,
            "this is an ordinary cron trigger reset from an ordinary error, so it keeps its 02:00 — resetting it "
            + "into 'fire now' is for a continuation parked because its parent was deleted, which this is not");
    }

    [Test]
    public async Task ResettingAContinuationParkedByItsDeletedParentRunsItOnceAndForgetsTheParent()
    {
        IOperableTrigger parent = await ScheduleParent("doomed");
        IOperableTrigger continuation = await ScheduleContinuation("orphaned", parent.Key, ContinuationCondition.OnSuccess);

        await store.DeleteTrigger(parent.Key);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Error,
            "the firing it asked about is never going to happen");

        (await store.ResetTriggerFromErrorState(continuation.Key)).Should().BeTrue();

        IOperableTrigger reset = (await store.GetTrigger(continuation.Key))!;
        reset.NextFireTimeUtc.Should().Be(clock.GetUtcNow(),
            "resetting a parked continuation runs it, at the instant a release would have given it");
        reset.Continuation.Should().Be(Continuation.None,
            "the reset is its release: from here it is an ordinary trigger, so a later error and reset of it keeps "
            + "whatever schedule it is on rather than firing it now again, and a reschedule does not wait for a "
            + "parent that no longer exists");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The release honours the trigger's end time and its calendar
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AContinuationWhoseEndTimeHasPassedByTheReleaseIsDiscarded()
    {
        IOperableTrigger parent = await ScheduleParent("slow-parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "too-late",
            parent.Key,
            ContinuationCondition.OnSuccess,
            configure: x => x.EndAt(clock.GetUtcNow().AddMinutes(30)));

        List<IOperableTrigger> fired = await Fire(parent.Key);

        // The parent runs for an hour, past the half hour the continuation was allowed to fire in.
        clock.Advance(TimeSpan.FromHours(1));
        await Complete(Firing(fired, parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTrigger(continuation.Key)).Should().BeNull(
            "a trigger does not fire after its end time, and a released continuation would fire now — so it has "
            + "no firing left, which is exactly where an outcome its condition did not name would have left it");

        signals.Finalized.Should().Contain(continuation.Key,
            "a discarded continuation is finalized the way one discarded by the outcome is");

        (await AcquireKeys()).Should().NotContain(continuation.Key);
    }

    [Test]
    public async Task AReleaseTheCalendarExcludesFiresAtTheCalendarsNextIncludedInstant()
    {
        // Nothing before noon, UTC. The fixture's clock stands at ten in the morning.
        ICalendar afternoons = new CronCalendar(null, "* * 0-11 ? * *", TimeZoneInfo.Utc);
        await store.AddCalendar("afternoons", afternoons, new AddCalendarOptions { Replace = false, UpdateTriggers = false });

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "afternoon-only",
            parent.Key,
            ContinuationCondition.OnSuccess,
            configure: x => x
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .WithCalendarName("afternoons"),
            calendar: afternoons);

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal);
        (await store.GetTrigger(continuation.Key))!.NextFireTimeUtc.Should().Be(new DateTimeOffset(2031, 6, 17, 12, 0, 0, TimeSpan.Zero),
            "the release would fire it now, the calendar excludes now, and a trigger never fires in an excluded "
            + "instant — so it fires at the first instant the calendar includes");
    }

    [Test]
    public async Task AContinuationTheCalendarPushesPastItsEndTimeIsDiscarded()
    {
        ICalendar afternoons = new CronCalendar(null, "* * 0-11 ? * *", TimeZoneInfo.Utc);
        await store.AddCalendar("afternoons", afternoons, new AddCalendarOptions { Replace = false, UpdateTriggers = false });

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "morning-window",
            parent.Key,
            ContinuationCondition.OnSuccess,
            configure: x => x
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(10)).RepeatForever())
                .WithCalendarName("afternoons")
                .EndAt(clock.GetUtcNow().AddHours(1)),
            calendar: afternoons);

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTrigger(continuation.Key)).Should().BeNull(
            "the first instant the calendar allows is noon, and the trigger was over at eleven");
        signals.Finalized.Should().Contain(continuation.Key);
    }

    [Test]
    public async Task AContinuationWhoseCalendarIncludesNothingIsDiscardedRatherThanFailingTheCompletion()
    {
        ICalendar never = new CronCalendar(null, "* * * ? * *", TimeZoneInfo.Utc);
        await store.AddCalendar("never", never, new AddCalendarOptions());

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "never-fires",
            parent.Key,
            ContinuationCondition.OnSuccess,
            configure: x => x.WithCalendarName("never"));

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTrigger(continuation.Key)).Should().BeNull(
            "a calendar that includes no instant leaves the trigger nothing to fire at — and the parent's "
            + "completion has to settle it rather than fail on the calendar's exception every time it is retried");
        signals.Finalized.Should().Contain(continuation.Key);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A job-wide state change from a sibling's firing leaves an awaiting trigger to its parent
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCase(SchedulerInstruction.SetAllJobTriggersError)]
    [TestCase(SchedulerInstruction.SetAllJobTriggersComplete)]
    public async Task AJobWideStateChangeLeavesItsAwaitingTriggerToTheParent(SchedulerInstruction instruction)
    {
        IJobDetail shared = Job("shared");
        IOperableTrigger sibling = Hourly("sibling", shared.Key);
        await store.ScheduleJob(shared, sibling);

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation("waiting", parent.Key, ContinuationCondition.OnSuccess, shared.Key);

        List<IOperableTrigger> fired = await Fire(sibling.Key, parent.Key);

        // What the scheduler tells the store when the job cannot be built for the sibling's firing, or
        // when the job asks for all of its triggers to be unscheduled.
        await Complete(Firing(fired, sibling.Key), ExecutionOutcome.NotExecuted, instruction);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Awaiting,
            "an awaiting trigger belongs to its parent's settlement: a sibling's firing did not happen to it, and "
            + "moving it would leave it neither waiting for the parent nor anywhere the parent can release it from");

        await Complete(Firing(fired, parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal,
            "the parent succeeded, which is what it was waiting for");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Discarding a continuation discards what waits on it
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task DiscardingAContinuationDiscardsEverythingWaitingOnItAllTheWayDown()
    {
        IOperableTrigger a = await ScheduleParent("a");
        IOperableTrigger b = await ScheduleContinuation("b", a.Key, ContinuationCondition.OnSuccess);
        IOperableTrigger c = await ScheduleContinuation("c", b.Key, ContinuationCondition.OnAnyOutcome);
        IOperableTrigger d = await ScheduleContinuation("d", b.Key, ContinuationCondition.OnSuccess);
        IOperableTrigger e = await ScheduleContinuation("e", c.Key, ContinuationCondition.OnFailure);

        await Complete(Firing(await Fire(a.Key), a.Key), ExecutionOutcome.Failed);

        foreach (IOperableTrigger discarded in new[] { b, c, d, e })
        {
            (await store.GetTrigger(discarded.Key)).Should().BeNull(
                "{0} waited, directly or through others, on a trigger that is never going to run — so no outcome "
                + "it could have been waiting for will ever happen, and it is discarded with it rather than released "
                + "for a firing that did not take place or parked for an operator to puzzle over",
                discarded.Key);
        }

        signals.Finalized.Should().BeEquivalentTo([b.Key, c.Key, d.Key, e.Key],
            "each of them is finalized, which is what a discard says");
        signals.InError.Should().BeEmpty(
            "the deleted-parent rule is for a parent an operator removed, and nothing here was removed by anyone");

        (await AcquireKeys()).Should().BeEmpty("nothing in the chain fires");
    }

    [Test]
    public async Task DeletingAParentStillParksWhatCaredAndReleasesWhatDidNot()
    {
        IOperableTrigger parent = await ScheduleParent("removed");
        IOperableTrigger onSuccess = await ScheduleContinuation("orphaned", parent.Key, ContinuationCondition.OnSuccess);
        IOperableTrigger onAny = await ScheduleContinuation("indifferent", parent.Key, ContinuationCondition.OnAnyOutcome);

        await store.DeleteTrigger(parent.Key);

        (await store.GetTriggerState(onSuccess.Key)).Should().Be(TriggerState.Error,
            "an operator deleting the parent is not the parent failing to run: the question has no answer, which "
            + "is an operator's to see");
        (await store.GetTriggerState(onAny.Key)).Should().Be(TriggerState.Normal);
        signals.InError.Should().Equal([onSuccess.Key]);
        signals.Finalized.Should().BeEmpty();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A continuation whose parent does not exist is refused when it is stored
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AContinuationOfATriggerThatDoesNotExistIsRefusedWithItsJob()
    {
        TriggerKey missing = new("no-such-parent", Group);
        IJobDetail job = Job("orphan-job");
        IOperableTrigger continuation = ContinuationOf(missing, "orphan", job.Key);

        Func<Task> schedule = async () => await store.ScheduleJob(job, continuation);

        ObjectDoesNotExistException refused = (await schedule.Should().ThrowAsync<ObjectDoesNotExistException>(
            "a continuation of a trigger the store does not hold would wait for a firing that can never happen, "
            + "and a trigger nobody can release is one nobody notices")).Which;

        refused.TriggerKey.Should().Be(continuation.Key);
        refused.MissingTriggerKey.Should().Be(missing);
        refused.Message.Should().Contain(continuation.Key.ToString()).And.Contain(missing.ToString(),
            "the message names both keys, so the typo is visible without a debugger");

        (await store.Exists(continuation.Key)).Should().BeFalse();
        (await store.Exists(job.Key)).Should().BeFalse(
            "the refusal is part of the store's own add, so the job stored beside the trigger is refused with it");
    }

    [Test]
    public async Task AddingAContinuationOfATriggerThatDoesNotExistToAStoredJobIsRefused()
    {
        IJobDetail job = Job("existing-job");
        await store.AddJob(job, new AddJobOptions());

        Func<Task> add = async () => await store.AddTrigger(ContinuationOf(new TriggerKey("typo", Group), "orphan", job.Key));

        await add.Should().ThrowAsync<ObjectDoesNotExistException>();
        (await store.GetTriggersForJob(job.Key)).Should().BeEmpty();
    }

    [Test]
    public async Task ReplacingAContinuationWithOneWhoseParentIsGoneIsRefusedAndLeavesItAsItWas()
    {
        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation("parked", parent.Key, ContinuationCondition.OnSuccess);

        await store.DeleteTrigger(parent.Key);
        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Error, "the parent is gone");

        // A reschedule rebuilds the trigger, and the rebuilt one still waits for the deleted parent.
        IOperableTrigger rebuilt = (IOperableTrigger) (await store.GetTrigger(continuation.Key))!.GetTriggerBuilder().Build();
        rebuilt.ComputeFirstFireTimeUtc(null);

        Func<Task> replace = async () => await store.ReplaceTrigger(continuation.Key, rebuilt);

        await replace.Should().ThrowAsync<ObjectDoesNotExistException>(
            "storing it again would park nothing — it would wait, silently and for ever, for a trigger that is gone");

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Error,
            "a refused replacement leaves the trigger it would have replaced exactly as it was");
    }

    [Test]
    public async Task ReplacingAnAwaitingTriggerWithOneThatWaitsForAMissingTriggerIsRefused()
    {
        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation("waiting", parent.Key, ContinuationCondition.OnSuccess);

        IOperableTrigger elsewhere = ContinuationOf(new TriggerKey("nowhere", Group), continuation.Key.Name, continuation.JobKey);

        Func<Task> replace = async () => await store.ReplaceTrigger(continuation.Key, elsewhere);

        await replace.Should().ThrowAsync<ObjectDoesNotExistException>();

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Awaiting);
        (await store.GetTrigger(continuation.Key))!.Continuation.Parent.Should().Be(parent.Key,
            "the trigger still waits for the parent it waited for before the refused replacement");
    }

    [Test]
    public async Task ABatchMayCarryAContinuationBeforeTheParentItWaitsFor()
    {
        IJobDetail parentJob = Job("batch-parent");
        IOperableTrigger parent = Hourly("batch-parent", parentJob.Key);
        IJobDetail continuationJob = Job("batch-continuation");
        IOperableTrigger continuation = ContinuationOf(parent.Key, "batch-continuation", continuationJob.Key);

        // The continuation first: a batch is one operation, and its parent is part of it.
        Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> batch = new()
        {
            [continuationJob] = [continuation],
            [parentJob] = [parent],
        };

        await store.ScheduleJobs(batch);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Awaiting);
        (await store.GetTriggerState(parent.Key)).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task ABatchCarryingAContinuationOfATriggerThatDoesNotExistStoresNothing()
    {
        IJobDetail ordinaryJob = Job("batch-ordinary");
        IOperableTrigger ordinary = Hourly("batch-ordinary", ordinaryJob.Key);
        IJobDetail continuationJob = Job("batch-orphan");
        IOperableTrigger continuation = ContinuationOf(new TriggerKey("absent", Group), "batch-orphan", continuationJob.Key);

        Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> batch = new()
        {
            [ordinaryJob] = [ordinary],
            [continuationJob] = [continuation],
        };

        Func<Task> schedule = async () => await store.ScheduleJobs(batch);

        await schedule.Should().ThrowAsync<ObjectDoesNotExistException>();

        (await store.Exists(ordinary.Key)).Should().BeFalse("the batch is refused as a whole, before anything in it is stored");
        (await store.Exists(ordinaryJob.Key)).Should().BeFalse();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A listener hears of a settlement once it has committed
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ListenersHearOfASettlementOnlyOnceItHasCommitted()
    {
        signals.Observe = CommittedState;

        IOperableTrigger doomed = await ScheduleParent("doomed");
        IOperableTrigger parked = await ScheduleContinuation("parked", doomed.Key, ContinuationCondition.OnSuccess);

        await store.DeleteTrigger(doomed.Key);

        signals.ObservedWhenNotified.Should().ContainKey(parked.Key).WhoseValue.Should().Be(TriggerState.Error,
            "a listener told the trigger is in error reads it in error — told from inside the deletion's "
            + "transaction, it would read the waiting row everyone outside that transaction still sees, and hear "
            + "of a change a rollback could yet undo");

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger discarded = await ScheduleContinuation("discarded", parent.Key, ContinuationCondition.OnFailure);

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Succeeded);

        signals.ObservedWhenNotified.Should().ContainKey(discarded.Key).WhoseValue.Should().Be(TriggerState.None,
            "a listener told the trigger is finalized finds it gone, because it is told after the completion committed");
    }

    /// <summary>
    /// A trigger's state as a reader outside the store's transaction sees it: through a connection of
    /// its own for the database, and through the store for the in-memory one, whose notifications are
    /// raised once its lock is released.
    /// </summary>
    private async Task<TriggerState> CommittedState(TriggerKey key)
    {
        if (kind == ContinuationStoreKind.InMemory)
        {
            return await store.GetTriggerState(key);
        }

        // A short busy timeout, so a reader blocked by an open write fails the test rather than hanging it.
        SqliteConnectionStringBuilder builder = new(database!.ConnectionString) { DefaultTimeout = 5 };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectTriggerState;
        command.Parameters.AddWithValue("@schedulerName", SchedulerName);
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        return await command.ExecuteScalarAsync() switch
        {
            null or DBNull => TriggerState.None,
            AdoConstants.StateError => TriggerState.Error,
            AdoConstants.StateAwaiting => TriggerState.Awaiting,
            _ => TriggerState.Normal
        };
    }

    private const string SelectTriggerState =
        "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    private async ValueTask<IJobStore> InMemoryStore()
    {
        RAMJobStore ram = TestJobStores.Ram(signals, clock);
        await ram.Initialize(TestJobStores.Identity(instanceName: SchedulerName));
        return ram;
    }

    private async ValueTask<IJobStore> SqliteStore()
    {
        LocalTransactionJobStore ado = new(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started: SchedulerStarted() spawns the misfire loop, which
        // would sweep on its own thread and race every assertion here.
        await ado.Initialize(TestJobStores.Identity(instanceName: SchedulerName));

        // One database for the fixture, so each test clears what the previous one left.
        await ado.Clear();
        return ado;
    }

    private static IJobDetail Job(string name, bool nonConcurrent = false)
    {
        if (nonConcurrent)
        {
            return JobBuilder.Create<NonConcurrentSettlementJob>().WithIdentity(name, Group).StoreDurably().Build();
        }

        return JobBuilder.Create<SettlementJob>().WithIdentity(name, Group).StoreDurably().Build();
    }

    /// <summary>
    /// A trigger that is due now and fires every hour after, so a completion leaves it waiting for its
    /// next hour rather than deleting it.
    /// </summary>
    private IOperableTrigger Hourly(string name, JobKey job)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    /// <summary>
    /// A parent: a job of its own and an hourly trigger due now, so the test can fire it and complete it.
    /// </summary>
    private async Task<IOperableTrigger> ScheduleParent(string name)
    {
        IJobDetail job = Job(name);
        IOperableTrigger parent = Hourly(name, job.Key);
        await store.ScheduleJob(job, parent);
        return parent;
    }

    /// <summary>
    /// A continuation of <paramref name="parent" />, with a start time already behind it so that only its
    /// state can keep it from being acquired. Fires <paramref name="job" /> when one is given — the job
    /// has to exist already — and a job of its own otherwise.
    /// </summary>
    private async Task<IOperableTrigger> ScheduleContinuation(
        string name,
        TriggerKey parent,
        ContinuationCondition condition,
        JobKey? job = null,
        string group = Group,
        Func<TriggerBuilder<IJob>, TriggerBuilder<IJob>>? configure = null,
        ICalendar? calendar = null)
    {
        IJobDetail? ownJob = job is null ? Job(name) : null;

        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, group)
            .ForJob(job ?? ownJob!.Key)
            .StartAt(clock.GetUtcNow().AddMinutes(-1))
            .StartAfter(parent, condition);

        IOperableTrigger continuation = (IOperableTrigger) (configure?.Invoke(builder) ?? builder).Build();
        continuation.ComputeFirstFireTimeUtc(calendar);

        if (ownJob is not null)
        {
            await store.ScheduleJob(ownJob, continuation);
        }
        else
        {
            await store.AddTrigger(continuation);
        }

        return continuation;
    }

    /// <summary>
    /// A continuation of <paramref name="parent" /> for <paramref name="job" />, built but not stored.
    /// </summary>
    private IOperableTrigger ContinuationOf(TriggerKey parent, string name, JobKey job)
    {
        IOperableTrigger continuation = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow().AddMinutes(-1))
            .StartAfter(parent)
            .Build();

        continuation.ComputeFirstFireTimeUtc(null);
        return continuation;
    }

    /// <summary>
    /// Acquires and fires the named triggers, which must be exactly what is due.
    /// </summary>
    private async Task<List<IOperableTrigger>> Fire(params TriggerKey[] keys)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.FromMinutes(1)
        });

        acquired.Select(x => x.Key).Should().BeEquivalentTo(keys, "the triggers under test are the ones due");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().OnlyContain(x => x.TriggerFiredBundle != null,
            "a firing has to be committed before completing it says anything");

        return acquired;
    }

    private static IOperableTrigger Firing(List<IOperableTrigger> fired, TriggerKey key)
    {
        return fired.Single(x => x.Key.Equals(key));
    }

    /// <summary>
    /// What an acquisition would take right now, handed straight back so it changes nothing.
    /// </summary>
    private async Task<List<TriggerKey>> AcquireKeys()
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.FromMinutes(1)
        });

        foreach (IOperableTrigger trigger in acquired)
        {
            await store.ReleaseAcquiredTrigger(trigger);
        }

        return acquired.Select(x => x.Key).ToList();
    }

    private async Task Complete(
        IOperableTrigger firing,
        ExecutionOutcome outcome,
        SchedulerInstruction instruction = SchedulerInstruction.NoInstruction)
    {
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = (await store.GetJob(firing.JobKey))!,
            Instruction = instruction,
            Outcome = outcome
        });
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Records what the store told the scheduler about, in the order it said it — and, when asked to,
    /// what somebody outside the store could read of the trigger at the moment it was told.
    /// </summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> Finalized { get; } = [];

        public List<TriggerKey> InError { get; } = [];

        /// <summary>Reads a trigger's state the way a listener calling back in would see it.</summary>
        public Func<TriggerKey, Task<TriggerState>>? Observe { get; set; }

        public Dictionary<TriggerKey, TriggerState> ObservedWhenNotified { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public async ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Finalized.Add(trigger.Key);
            await ObserveWhenNotified(trigger.Key);
        }

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public async ValueTask NotifySchedulerListenersTriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            InError.Add(triggerKey);
            await ObserveWhenNotified(triggerKey);
        }

        private async Task ObserveWhenNotified(TriggerKey key)
        {
            if (Observe is not null)
            {
                ObservedWhenNotified[key] = await Observe(key);
            }
        }

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class SettlementJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A job that forbids concurrent execution, which is what makes a store block its other triggers.</summary>
    [DisallowConcurrentExecution]
    public sealed class NonConcurrentSettlementJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
