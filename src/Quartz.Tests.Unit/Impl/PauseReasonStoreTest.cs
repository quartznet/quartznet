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

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Which store <see cref="PauseReasonStoreTest" /> is run against.
/// </summary>
public enum PauseStoreKind
{
    InMemory,
    Sqlite
}

/// <summary>
/// What a pause records — why, who asked and when — and when a store answers it, asserted against the
/// in-memory store and against a SQLite-backed ADO store.
/// </summary>
/// <remarks>
/// Driven against the store on a clock the test owns, with the store initialized but never started, as
/// <c>OverlapPolicyStoreTest</c> is. The SQLite cases that write a row by hand stand in for a 4.2 node
/// sharing the database: it pauses and resumes without naming the three columns.
/// </remarks>
[TestFixture(PauseStoreKind.InMemory)]
[TestFixture(PauseStoreKind.Sqlite)]
public sealed class PauseReasonStoreTest
{
    private const string Group = "pauses";
    private const string SchedulerName = "PauseReasonStoreTest";
    private const string DataSourceName = "pause-reason";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly PauseDetails maintenance = new() { Reason = "database maintenance", RequestedBy = "alice" };

    private readonly PauseStoreKind kind;

    private SqliteTestDatabase? database;
    private IDbProvider? dbProvider;

    private FakeTimeProvider clock = null!;
    private IJobStore store = null!;

    public PauseReasonStoreTest(PauseStoreKind kind)
    {
        this.kind = kind;
    }

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        if (kind != PauseStoreKind.Sqlite)
        {
            return;
        }

        database = new SqliteTestDatabase("pause-reason");

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
        store = kind == PauseStoreKind.InMemory ? await InMemoryStore() : await SqliteStore();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // One trigger
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task APauseWithAReasonIsReadBackUntilTheTriggerIsResumed()
    {
        TriggerKey key = await Schedule("nightly");

        (await store.PauseTriggerWith(key, maintenance)).Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(5));

        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "the pause is stamped with the store's clock when it is made, not when it is read");
        (await Header(key)).Pause.Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "the listing carries the trigger's own record, so a page of triggers needs no read per row");

        (await store.ResumeTrigger(key)).Should().BeTrue();

        (await store.GetTriggerPause(key)).Should().BeNull("a resumed trigger has no pause to explain");
        (await Header(key)).Pause.Should().BeNull();

        if (kind == PauseStoreKind.Sqlite)
        {
            (await ReadColumn(AdoConstants.ColumnPauseReason, key)).Should().BeNull(
                "a 4.3 node's resume forgets the reason, so a later pause by a node that records none cannot "
                + "bring it back");
            (await ReadColumn(AdoConstants.ColumnPausedBy, key)).Should().BeNull();
            (await ReadColumn(AdoConstants.ColumnPausedAt, key)).Should().BeNull();
        }
    }

    [Test]
    public async Task AReasonlessPauseRecordsNothing()
    {
        TriggerKey reasonless = await Schedule("reasonless");
        TriggerKey withNull = await Schedule("with-null");
        TriggerKey saysNothing = await Schedule("says-nothing");

        (await store.PauseTrigger(reasonless)).Should().BeTrue();
        (await store.PauseTriggerWith(withNull, null)).Should().BeTrue();
        (await store.PauseTriggerWith(saysNothing, new PauseDetails())).Should().BeTrue();

        foreach (TriggerKey key in new[] { reasonless, withNull, saysNothing })
        {
            (await store.GetTriggerState(key)).Should().Be(TriggerState.Paused);
            (await store.GetTriggerPause(key)).Should().BeNull(
                "a pause that says nothing is the pause 4.2 made, and leaves nothing behind to read");
            (await Header(key)).Pause.Should().BeNull();

            if (kind == PauseStoreKind.Sqlite)
            {
                (await ReadColumn(AdoConstants.ColumnPausedAt, key)).Should().BeNull(
                    "the reasonless pause writes what 4.2 wrote, which is the state and nothing else");
            }
        }
    }

    [Test]
    public async Task ATriggerThatWasAlreadyPausedKeepsThePauseItHad()
    {
        TriggerKey key = await Schedule("nightly");
        await store.PauseTriggerWith(key, maintenance);

        clock.Advance(TimeSpan.FromHours(1));
        (await store.PauseTriggerWith(key, new PauseDetails { Reason = "second thoughts", RequestedBy = "bob" }))
            .Should().BeFalse("the trigger was already paused, so this call moved nothing");

        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "a pause that changes nothing records nothing; the reason is the one the trigger was paused for");
    }

    [Test]
    public async Task ATriggerThatIsMissingOrRunningHasNoPause()
    {
        TriggerKey key = await Schedule("nightly");

        (await store.GetTriggerPause(key)).Should().BeNull("a trigger that is not paused has nothing to say about a pause");
        (await store.GetTriggerPause(new TriggerKey("missing", Group))).Should().BeNull();
        (await store.PauseTriggerWith(new TriggerKey("missing", Group), maintenance)).Should().BeFalse();
    }

    [Test]
    public async Task TextsLongerThanTheirColumnsAreCutAndBlankOnesAreNone()
    {
        TriggerKey cut = await Schedule("cut");
        TriggerKey blank = await Schedule("blank");

        // A surrogate pair straddling the limit: the cut falls before it rather than through it.
        string reason = new string('r', PauseDetails.MaxReasonLength - 1) + "\U0001F600" + "tail";
        string requester = new('u', PauseDetails.MaxRequestedByLength + 50);

        await store.PauseTriggerWith(cut, new PauseDetails { Reason = reason, RequestedBy = requester });
        await store.PauseTriggerWith(blank, new PauseDetails { Reason = "   ", RequestedBy = "" });

        PauseInfo? stored = await store.GetTriggerPause(cut);
        stored!.Reason.Should().Be(new string('r', PauseDetails.MaxReasonLength - 1),
            "a reason is cut to what the column holds, and never between the halves of a surrogate pair");
        stored.RequestedBy.Should().HaveLength(PauseDetails.MaxRequestedByLength);

        (await store.GetTriggerState(blank)).Should().Be(TriggerState.Paused);
        (await store.GetTriggerPause(blank)).Should().BeNull(
            "blank texts say nothing, so the pause is the reasonless one and records nothing");
    }

    [Test]
    public async Task ABlockedTriggerPausedWithAReasonKeepsItWhileItsJobRuns()
    {
        TriggerKey key = await Schedule("nightly", nonConcurrent: true);
        await Fire(key);

        (await store.PauseTriggerWith(key, maintenance)).Should().BeTrue(
            "a blocked trigger is pausable: it becomes paused-blocked");

        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "paused-blocked is paused, and says why");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Jobs and groups
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task PausingAJobRecordsTheReasonOnEachOfItsTriggers()
    {
        IJobDetail job = await StoreJob("reports");
        TriggerKey first = await Schedule("first", job);
        TriggerKey second = await Schedule("second", job);

        (await store.PauseJobWith(job.Key, maintenance)).Should().BeTrue();

        (await store.GetTriggerPause(first)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));
        (await store.GetTriggerPause(second)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "a job is paused through its triggers, so each of them carries the reason");

        await store.ResumeJob(job.Key);
        (await store.GetTriggerPause(first)).Should().BeNull();
        (await store.GetTriggerPause(second)).Should().BeNull();
    }

    [Test]
    public async Task PausingATriggerGroupRecordsTheReasonOnTheGroupAndOnItsTriggers()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");

        (await store.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals(Group), maintenance)).Should().Equal([Group]);

        (await store.GetTriggerGroupPause(Group)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "the group's own record is what outlives the triggers it held when it was paused");
        (await store.GetTriggerPause(first)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));
        (await Header(second)).Pause.Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "the triggers the pause moved carry the record themselves, so the listing shows it");

        (await store.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group))).Should().Equal([Group]);

        (await store.GetTriggerGroupPause(Group)).Should().BeNull("the group's row goes with its pause");
        (await store.GetTriggerPause(first)).Should().BeNull();
        (await store.GetTriggerPause(second)).Should().BeNull();
    }

    [Test]
    public async Task ATriggerStoredIntoAPausedGroupAnswersTheGroupsReason()
    {
        await store.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals(Group), maintenance);
        clock.Advance(TimeSpan.FromMinutes(10));

        TriggerKey key = await Schedule("added-later");

        (await store.GetTriggerState(key)).Should().Be(TriggerState.Paused, "a paused group pauses what is stored into it");
        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "the trigger has no pause of its own, and the group's is why it is paused");
        (await Header(key)).Pause.Should().BeNull(
            "the listing carries the record on the trigger itself, and this one has none");
    }

    [Test]
    public async Task PausingAJobGroupRecordsTheReasonOnTheGroupAndOnItsJobsTriggers()
    {
        IJobDetail job = await StoreJob("reports");
        TriggerKey key = await Schedule("nightly", job);

        (await store.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals(Group), maintenance)).Should().Equal([Group]);

        (await store.GetJobGroupPause(Group)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));
        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));

        await store.ResumeJobGroups(GroupMatcher<JobKey>.GroupEquals(Group));

        (await store.GetJobGroupPause(Group)).Should().BeNull();
        (await store.GetTriggerPause(key)).Should().BeNull();
    }

    [Test]
    public async Task AJobAddedToAPausedJobGroupAnswersTheGroupsReason()
    {
        await store.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals(Group), maintenance);

        TriggerKey key = await Schedule("added-later");

        (await store.GetTriggerState(key)).Should().Be(TriggerState.Paused);
        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "the job group's record answers for a trigger of a job added to the group after it was paused");
    }

    [Test]
    public async Task PauseAllRecordsTheReasonOnEveryGroupAndTrigger()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey other = await Schedule("other", group: "elsewhere");

        await store.PauseAllWith(maintenance);

        (await store.GetTriggerGroupPause(Group)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));
        (await store.GetTriggerGroupPause("elsewhere")).Should().Be(new PauseInfo("database maintenance", "alice", epoch));
        (await store.GetTriggerPause(first)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));
        (await store.GetTriggerPause(other)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));

        await store.ResumeAll();

        (await store.GetTriggerGroupPause(Group)).Should().BeNull();
        (await store.GetTriggerGroupPause("elsewhere")).Should().BeNull();
        (await store.GetTriggerPause(first)).Should().BeNull();
        (await store.GetTriggerPause(other)).Should().BeNull();
    }

    [Test]
    public async Task AGroupFirstPausedByPauseAllCarriesThePauseAllsReason()
    {
        IgnoreUnlessSqlite("the in-memory store's pause-all pauses the groups it holds and remembers no marker");

        await store.PauseAllWith(maintenance);

        TriggerKey key = await Schedule("added-later", group: "brand-new");

        (await store.GetTriggerState(key)).Should().Be(TriggerState.Paused, "pause-all pauses a group nothing had used yet");
        (await store.GetTriggerGroupPause("brand-new")).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "the group's row is written as it always was, and the pause-all's record answers for a row that has none");
        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch));
    }

    [Test]
    public async Task ReasonlessGroupPausesRecordNothing()
    {
        TriggerKey key = await Schedule("first");

        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group));
        await store.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals("jobs-" + Group), null);
        await store.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("other"), new PauseDetails { Reason = " " });

        (await store.GetTriggerState(key)).Should().Be(TriggerState.Paused, "the reasonless group pause paused its trigger");
        (await store.GetTriggerState(await Schedule("born-paused", group: "other"))).Should().Be(TriggerState.Paused,
            "the group paused with details that said nothing is paused all the same");

        (await store.GetTriggerGroupPause(Group)).Should().BeNull("the group is paused, and the pause said nothing");
        (await store.GetJobGroupPause("jobs-" + Group)).Should().BeNull();
        (await store.GetTriggerGroupPause("other")).Should().BeNull();
        (await store.GetTriggerPause(key)).Should().BeNull();
        (await store.GetTriggerGroupPause("never-paused")).Should().BeNull();
    }

    [Test]
    public async Task TheReasonlessSetFormsRecordNothing()
    {
        IJobDetail job = await StoreJob("reports");
        TriggerKey ofJob = await Schedule("of-job", job);
        TriggerKey single = await Schedule("single");

        (await store.PauseTriggers([single])).Should().Equal([single]);
        (await store.PauseJobs([job.Key])).Should().Equal([job.Key]);

        (await store.GetTriggerPause(single)).Should().BeNull();
        (await store.GetTriggerPause(ofJob)).Should().BeNull();

        (await store.ResumeTriggers([single, ofJob])).Should().Equal([single, ofJob]);
        (await store.GetTriggerState(single)).Should().Be(TriggerState.Normal,
            "a resume of a trigger that carries no record still resumes it");
    }

    [Test]
    public async Task AReasonlessResumeOfAPauseWithAReasonForgetsIt()
    {
        TriggerKey key = await Schedule("nightly");
        await store.PauseTriggerWith(key, maintenance);

        (await store.ResumeTriggers([key])).Should().Equal([key]);
        await store.PauseTrigger(key);

        (await store.GetTriggerPause(key)).Should().BeNull(
            "the resume forgot the first pause's reason, so the second pause, which said nothing, has none");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Sets of keys
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task PausingASetOfTriggersRecordsTheReasonOnEachOneItPausesInTheOrderGiven()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");
        TriggerKey earlier = await Schedule("earlier");
        await store.PauseTriggerWith(earlier, new PauseDetails { Reason = "earlier window", RequestedBy = "bob" });

        clock.Advance(TimeSpan.FromHours(1));
        TriggerKey missing = new("missing", Group);

        (await store.PauseTriggersWith([second, missing, earlier, first], maintenance)).Should().Equal([second, first],
            "a missing key and one already paused are absent, and the rest keep the order they were given in");

        PauseInfo recorded = new("database maintenance", "alice", epoch.AddHours(1));
        (await store.GetTriggerPause(first)).Should().Be(recorded);
        (await store.GetTriggerPause(second)).Should().Be(recorded);
        (await store.GetTriggerPause(earlier)).Should().Be(new PauseInfo("earlier window", "bob", epoch),
            "a trigger that was already paused keeps the pause it had");
        (await Header(second)).Pause.Should().Be(recorded, "the listing carries the set pause's record too");
    }

    [Test]
    public async Task PausingASetOfJobsRecordsTheReasonOnEachOfTheirTriggers()
    {
        IJobDetail reports = await StoreJob("reports");
        TriggerKey reportsFirst = await Schedule("reports-first", reports);
        TriggerKey reportsSecond = await Schedule("reports-second", reports);
        IJobDetail exports = await StoreJob("exports");
        TriggerKey ofExports = await Schedule("of-exports", exports);
        JobKey missing = new("missing", Group);

        (await store.PauseJobsWith([exports.Key, missing, reports.Key], maintenance)).Should().Equal([exports.Key, reports.Key],
            "a key that names no job is absent, and the rest keep the order they were given in");

        PauseInfo recorded = new("database maintenance", "alice", epoch);
        (await store.GetTriggerPause(reportsFirst)).Should().Be(recorded);
        (await store.GetTriggerPause(reportsSecond)).Should().Be(recorded);
        (await store.GetTriggerPause(ofExports)).Should().Be(recorded);
    }

    [Test]
    public async Task ASetPauseThatSaysNothingRecordsNothing()
    {
        IJobDetail job = await StoreJob("reports");
        TriggerKey ofJob = await Schedule("of-job", job);
        TriggerKey single = await Schedule("single");

        (await store.PauseTriggersWith([single], new PauseDetails { Reason = "  " })).Should().Equal([single]);
        (await store.PauseJobsWith([job.Key], null)).Should().Equal([job.Key]);

        (await store.GetTriggerState(single)).Should().Be(TriggerState.Paused);
        (await store.GetTriggerState(ofJob)).Should().Be(TriggerState.Paused);
        (await store.GetTriggerPause(single)).Should().BeNull("blank details are the reasonless pause");
        (await store.GetTriggerPause(ofJob)).Should().BeNull();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Beside a 4.2 node
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ARowA42NodePausedReadsAsAPauseWithNoRecord()
    {
        IgnoreUnlessSqlite("a 4.2 node shares a database, not an in-memory store");

        TriggerKey key = await Schedule("nightly");
        await WriteState(key, AdoConstants.StatePaused);

        (await store.GetTriggerState(key)).Should().Be(TriggerState.Paused);
        (await store.GetTriggerPause(key)).Should().BeNull(
            "a 4.2 node's pause leaves the columns NULL, which is a pause that recorded nothing");
        (await Header(key)).Pause.Should().BeNull();
    }

    [Test]
    public async Task AReasonA42NodesResumeLeftBehindIsNotReported()
    {
        IgnoreUnlessSqlite("a 4.2 node shares a database, not an in-memory store");

        TriggerKey key = await Schedule("nightly");
        await store.PauseTriggerWith(key, maintenance);

        // A 4.2 node's resume moves the state and names none of the three columns.
        await WriteState(key, AdoConstants.StateWaiting);

        (await ReadColumn(AdoConstants.ColumnPauseReason, key)).Should().Be("database maintenance",
            "the precondition: the old node left the reason behind");
        (await store.GetTriggerPause(key)).Should().BeNull(
            "a reason is read only while the trigger is paused, so a running trigger never reports one");
        (await Header(key)).Pause.Should().BeNull();

        clock.Advance(TimeSpan.FromHours(1));
        await store.PauseTrigger(key);

        // The hazard the upgrade notes name: a pause without a reason makes 4.2's statements, which
        // do not touch the columns, so what the 4.2 node left reads as current again.
        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("database maintenance", "alice", epoch),
            "a reasonless pause writes what 4.2 wrote, so it cannot clear what a 4.2 resume left behind");

        await store.ResumeTrigger(key);
        clock.Advance(TimeSpan.FromHours(1));
        await store.PauseTriggerWith(key, new PauseDetails { Reason = "second window", RequestedBy = "bob" });

        (await store.GetTriggerPause(key)).Should().Be(new PauseInfo("second window", "bob", epoch.AddHours(2)),
            "a 4.3 node's resume forgets the record, and a pause with a reason writes its own");
    }

    [Test]
    public async Task AGroupRowA42NodeWroteReadsAsAPauseWithNoRecord()
    {
        IgnoreUnlessSqlite("a 4.2 node shares a database, not an in-memory store");

        await Execute(
            "INSERT INTO QRTZ_PAUSED_TRIGGER_GRPS (SCHED_NAME, TRIGGER_GROUP) VALUES (@sched, 'old-trigger-group')");
        await Execute(
            "INSERT INTO QRTZ_PAUSED_JOB_GRPS (SCHED_NAME, JOB_GROUP) VALUES (@sched, 'old-job-group')");

        (await store.GetTriggerGroupPause("old-trigger-group")).Should().BeNull(
            "the row says the group is paused and nothing more");
        (await store.GetJobGroupPause("old-job-group")).Should().BeNull();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    private void IgnoreUnlessSqlite(string reason)
    {
        if (kind != PauseStoreKind.Sqlite)
        {
            Assert.Ignore(reason);
        }
    }

    private async ValueTask<IJobStore> InMemoryStore()
    {
        RAMJobStore ram = TestJobStores.Ram(timeProvider: clock);
        await ram.Initialize(TestJobStores.Identity(instanceName: SchedulerName));
        return ram;
    }

    private async ValueTask<IJobStore> SqliteStore()
    {
        LocalTransactionJobStore ado = new(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await ado.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));
        await ado.Clear();
        return ado;
    }

    private async Task<IJobDetail> StoreJob(string name, bool nonConcurrent = false)
    {
        IJobDetail job = nonConcurrent
            ? JobBuilder.Create<NonConcurrentPauseJob>().WithIdentity(name, Group).StoreDurably().Build()
            : JobBuilder.Create<PauseJob>().WithIdentity(name, Group).StoreDurably().Build();

        await store.AddJob(job);
        return job;
    }

    private async Task<TriggerKey> Schedule(string name, bool nonConcurrent = false, string group = Group)
    {
        IJobDetail job = await StoreJob("job-" + name + "-" + group, nonConcurrent);
        return await Schedule(name, job, group);
    }

    private async Task<TriggerKey> Schedule(string name, IJobDetail job, string group = Group)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);
        return trigger.Key;
    }

    /// <summary>Acquires and fires the trigger, which then runs until the test completes it.</summary>
    private async Task Fire(TriggerKey key)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        acquired.Select(x => x.Key).Should().Equal([key], "the trigger under test is the one due");
        (await store.TriggersFired(acquired)).Should().OnlyContain(x => x.TriggerFiredBundle != null);
    }

    private async Task<TriggerHeader> Header(TriggerKey key)
    {
        PagedResult<TriggerHeader> page = await store.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals(key.Group),
            Take = PagedQuery.All
        });

        return page.Items.Single(x => x.Key.Equals(key));
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

    private async Task WriteState(TriggerKey key, string state)
    {
        await using SqliteConnection connection = new(database!.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = @state WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@state", state);
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        await command.ExecuteNonQueryAsync();
    }

    private async Task Execute(string sql)
    {
        await using SqliteConnection connection = new(database!.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@sched", SchedulerName);
        await command.ExecuteNonQueryAsync();
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class PauseJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A job that forbids concurrent execution, whose triggers are blocked while it runs.</summary>
    [DisallowConcurrentExecution]
    public sealed class NonConcurrentPauseJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
