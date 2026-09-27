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
/// Which store <see cref="OverlapPolicyStoreTest" /> is run against.
/// </summary>
public enum OverlapStoreKind
{
    InMemory,
    Sqlite
}

/// <summary>
/// What a trigger's <see cref="OverlapPolicy" /> does when a firing of it comes due while an earlier one
/// runs, asserted against the in-memory store and against a SQLite-backed ADO store.
/// </summary>
/// <remarks>
/// Driven against the store rather than through a scheduler, on a clock the test owns, with the store
/// initialized but never started, as <c>ContinuationSettlementTest</c> is and for the same reasons. A
/// firing "runs" from the moment <see cref="IJobStore.TriggersFired" /> committed it until the test
/// completes it.
/// </remarks>
[TestFixture(OverlapStoreKind.InMemory)]
[TestFixture(OverlapStoreKind.Sqlite)]
public sealed class OverlapPolicyStoreTest
{
    private const string Group = "overlap";
    private const string SchedulerName = "OverlapPolicyStoreTest";
    private const string DataSourceName = "overlap-policy";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly OverlapStoreKind kind;

    private SqliteTestDatabase? database;
    private IDbProvider? dbProvider;

    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private IJobStore store = null!;
    private IJobStore? otherNode;

    public OverlapPolicyStoreTest(OverlapStoreKind kind)
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

        database = new SqliteTestDatabase("overlap-policy");

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
        store = kind == OverlapStoreKind.InMemory ? await InMemoryStore() : await SqliteStore("node-a", clear: true);
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        if (otherNode is not null)
        {
            await otherNode.Shutdown();
            otherNode = null;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Default and AllowAll: today's behaviour
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCase(OverlapPolicy.Default)]
    [TestCase(OverlapPolicy.AllowAll)]
    public async Task ADueFiringStartsBesideTheRunningOne(OverlapPolicy policy)
    {
        IOperableTrigger trigger = await Schedule("trigger", policy);
        await Fire(trigger.Key);

        clock.Advance(TimeSpan.FromHours(1));
        List<TriggerFiredResult> second = await FireResults(trigger.Key);

        second.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            $"{policy} starts a firing that comes due whatever else of the trigger is running");
        second[0].TriggerFiredBundle!.SupersededFireInstanceIds.Should().BeNull(
            "nothing is interrupted unless the trigger says CancelPrevious");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Skip
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task SkipDropsTheDueFiringAndMovesTheTriggerOnWithoutAMisfire()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.Skip);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        clock.Advance(TimeSpan.FromHours(1));
        List<TriggerFiredResult> second = await FireResults(trigger.Key);

        second.Should().ContainSingle().Which.IsDeclined.Should().BeTrue(
            "the earlier firing is still running and the trigger says Skip");

        (await store.GetTrigger(trigger.Key))!.NextFireTimeUtc.Should().Be(epoch.AddHours(2),
            "the dropped occurrence is advanced past as a firing would advance past it");
        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Executing,
            "the earlier firing still runs, and a skip holds nothing else back");
        await StoredStateShouldBe(trigger.Key, AdoConstants.StateWaiting, "a skip leaves the trigger waiting for its next occurrence");

        signals.Skipped.Should().ContainSingle().Which.Should().Be((trigger.Key, epoch.AddHours(1)),
            "the listeners are told which firing was dropped, which is what the misfire history records");
        signals.Misfired.Should().BeEmpty("a skipped firing is not a misfire and is never reported as one");

        if (kind == OverlapStoreKind.Sqlite)
        {
            (await FiredRowCount(trigger.Key)).Should().Be(1,
                "the declined acquisition's fired row goes with it; only the running firing's is left");
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        (await AcquireKeys()).Should().BeEmpty("the dropped occurrence is not due any more, and the next is an hour away");
        (await RecoverMisfires()).Should().Be(0, "the misfire handler never sees an occurrence that was skipped");
        signals.Misfired.Should().BeEmpty("and so never reports it");

        await Complete(first[0]);
        clock.Advance(TimeSpan.FromMinutes(55));
        (await FireResults(trigger.Key)).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "once the running firing ended, the next occurrence fires as usual");
    }

    [Test]
    public async Task SkippingTheLastOccurrenceFinalizesTheTriggerAndTheRunningFiringRemovesIt()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.Skip, repeatCount: 1);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        clock.Advance(TimeSpan.FromHours(1));
        (await FireResults(trigger.Key)).Should().ContainSingle().Which.IsDeclined.Should().BeTrue();

        await StoredStateShouldBe(trigger.Key, AdoConstants.StateComplete,
            "the skipped occurrence was the trigger's last, so it has nothing left to fire");
        signals.Finalized.Should().Contain(trigger.Key,
            "no firing of that occurrence will run to say the trigger is finished, so the skip says it");

        await Complete(first[0]);

        (await store.GetTrigger(trigger.Key)).Should().BeNull(
            "a spent trigger is deleted rather than left complete, and the running firing's completion is "
            + "the one that owes it the deletion");
    }

    [Test]
    public async Task ATriggerUpdatedFromSkipToDefaultWhileItRunsStartsItsNextFiring()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.Skip);
        await Fire(trigger.Key);

        (await store.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithOverlapPolicy(OverlapPolicy.Default)))
            .Should().BeTrue();

        clock.Advance(TimeSpan.FromHours(1));
        (await FireResults(trigger.Key)).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the policy is read when a firing comes due, and by then it says Default");
        signals.Skipped.Should().BeEmpty();
    }

    [Test]
    public async Task ASkipDoesNotSettleAContinuationButTheRunningFiringsCompletionDoes()
    {
        IOperableTrigger parent = await Schedule("parent", OverlapPolicy.Skip);
        IOperableTrigger continuation = await ScheduleContinuation("continuation", parent.Key);

        List<IOperableTrigger> first = await Fire(parent.Key);

        clock.Advance(TimeSpan.FromHours(1));
        (await FireResults(parent.Key)).Should().ContainSingle().Which.IsDeclined.Should().BeTrue();

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Awaiting,
            "nothing ran for the skipped occurrence, so there is no outcome to settle the continuation with");

        await Complete(first[0], ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal,
            "the firing that did run settles it when it ends, as every firing settles its own");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // BufferOne
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task BufferOneHoldsTheTriggerWhileItsFiringRunsAndFiresOnceWhenItEnds()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.BufferOne, cron: true);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        await StoredStateShouldBe(trigger.Key, AdoConstants.StateBlocked,
            "the firing holds its own trigger back while it runs, as DisallowConcurrentExecution holds a job's");

        clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));
        (await AcquireKeys()).Should().BeEmpty("three occurrences came due while it ran, and none is acquired");

        await Complete(first[0]);

        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal);
        List<IOperableTrigger> buffered = await Fire(trigger.Key);
        buffered.Should().ContainSingle("the backlog collapses to one firing: the cron misfire instruction fires it once, now");

        await Complete(buffered[0]);
        (await AcquireKeys()).Should().BeEmpty(
            "the buffered firing was the only one kept; the next is the next hour");
    }

    [Test]
    public async Task BufferOneFiresTheOccurrenceThatCameDueWithinTheMisfireThreshold()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.BufferOne, cron: true);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(3));
        await Complete(first[0]);

        (await AcquireKeys()).Should().Equal([trigger.Key],
            "released three seconds after its occurrence came due, the trigger fires it straight away");
        signals.Misfired.Should().BeEmpty("three seconds late is within either store's misfire threshold");
    }

    [Test]
    public async Task ABufferedTriggerThatIsPausedAndResumedIsStillHeldWhileItsFiringRuns()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.BufferOne);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        await store.PauseTrigger(trigger.Key);
        (await store.ResumeTrigger(trigger.Key)).Should().BeTrue();

        await StoredStateShouldBe(trigger.Key, AdoConstants.StateBlocked,
            "its own firing still runs, and that is what held it back before it was paused");

        clock.Advance(TimeSpan.FromHours(1));
        (await AcquireKeys()).Should().BeEmpty("a resume must not let a second firing start beside the first");

        await Complete(first[0]);
        (await AcquireKeys()).Should().Equal([trigger.Key], "the completion lets go of it");
    }

    [Test]
    public async Task ABufferedTriggerPausedWhenItsFiringEndsGoesBackToPaused()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.BufferOne);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        await store.PauseTrigger(trigger.Key);
        await Complete(first[0]);

        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused,
            "the firing lets go of it, and what is left is the pause somebody asked for");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // CancelPrevious
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task CancelPreviousFiresAndNamesTheRunningFiringsOfThisNode()
    {
        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.CancelPrevious);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        clock.Advance(TimeSpan.FromHours(1));
        List<TriggerFiredResult> second = await FireResults(trigger.Key);

        TriggerFiredBundle bundle = second.Should().ContainSingle().Which.TriggerFiredBundle!;
        bundle.Should().NotBeNull("the new firing starts");
        bundle.SupersededFireInstanceIds.Should().Equal([first[0].FireInstanceId],
            "the running firing is this node's, so the scheduler is told to interrupt it");

        await StoredStateShouldBe(trigger.Key, AdoConstants.StateWaiting, "nothing holds the trigger back");
    }

    [Test]
    public async Task CancelPreviousWaitsForAFiringOnAnotherNodeAndFiresWhenItEnds()
    {
        IgnoreUnlessSqlite("Only a database is shared by two nodes.");

        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.CancelPrevious);
        otherNode = await SqliteStore("node-b", clear: false);

        List<IOperableTrigger> remote = await Fire(otherNode, trigger.Key);

        clock.Advance(TimeSpan.FromHours(1));
        (await FireResults(trigger.Key)).Should().ContainSingle().Which.IsDeclined.Should().BeTrue(
            "an interrupt cannot reach another node, so the trigger waits for that firing as BufferOne would");

        await StoredStateShouldBe(trigger.Key, AdoConstants.StateBlocked, "held until the other node's firing ends");
        (await FiredRowCount(trigger.Key)).Should().Be(1, "the held acquisition leaves no fired row behind");
        (await AcquireKeys()).Should().BeEmpty();

        await Complete(otherNode, remote[0]);

        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Normal,
            "the other node's firing started under CancelPrevious, and its completion lets go of the trigger");
        (await Fire(trigger.Key)).Should().ContainSingle("the occurrence that was held fires now");
    }

    [Test]
    public async Task CancelPreviousGivenWhileAnotherPolicysFiringRunsElsewhereDoesNotWaitForIt()
    {
        IgnoreUnlessSqlite("Only a database is shared by two nodes.");

        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.Default);
        otherNode = await SqliteStore("node-b", clear: false);

        List<IOperableTrigger> remote = await Fire(otherNode, trigger.Key);

        await store.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithOverlapPolicy(OverlapPolicy.CancelPrevious));
        Convert.ToInt32(await ReadColumn("OVERLAP_POLICY", trigger.Key)).Should().Be(19,
            "a Default firing runs on another node, and its completion would not let go of a held trigger");

        clock.Advance(TimeSpan.FromHours(1));
        List<TriggerFiredResult> second = await FireResults(trigger.Key);
        second.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "holding the trigger behind a firing whose completion never releases it would stop it for good");
        second[0].TriggerFiredBundle!.SupersededFireInstanceIds.Should().BeNull("the running firing is not this node's");

        await Complete(otherNode, remote[0]);
        await Complete(second[0].TriggerFiredBundle!.Trigger);

        clock.Advance(TimeSpan.FromHours(1));
        await Fire(trigger.Key);
        Convert.ToInt32(await ReadColumn("OVERLAP_POLICY", trigger.Key)).Should().Be((int) OverlapPolicy.CancelPrevious,
            "a firing that found nothing running settled the policy, so the next overlap is waited for");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // What every policy leaves alone
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCase(OverlapPolicy.Default)]
    [TestCase(OverlapPolicy.Skip)]
    [TestCase(OverlapPolicy.BufferOne)]
    [TestCase(OverlapPolicy.CancelPrevious)]
    [TestCase(OverlapPolicy.AllowAll)]
    public async Task DisallowConcurrentExecutionHoldsTheTriggerWhateverThePolicy(OverlapPolicy policy)
    {
        IOperableTrigger trigger = await Schedule("trigger", policy, nonConcurrent: true);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        await StoredStateShouldBe(trigger.Key, AdoConstants.StateBlocked,
            "the job-level rule holds every trigger of the job back while one of its firings runs");

        clock.Advance(TimeSpan.FromHours(1));
        (await AcquireKeys()).Should().BeEmpty($"{policy} never overrides DisallowConcurrentExecution");
        signals.Skipped.Should().BeEmpty("the policy never sees an overlap the job-level rule already prevented");

        await Complete(first[0]);
        (await AcquireKeys()).Should().Equal([trigger.Key], "the job's completion lets go of it, as it always did");
    }

    [TestCase(OverlapPolicy.Skip)]
    [TestCase(OverlapPolicy.BufferOne)]
    [TestCase(OverlapPolicy.CancelPrevious)]
    public async Task ARetryIsNotHeldBackByTheAttemptThatFailed(OverlapPolicy policy)
    {
        IOperableTrigger trigger = await Schedule("trigger", policy, retry: true);
        List<IOperableTrigger> first = await Fire(trigger.Key);

        IOperableTrigger failed = first[0];
        failed.RetryAttempt = 1;
        failed.NextFireTimeUtc = clock.GetUtcNow().AddSeconds(30);
        await Complete(failed, ExecutionOutcome.Failed, SchedulerInstruction.RetryTrigger);

        clock.Advance(TimeSpan.FromSeconds(30));
        List<TriggerFiredResult> retry = await FireResults(trigger.Key);

        retry.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "the failed attempt is over, and a retry continues its occurrence rather than overlapping it");
        retry[0].TriggerFiredBundle!.Trigger.RetryAttempt.Should().Be(1);
    }

    [TestCase(OverlapPolicy.Skip)]
    [TestCase(OverlapPolicy.BufferOne)]
    [TestCase(OverlapPolicy.CancelPrevious)]
    [TestCase(OverlapPolicy.AllowAll)]
    public async Task ThePolicyIsStoredReadBackAndListed(OverlapPolicy policy)
    {
        IOperableTrigger trigger = await Schedule("trigger", policy);

        (await store.GetTrigger(trigger.Key))!.OverlapPolicy.Should().Be(policy);

        PagedResult<TriggerHeader> listed = await store.QueryTriggers(new TriggerQuery());
        listed.Items.Should().ContainSingle().Which.OverlapPolicy.Should().Be(policy,
            "a listing reports the policy without materializing the trigger");

        if (kind == OverlapStoreKind.Sqlite)
        {
            Convert.ToInt32(await ReadColumn("OVERLAP_POLICY", trigger.Key)).Should().Be((int) policy,
                "the column holds the integer of the policy");
        }
    }

    [Test]
    public async Task ADefaultTriggerWritesTheRowA42NodeWould()
    {
        IgnoreUnlessSqlite("The column is the database's.");

        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.Default);

        (await ReadColumn("OVERLAP_POLICY", trigger.Key)).Should().BeNull(
            "Default is written as NULL, which is also what every row a 4.2 node writes holds");
        (await store.GetTrigger(trigger.Key))!.OverlapPolicy.Should().Be(OverlapPolicy.Default);
    }

    [Test]
    public async Task AValueANewerNodeWroteReadsAsDefault()
    {
        IgnoreUnlessSqlite("The column is the database's.");

        IOperableTrigger trigger = await Schedule("trigger", OverlapPolicy.Skip);
        await WriteColumn("OVERLAP_POLICY", 99, trigger.Key);

        (await store.GetTrigger(trigger.Key))!.OverlapPolicy.Should().Be(OverlapPolicy.Default,
            "a row is still a schedule, and refusing to read it would take the job out of service");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The state the database row holds, which is what the acquisition reads; the reported state puts a
    /// running firing ahead of it. The in-memory store has no row, and its tests read acquisition instead.
    /// </summary>
    private async Task StoredStateShouldBe(TriggerKey key, string state, string because)
    {
        if (kind == OverlapStoreKind.Sqlite)
        {
            (await ReadColumn("TRIGGER_STATE", key)).Should().Be(state, because);
        }
    }

    private void IgnoreUnlessSqlite(string reason)
    {
        if (kind != OverlapStoreKind.Sqlite)
        {
            Assert.Ignore(reason);
        }
    }

    private async ValueTask<IJobStore> InMemoryStore()
    {
        RAMJobStore ram = TestJobStores.Ram(signals, clock);
        await ram.Initialize(TestJobStores.Identity(instanceName: SchedulerName));
        return ram;
    }

    private async ValueTask<IJobStore> SqliteStore(string instanceId, bool clear)
    {
        LocalTransactionJobStore ado = new(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: instanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await ado.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: instanceId));

        if (clear)
        {
            await ado.Clear();
        }

        return ado;
    }

    private async Task<IOperableTrigger> Schedule(
        string name,
        OverlapPolicy policy,
        bool nonConcurrent = false,
        bool cron = false,
        int repeatCount = -1,
        bool retry = false)
    {
        IJobDetail job = nonConcurrent
            ? JobBuilder.Create<NonConcurrentOverlapJob>().WithIdentity(name, Group).StoreDurably().Build()
            : JobBuilder.Create<OverlapJob>().WithIdentity(name, Group).StoreDurably().Build();

        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .WithOverlapPolicy(policy);

        builder = cron
            ? builder.WithCronSchedule("0 0 * * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
            : builder.WithSimpleSchedule(x =>
            {
                x.WithInterval(TimeSpan.FromHours(1));
                if (repeatCount < 0)
                {
                    x.RepeatForever();
                }
                else
                {
                    x.WithRepeatCount(repeatCount);
                }
            });

        if (retry)
        {
            builder = builder.WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30)));
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await store.ScheduleJob(job, trigger);
        return trigger;
    }

    private async Task<IOperableTrigger> ScheduleContinuation(string name, TriggerKey parent)
    {
        IJobDetail job = JobBuilder.Create<OverlapJob>().WithIdentity(name, Group).StoreDurably().Build();
        IOperableTrigger continuation = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow().AddMinutes(-1))
            .StartAfter(parent, ContinuationCondition.OnAnyOutcome)
            .Build();

        continuation.ComputeFirstFireTimeUtc(null);
        await store.ScheduleJob(job, continuation);
        return continuation;
    }

    private Task<List<IOperableTrigger>> Fire(params TriggerKey[] keys) => Fire(store, keys);

    /// <summary>
    /// Acquires and fires the named triggers on <paramref name="node" />, which must be exactly what is
    /// due, and must all fire.
    /// </summary>
    private async Task<List<IOperableTrigger>> Fire(IJobStore node, params TriggerKey[] keys)
    {
        (List<IOperableTrigger> acquired, List<TriggerFiredResult> results) = await AcquireAndFire(node, keys);

        results.Should().OnlyContain(x => x.TriggerFiredBundle != null, "a firing has to start before it can run");
        return results.Select(x => x.TriggerFiredBundle!.Trigger).ToList();
    }

    private async Task<List<TriggerFiredResult>> FireResults(params TriggerKey[] keys)
    {
        (_, List<TriggerFiredResult> results) = await AcquireAndFire(store, keys);
        return results;
    }

    private async Task<(List<IOperableTrigger> Acquired, List<TriggerFiredResult> Results)> AcquireAndFire(IJobStore node, TriggerKey[] keys)
    {
        List<IOperableTrigger> acquired = await node.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        acquired.Select(x => x.Key).Should().BeEquivalentTo(keys, "the triggers under test are the ones due");

        List<TriggerFiredResult> results = await node.TriggersFired(acquired);
        return (acquired, results);
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

    private Task Complete(
        IOperableTrigger firing,
        ExecutionOutcome outcome = ExecutionOutcome.Succeeded,
        SchedulerInstruction instruction = SchedulerInstruction.NoInstruction)
    {
        return Complete(store, firing, outcome, instruction);
    }

    private static async Task Complete(
        IJobStore node,
        IOperableTrigger firing,
        ExecutionOutcome outcome = ExecutionOutcome.Succeeded,
        SchedulerInstruction instruction = SchedulerInstruction.NoInstruction)
    {
        await node.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = (await node.GetJob(firing.JobKey))!,
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

    private async Task WriteColumn(string column, int value, TriggerKey key)
    {
        await using SqliteConnection connection = new(database!.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"UPDATE QRTZ_TRIGGERS SET {column} = @value WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> FiredRowCount(TriggerKey key)
    {
        await using SqliteConnection connection = new(database!.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        return (long) (await command.ExecuteScalarAsync())!;
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Records what the store told the scheduler about.
    /// </summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<(TriggerKey Key, DateTimeOffset? ScheduledFireTimeUtc)> Skipped { get; } = [];

        public List<TriggerKey> Misfired { get; } = [];

        public List<TriggerKey> Finalized { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Misfired.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifyTriggerListenersSkipped(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Skipped.Add((trigger.Key, trigger.NextFireTimeUtc));
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
    public sealed class OverlapJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A job that forbids concurrent execution, which holds every trigger of it back while it runs.</summary>
    [DisallowConcurrentExecution]
    public sealed class NonConcurrentOverlapJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
