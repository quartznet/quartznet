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
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A node that acquired a trigger ahead of its fire time holds a copy of it until then. Another node
/// replaces the trigger meanwhile and acquires the replacement. The held copy is not fired, does not
/// write the old definition back, and its release does not undo the other node's acquisition (#4323).
/// </summary>
/// <remarks>
/// <para>
/// Two containers on one SQLite file, as in <see cref="AcquisitionBehindExecutingJobSqliteTest" />: each
/// step is a store call awaited in sequence, so the interleaving is the test's, and the rows are read
/// straight off the tables. The clustered PostgreSQL run of the same scenario is
/// <c>ReplacedTriggerClusteredPostgresTest</c> in the integration suite.
/// </para>
/// <para>
/// Run twice: through the shipped delegate, whose fire reads the trigger and its reservation in one
/// statement, and through a subclass of it, which keeps the single-trigger members it may override and
/// so reads the reservation on its own.
/// </para>
/// </remarks>
[TestFixture(true)]
[TestFixture(false)]
[NonParallelizable]
public sealed class ReplacedTriggerAcquisitionSqliteTest
{
    private const string Group = "replaced";
    private static readonly JobKey jobKey = new("job", Group);
    private static readonly TriggerKey triggerKey = new("trigger", Group);

    private readonly bool shippedDelegate;

    private SqliteTestDatabase database = null!;
    private ServiceProvider nodeA = null!;
    private ServiceProvider nodeB = null!;
    private IScheduler schedulerA = null!;
    private IScheduler schedulerB = null!;
    private IJobStore storeA = null!;
    private IJobStore storeB = null!;
    private DateTimeOffset due;

    public ReplacedTriggerAcquisitionSqliteTest(bool shippedDelegate)
    {
        this.shippedDelegate = shippedDelegate;
    }

    [SetUp]
    public async Task CreateTwoNodesOnOneDatabase()
    {
        database = new SqliteTestDatabase("replaced-trigger-acquisition");

        nodeA = BuildNode("node-a", provisionSchema: true);
        schedulerA = await nodeA.GetRequiredService<ISchedulerFactory>().GetScheduler();
        storeA = nodeA.GetRequiredService<IJobStore>();

        nodeB = BuildNode("node-b", provisionSchema: false);
        schedulerB = await nodeB.GetRequiredService<ISchedulerFactory>().GetScheduler();
        storeB = nodeB.GetRequiredService<IJobStore>();

        // Nodes of one cluster, which is what they stand in for. A SQLite store refuses to be configured
        // clustered, so it is told after it has started.
        ((AdoJobStoreBase) storeA).Clustered = true;
        ((AdoJobStoreBase) storeB).Clustered = true;

        // Ahead of now, so that no misfire handling moves a fire time under the test.
        due = TimeProvider.System.GetUtcNow().AddSeconds(30);
        due = due.AddTicks(-(due.Ticks % TimeSpan.TicksPerSecond));

        await schedulerA.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build());
        await schedulerA.ScheduleJob(Trigger(TimeSpan.FromHours(1), due));
    }

    [TearDown]
    public async Task DisposeNodes()
    {
        await nodeB.DisposeAsync();
        await nodeA.DisposeAsync();
        database.Dispose();
    }

    /// <summary>
    /// The issue's case, at the hardest fire time: the replacement is due when the held copy is, so
    /// comparing fire times alone would let the old copy fire and write its definition back.
    /// </summary>
    [Test]
    public async Task AHeldCopyOfATriggerAnotherNodeReplacedIsNotFiredAndDoesNotWriteItsDefinitionBack()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        (await schedulerB.RescheduleJob(triggerKey, Trigger(TimeSpan.FromHours(2), due))).Should().Be(due);
        IOperableTrigger heldByB = await ReserveOne(storeB);
        heldByB.NextFireTimeUtc.Should().Be(heldByA.NextFireTimeUtc, "the premise: the replacement is due when A's copy is");

        List<TriggerFiredResult> onA = await storeA.TriggersFired([heldByA]);
        onA.Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull(
            "A's acquisition was of the trigger B replaced, which is no longer stored");
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2), "A's copy is not written over the replacement");

        await storeA.ReleaseAcquiredTrigger(heldByA);
        (await StateOf()).Should().Be("ACQUIRED", "the row is B's acquisition, and A's release is not B's to undo");
        (await FiredRows()).Should().Equal([("node-b", "ACQUIRED")], "A's reservation went with the trigger it reserved");

        TriggerFiredBundle firedOnB = (await storeB.TriggersFired([heldByB])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        firedOnB.Should().NotBeNull("B's acquisition is still its own");
        ((ISimpleTrigger) firedOnB.Trigger).RepeatInterval.Should().Be(TimeSpan.FromHours(2));
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2));
        (await NextFireTimeStored()).Should().Be(due.AddHours(2), "the replacement's schedule is the one that moves on");
    }

    /// <summary>
    /// A held copy released before its fire time — the scheduler's schedule change, standby, shutdown —
    /// leaves the replacement as another node acquired it.
    /// </summary>
    [Test]
    public async Task TheReleaseOfAHeldCopyOfAReplacedTriggerLeavesTheOtherNodesAcquisition()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        await schedulerB.RescheduleJob(triggerKey, Trigger(TimeSpan.FromHours(2), due.AddSeconds(5)));
        IOperableTrigger heldByB = await ReserveOne(storeB);

        await storeA.ReleaseAcquiredTrigger(heldByA);

        (await StateOf()).Should().Be("ACQUIRED",
            "moved back to WAITING, the row could be acquired by a third node for the fire time B holds");
        (await FiredRows()).Should().Equal([("node-b", "ACQUIRED")]);
        (await storeB.TriggersFired([heldByB])).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "B's acquisition survives A's release");
    }

    /// <summary>
    /// A replace by key that writes the row over rather than deleting it — a schedule call told to replace
    /// what is stored — cancels the held acquisition just the same.
    /// </summary>
    [Test]
    public async Task AReplaceInPlaceCancelsTheHeldAcquisitionToo()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        ScheduleTriggerResult replaced = await schedulerB.ScheduleTrigger(Trigger(TimeSpan.FromHours(2), due), TriggerConflict.Replace);
        replaced.Outcome.Should().Be(ScheduleOutcome.Replaced);
        IOperableTrigger heldByB = await ReserveOne(storeB);

        (await storeA.TriggersFired([heldByA])).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull();
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2));
        await storeA.ReleaseAcquiredTrigger(heldByA);

        (await storeB.TriggersFired([heldByB])).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2));
    }

    /// <summary>
    /// A held copy whose fire time another node has fired since is not fired a second time, even while its
    /// reservation is still there: here a pause and resume let B acquire and fire the same fire time.
    /// </summary>
    [Test]
    public async Task AFireTimeAnotherNodeFiredSinceIsNotFiredAgainFromAnEarlierAcquisition()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        await schedulerB.PauseTrigger(triggerKey);
        await schedulerB.ResumeTrigger(triggerKey);
        TriggerFiredBundle firedOnB = (await storeB.TriggersFired([await ReserveOne(storeB)])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        firedOnB.Should().NotBeNull("the premise: B fired the fire time A still holds");
        await storeB.TriggeredJobComplete(firedOnB.Trigger, firedOnB.JobDetail, SchedulerInstruction.NoInstruction);
        IOperableTrigger nextOnB = await ReserveOne(storeB);
        nextOnB.NextFireTimeUtc.Should().Be(due.AddHours(1), "the premise: B holds the fire time after it");

        (await storeA.TriggersFired([heldByA])).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull(
            $"{due:O} has run, on B, and A's acquisition of it is out of date");
        (await NextFireTimeStored()).Should().Be(due.AddHours(1), "A's fire did not move the trigger");
    }

    /// <summary>
    /// After a pause and resume let B fire T and acquire T2, A's reservation row for T is still there;
    /// releasing A's outdated acquisition deletes only A's own row and leaves B's ACQUIRED row for T2
    /// intact (#4349).
    /// </summary>
    [Test]
    public async Task ReleasingAnOutdatedAcquisitionAfterPauseAndResumeLeavesTheOtherNodesNewerAcquisition()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        await schedulerB.PauseTrigger(triggerKey);
        await schedulerB.ResumeTrigger(triggerKey);
        TriggerFiredBundle firedOnB = (await storeB.TriggersFired([await ReserveOne(storeB)])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        firedOnB.Should().NotBeNull("the premise: B fired the fire time A still holds");
        await storeB.TriggeredJobComplete(firedOnB.Trigger, firedOnB.JobDetail, SchedulerInstruction.NoInstruction);
        IOperableTrigger nextOnB = await ReserveOne(storeB);
        nextOnB.NextFireTimeUtc.Should().Be(due.AddHours(1), "the premise: B holds the fire time after it");

        (await storeA.TriggersFired([heldByA])).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull();
        await storeA.ReleaseAcquiredTrigger(heldByA);

        (await StateOf()).Should().Be("ACQUIRED",
            "the row is B's acquisition for T2, and A's release for T must not move it back to WAITING");
        (await NextFireTimeStored()).Should().Be(due.AddHours(1));
        (await FiredRows()).Should().Equal([("node-b", "ACQUIRED")],
            "A's own reservation row is deleted while B's acquisition survives");

        TriggerFiredBundle nextFiredOnB = (await storeB.TriggersFired([nextOnB])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        nextFiredOnB.Should().NotBeNull("B's acquisition for T2 survives A's release");
        (await NextFireTimeStored()).Should().Be(due.AddHours(2));
    }

    /// <summary>
    /// The same outdated-release race through a BLOCKED round-trip on a job that disallows concurrent
    /// execution: a sibling trigger's firing moves A's acquired trigger to BLOCKED and its completion
    /// returns it to WAITING, letting B fire T and acquire T2 while A's reservation row remains (#4349).
    /// </summary>
    [Test]
    public async Task ReleasingAnOutdatedAcquisitionAfterBlockedRoundTripLeavesTheOtherNodesNewerAcquisition()
    {
        JobKey serialJobKey = new("serial-job", Group);
        TriggerKey serialTriggerKey = new("serial-trigger", Group);
        TriggerKey siblingTriggerKey = new("serial-sibling", Group);

        await schedulerA.AddJob(JobBuilder.Create<SerialNoOpJob>().WithIdentity(serialJobKey).StoreDurably().Build());
        await schedulerA.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(serialTriggerKey)
            .ForJob(serialJobKey)
            .StartAt(due)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build());
        await schedulerA.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(siblingTriggerKey)
            .ForJob(serialJobKey)
            .StartAt(due.AddSeconds(1))
            .WithSimpleSchedule(x => x.WithRepeatCount(0))
            .Build());

        // Clear the default fixture trigger so only the serial job's triggers are acquired.
        await schedulerA.UnscheduleJob(triggerKey);

        IOperableTrigger heldByA = await ReserveOne(storeA);
        heldByA.Key.Should().Be(serialTriggerKey);

        // B fires the sibling, which moves serialTriggerKey ACQUIRED -> BLOCKED, then completes it
        // (BLOCKED -> WAITING), letting B fire T and acquire T2 while A's reservation row for T remains.
        IOperableTrigger siblingOnB = await ReserveOne(storeB);
        siblingOnB.Key.Should().Be(siblingTriggerKey);
        TriggerFiredBundle siblingFired = (await storeB.TriggersFired([siblingOnB])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        siblingFired.Should().NotBeNull();
        await storeB.TriggeredJobComplete(siblingFired.Trigger, siblingFired.JobDetail, SchedulerInstruction.NoInstruction);

        TriggerFiredBundle firedOnB = (await storeB.TriggersFired([await ReserveOne(storeB)])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        firedOnB.Should().NotBeNull("the premise: B fired the fire time A still holds");
        await storeB.TriggeredJobComplete(firedOnB.Trigger, firedOnB.JobDetail, SchedulerInstruction.NoInstruction);
        IOperableTrigger nextOnB = await ReserveOne(storeB);
        nextOnB.NextFireTimeUtc.Should().Be(due.AddHours(1), "the premise: B holds the fire time after it");

        await storeA.ReleaseAcquiredTrigger(heldByA);

        (await StateOf(serialTriggerKey)).Should().Be("ACQUIRED",
            "the row is B's acquisition for T2, and A's release for T must not move it back to WAITING");
        (await NextFireTimeStored(serialTriggerKey)).Should().Be(due.AddHours(1));
        (await FiredRows()).Should().Equal([("node-b", "ACQUIRED")],
            "A's own reservation row is deleted while B's acquisition survives");

        TriggerFiredBundle nextFiredOnB = (await storeB.TriggersFired([nextOnB])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        nextFiredOnB.Should().NotBeNull("B's acquisition for T2 survives A's release");
        (await NextFireTimeStored(serialTriggerKey)).Should().Be(due.AddHours(2));
    }

    /// <summary>
    /// What a replace cancels is a reservation, not an execution: the fired-trigger row of a firing still
    /// running stays, for its completion or for recovery to settle (#3759).
    /// </summary>
    [Test]
    public async Task AReplaceKeepsTheRowOfAFiringStillRunning()
    {
        TriggerFiredBundle running = (await storeA.TriggersFired([await ReserveOne(storeA)])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        running.Should().NotBeNull();
        IOperableTrigger heldByA = await ReserveOne(storeA);

        await schedulerB.RescheduleJob(triggerKey, Trigger(TimeSpan.FromHours(2), due.AddHours(3)));

        (await FiredRows()).Should().Equal([("node-a", "EXECUTING")],
            "the reservation of the held copy is cancelled, the row of the firing still running is not");
        (await storeA.TriggersFired([heldByA])).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull();
        await storeA.ReleaseAcquiredTrigger(heldByA);
        (await StateOf()).Should().Be("WAITING", "nothing holds the replacement");

        await storeA.TriggeredJobComplete(running.Trigger, running.JobDetail, SchedulerInstruction.NoInstruction);
        (await FiredRows()).Should().BeEmpty();
    }

    /// <summary>
    /// The control: an acquisition nothing replaced is fired and released as before.
    /// </summary>
    [Test]
    public async Task AnAcquisitionNothingReplacedIsFiredAndReleasedAsBefore()
    {
        IOperableTrigger held = await ReserveOne(storeA);
        await storeA.ReleaseAcquiredTrigger(held);
        (await StateOf()).Should().Be("WAITING");
        (await FiredRows()).Should().BeEmpty();

        TriggerFiredBundle fired = (await storeA.TriggersFired([await ReserveOne(storeA)])).Should().ContainSingle().Subject.TriggerFiredBundle!;
        fired.Should().NotBeNull();
        (await NextFireTimeStored()).Should().Be(due.AddHours(1));
    }

    private static ITrigger Trigger(TimeSpan interval, DateTimeOffset startAt)
    {
        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();
    }

    private static async Task<IOperableTrigger> ReserveOne(IJobStore store)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddDays(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        return acquired.Should().ContainSingle("the trigger is the only one stored, and it is waiting").Subject;
    }

    private async Task<TimeSpan> IntervalStored()
    {
        return TimeSpan.FromMilliseconds(await ReadLong("SELECT REPEAT_INTERVAL FROM QRTZ_SIMPLE_TRIGGERS WHERE TRIGGER_NAME = @name"));
    }

    private async Task<DateTimeOffset> NextFireTimeStored(TriggerKey? key = null)
    {
        return new DateTimeOffset(await ReadLong("SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", key), TimeSpan.Zero);
    }

    private async Task<string> StateOf(TriggerKey? key = null)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", (key ?? triggerKey).Name);
        return (string) (await command.ExecuteScalarAsync())!;
    }

    private async Task<long> ReadLong(string sql, TriggerKey? key = null)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", (key ?? triggerKey).Name);
        return (long) (await command.ExecuteScalarAsync())!;
    }

    private async Task<List<(string Instance, string State)>> FiredRows()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT INSTANCE_NAME, STATE FROM QRTZ_FIRED_TRIGGERS ORDER BY INSTANCE_NAME, STATE";

        List<(string, string)> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private ServiceProvider BuildNode(string instanceId, bool provisionSchema)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "replaced-trigger-acquisition";
                options.InstanceId = instanceId;
            });

            q.UsePersistentStore(store =>
            {
                // Before UseSqlite, whose registration is a try-add.
                if (!shippedDelegate)
                {
                    store.UseDriverDelegate<SubclassedSqliteDelegate>();
                }

                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                if (provisionSchema)
                {
                    store.ProvisionSchema();
                }
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A delegate of somebody's own: the store asks it only the members it may have overridden.
    /// </summary>
    public sealed class SubclassedSqliteDelegate : SQLiteDelegate
    {
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    public sealed class SerialNoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
