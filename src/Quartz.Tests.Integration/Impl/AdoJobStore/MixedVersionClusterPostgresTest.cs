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

using System.Diagnostics;
using System.Globalization;
using System.Text;

using AwesomeAssertions.Execution;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Quartz.Extensibility;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A released node of the previous minor, 4.6, and a working-tree node share one PostgreSQL cluster and
/// one execution history through a rolling upgrade whose migration runs while the released node is up,
/// and nothing is lost, doubled or overwritten.
/// </summary>
/// <remarks>
/// <para>
/// <b>The upgrade.</b> The schema starts as 4.3.0's fresh install with every migration through 4.6
/// applied over it (<see cref="MigrationChains.Since" />): an operator's 4.6 database. The released node —
/// Quartz 4.6.0 from nuget.org — starts on it and schedules half of the work. Then every migration since
/// 4.6 runs while that node is up, as the migration guide says to, and the working-tree node, which
/// refuses a schema without them, joins and schedules the other half. Both run with their shipped
/// defaults and keep their execution history in the database.
/// </para>
/// <para>
/// <b>The nodes.</b> <c>Quartz.Tests.Integration.MixedVersionNode</c> is built twice with one assembly
/// name, so a job type either node stores reads back the same in the other; each is a process of its
/// own, because the two Quartz assemblies share an identity. Every execution is written to a plain
/// table by the job that ran, and the assertions read that table and the store's columns directly.
/// </para>
/// <para>
/// <b>What must hold.</b> Two thousand one-offs each run exactly once, some on each node; the report says
/// how many. A <see cref="DisallowConcurrentExecutionAttribute" /> job with recurring triggers never
/// overlaps itself, whichever version runs it. Every continuation is settled once, by a parent that
/// completed on either node. The pause and overlap columns the working tree wrote survive the released
/// node firing, misfiring, updating, pausing and resuming the rows around them, and a trigger the working
/// tree gave <c>RetryPolicy.None</c> is fired by the released node and read back as it was written. A
/// trigger the working tree made measure its interval from completion is fired by the released node at a
/// fixed rate, and its <c>FIXED_DELAY</c> is still set after that node fired and updated it (#4140). Every
/// execution is in the history once, under the node that ran it, with the outcome that node's jobs report.
/// </para>
/// <para>
/// <b>The lock.</b> Each node reads the other's version stamp as 4.6 or later, so neither keeps its rounds
/// locked for the other (#4067): once the work that needs the lock is stood down, a round of
/// concurrent-allowed work on either node takes none, and the nodes' <c>round-locks</c> commands say so.
/// The other arm turns <c>LockFreeFiringRounds</c> off on the working tree and asserts that every one of
/// its rounds is locked, beside a released node whose rounds are not.
/// </para>
/// <para>
/// It runs in the PostgreSQL leg. Against a server at its shipped durability — the Testcontainers one
/// runs with <c>fsync</c> off — it runs like this, and the report goes to the detailed output:
/// </para>
/// <code>
/// docker run -d --name g8-pg -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet -e POSTGRES_DB=quartznet -p 55432:5432 postgres:15.1
/// $env:QUARTZ_TEST_DATABASE = 'basic'   # start no container
/// $env:PG_CONNECTION_STRING = 'Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
/// dotnet test --project src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj `
///   --filter 'FullyQualifiedName~MixedVersionClusterPostgresTest' --output Detailed
/// </code>
/// </remarks>
[NonParallelizable]
[Category("db-postgres")]
[Category("migrations")]
public sealed class MixedVersionClusterPostgresTest
{
    /// <summary>The released Quartz the old node runs; the node project's csproj names the same one.</summary>
    private const string ReleasedQuartz = "4.6.0";

    /// <summary>
    /// The release whose fresh install the database starts as: its install plus every migration through
    /// the released node's minor is the database an operator on that minor has.
    /// </summary>
    private const string ReleasedSchema = "4.3";

    private const string SchedulerName = "MixedVersionGate";

    /// <summary>
    /// This fixture's own, in the database every PostgreSQL fixture shares: <c>MigrationScriptTest</c> and
    /// <c>UpgradeRehearsalTest</c> own <c>QRTZM_</c>, <c>QRTZD_</c>, <c>QRTZS_</c> and <c>QRTZU_</c>.
    /// </summary>
    private const string TablePrefix = "QRTZV_";

    private const string RunsTable = "mixed_version_runs";

    private const string Released = "node-46";
    private const string WorkingTree = "node-47";

    private const int OneOffs = 2_000;

    /// <summary>
    /// The serial job's triggers each node schedules for each of its windows: two pinned to itself, and
    /// two either node may fire.
    /// </summary>
    private const int SerialTriggersEach = 2;

    /// <summary>
    /// How many firings each of the serial job's three windows stays open for: the released node's pinned
    /// triggers, then the working tree's, then the triggers either node may fire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows rather than every trigger at once, because a [DisallowConcurrentExecution] job stays with
    /// the node running it: that node's completion lets the job's other triggers go and its own loop is
    /// the first to reach for them. Left to that race, one node ran the job for the whole run as often
    /// as not. The pinned windows hand it from one version to the other; the last one is the race.
    /// </para>
    /// <para>
    /// A count rather than a stretch of time. Ten-second windows at fixed instants once saw a released
    /// node's pinned triggers fire 4 times against a floor of 10, when the drain before them overran its
    /// allowance; a window now opens once the drain is over and closes once it has fired this many.
    /// </para>
    /// </remarks>
    private const int SerialFloor = 10;

    /// <summary>The serial job's interval, in milliseconds.</summary>
    private const int SerialIntervalMs = 1000;

    /// <summary>The interval of the fixed-delay trigger the released node fires, in milliseconds.</summary>
    private const int FixedDelayIntervalMs = 1000;

    /// <summary>
    /// How many more rounds the working tree has to run after the stand-down, in the arm with lock-free
    /// rounds off, before its rounds are read.
    /// </summary>
    private const int RoundsAfterStandDown = 5;

    private const int ChainPairs = 40;
    private const int ProgressFirings = 5;

    private const string PauseReason = "held for the 4.4 upgrade — ünïcode kept";
    private const string PausedBy = "g8-gate";
    private const string UpdatedDescription = "updated by the released node";
    private const int UpdatedPriority = 7;

    /// <summary>
    /// From the first trigger scheduled to the instant the work is due: long enough to start the
    /// working-tree node and have both nodes write their halves.
    /// </summary>
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long one serial window may stay open waiting for its firings before the gate moves on and the
    /// assertions say what is missing.
    /// </summary>
    private static readonly TimeSpan SerialWindowLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long after the work is due the workload may take before the assertions say what is missing:
    /// twice the run, and short enough that a failing gate reports inside the leg's timeout.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How long the nodes are given, once the work that needs the lock is stood down, to each run a round
    /// of concurrent-allowed work without it.
    /// </summary>
    private static readonly TimeSpan LockFreeWithin = TimeSpan.FromSeconds(60);

    private const string CreateRunsTable =
        "CREATE TABLE " + RunsTable + " ("
        + "id BIGSERIAL PRIMARY KEY, trigger_group TEXT NOT NULL, trigger_name TEXT NOT NULL, job_group TEXT NOT NULL, "
        + "job_name TEXT NOT NULL, fire_instance_id TEXT NOT NULL, node TEXT NOT NULL, scheduled_fire_utc BIGINT NULL, "
        + "fired_utc BIGINT NOT NULL, started_utc BIGINT NOT NULL, started BIGINT NOT NULL, ended BIGINT NOT NULL, "
        + "recovering BOOLEAN NOT NULL, progress INTEGER NULL, progress_message TEXT NULL)";

    private const string SelectRuns =
        "SELECT trigger_group, trigger_name, job_group, job_name, fire_instance_id, node, scheduled_fire_utc, fired_utc, started_utc, "
        + "started, ended, recovering, progress, progress_message FROM " + RunsTable + " ORDER BY started";

    private const string SelectHistory =
        "SELECT instance_name, job_group, job_name, succeeded, result, summary, metrics, manual, fire_instance_id "
        + "FROM qrtzv_execution_history WHERE sched_name = @schedulerName";

    private const string SelectJobStatus =
        "SELECT job_group, job_name, run_count, last_instance_name FROM qrtzv_job_status WHERE sched_name = @schedulerName";

    private const string SelectTriggerColumns =
        "SELECT trigger_state, overlap_policy, pause_reason, paused_by, paused_at, description, priority, retry_policy, fixed_delay "
        + "FROM qrtzv_triggers WHERE sched_name = @schedulerName AND trigger_group = @group AND trigger_name = @name";

    private const string SelectTriggerGroupPause =
        "SELECT pause_reason, paused_by, paused_at FROM qrtzv_paused_trigger_grps WHERE sched_name = @schedulerName AND trigger_group = @group";

    private const string SelectJobGroupPause =
        "SELECT pause_reason, paused_by, paused_at FROM qrtzv_paused_job_grps WHERE sched_name = @schedulerName AND job_group = @group";

    private const string SelectLeftoverChain =
        "SELECT trigger_name, trigger_state FROM qrtzv_triggers WHERE sched_name = @schedulerName AND trigger_group = 'chain' ORDER BY trigger_name";

    private const string BackdatePausedTrigger =
        "UPDATE qrtzv_triggers SET next_fire_time = next_fire_time - @age "
        + "WHERE sched_name = @schedulerName AND trigger_group = @group AND trigger_name = @name AND trigger_state = 'PAUSED'";

    private const string SelectLeftoverTables =
        "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() "
        + "AND (table_name LIKE 'qrtzv\\_%' OR table_name = '" + RunsTable + "')";

    /// <param name="lockFreeRounds">
    /// Whether the working tree runs with the shipped <c>LockFreeFiringRounds</c>, in which case a round of
    /// concurrent-allowed work beside the released node takes no lock, or with it off, in which case none of
    /// its rounds goes without.
    /// </param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task AReleased46NodeAndAWorkingTreeNodeShareOneClusterAndOneHistory(bool lockFreeRounds)
    {
        string connectionString = MigrationScriptTest.RequireConnectionString("PG_CONNECTION_STRING");
        string releasedAssembly = NodeAssembly(ReleasedQuartz);
        string workingTreeAssembly = NodeAssembly(null);

        Stopwatch elapsed = Stopwatch.StartNew();
        await PrepareDatabase(connectionString);

        MixedVersionNodeProcess released = null;
        MixedVersionNodeProcess workingTree = null;
        try
        {
            // The rolling upgrade: the released node is up, and writes its half, before the new node exists.
            released = await MixedVersionNodeProcess.Start(releasedAssembly, Released, SchedulerName, connectionString, TablePrefix, RunsTable, history: true);
            released.QuartzVersion.Should().StartWith(ReleasedQuartz, "the old node has to run the released package, not the working tree");
            released.Build.Should().Be("released");
            await released.Send("start");

            DateTimeOffset due = DateTimeOffset.UtcNow + Lead;
            await ScheduleHalf(released, first: 0, due);

            // The working tree's migrations, run while the released node is up and has rows of its own in the
            // tables they alter. The working tree refuses to start without them.
            await MigrateWhileReleasedIsUp(connectionString);
            (await released.Send("cluster-nodes"))["nodes"].Should().Be($"{Released}:Alive",
                "the released node is still up and checking in once the migration has run under it");

            workingTree = await MixedVersionNodeProcess.Start(workingTreeAssembly, WorkingTree, SchedulerName, connectionString, TablePrefix, RunsTable, history: true, lockFreeRounds);
            workingTree.QuartzVersion.Should().NotStartWith(ReleasedQuartz, "the new node has to run the working tree");
            workingTree.Build.Should().Be("working-tree");
            await workingTree.Send("start");

            await ScheduleHalf(workingTree, first: 1, due);
            StateArrangement state = await ArrangeWorkingTreeState(workingTree, due);
            await Backdate(connectionString, "held", "overlap-misfired", TimeSpan.FromMinutes(10));
            string retryPolicyAsScheduled = (await ReadTriggerColumns(connectionString, "state", "retry-none")).RetryPolicy;
            bool? fixedDelayAsScheduled = (await ReadTriggerColumns(connectionString, "state", "fixed-delay")).FixedDelay;

            (DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1)).Should().BeBefore(due,
                "both nodes have to be up and every trigger written before the work is due, or part of the upgrade "
                + "happens before the cluster is mixed");

            string nodes = (await workingTree.Send("cluster-nodes"))["nodes"];
            nodes.Should().Be($"{Released}:Alive,{WorkingTree}:Alive", "each node sees the other checking in");

            // A few seconds into the drain, so that the released node has fired its overlap-policy trigger
            // and is writing fired rows for the one-offs while it works around the working tree's rows.
            await Task.Delay(Until(due + TimeSpan.FromSeconds(3)));
            AroundTheState around = await WorkAroundTheState(released, due);

            await RunTheSerialWindows(connectionString, released, workingTree, due);
            await WaitForTheWorkload(connectionString, due, around);

            // Settled in both directions before the reads: a duplicate that lost a race by a few hundred
            // milliseconds arrives after the last expected execution, not before it.
            await Task.Delay(TimeSpan.FromSeconds(3));

            Dictionary<string, string> triggerPause = await workingTree.Send("trigger-pause", ("name", "paused-with-reason"), ("group", "state"));
            Dictionary<string, string> groupPause = await workingTree.Send("trigger-group-pause", ("group", "held"));
            Dictionary<string, string> jobGroupPause = await workingTree.Send("job-group-pause", ("group", "held-jobs"));

            (RoundLocks releasedRounds, RoundLocks workingTreeRounds) = await RoundLocksBesideEachOther(released, workingTree, lockFreeRounds);

            // The rolling upgrade completes: the released node stops, then the working tree.
            await released.Stop(waitForJobs: true);
            long releasedLiveUntil = DateTimeOffset.UtcNow.UtcTicks;
            await workingTree.Stop(waitForJobs: true);

            List<Run> runs = await ReadRuns(connectionString);
            ColumnsAfter columns = await ReadColumns(connectionString);
            HistoryAfter history = await ReadHistory(connectionString);
            HistoryThroughTheApi throughTheApi = await ReadHistoryThroughTheApi(connectionString);

            TestContext.Out.WriteLine(Report(runs, due, elapsed.Elapsed, released, workingTree, await Durability(connectionString), releasedRounds, workingTreeRounds));

            using (new AssertionScope())
            {
                AssertExactlyOnce(runs);
                AssertNoOverlap(runs);
                AssertContinuationsSettled(runs, columns);
                AssertWorkingTreeColumnsSurvived(runs, columns, state, around, releasedLiveUntil);
                AssertRetryPolicyNoneSurvivesTheReleasedNode(runs, columns, retryPolicyAsScheduled, releasedLiveUntil);
                AssertTheReleasedNodeRunsAFixedDelayAtAFixedRate(runs, columns, fixedDelayAsScheduled, state, around, releasedLiveUntil);
                AssertTheWorkingTreeReadsThemBack(columns, triggerPause, groupPause, jobGroupPause);
                AssertProgressSurvived(runs);
                AssertEachRunIsInTheHistoryOnce(runs, history);
                AssertEachNodeWritesTheOutcomeItsJobsReport(runs, history);
                AssertTheWorkingTreeReadsTheHistoryBack(runs, history, throughTheApi);
                AssertNeitherNodeLocksForTheOther(releasedRounds, workingTreeRounds, lockFreeRounds);
            }
        }
        finally
        {
            string logs = string.Join(Environment.NewLine, new[] { released, workingTree }.Where(x => x is not null).Select(x => x.Describe()));
            TestContext.Out.WriteLine(logs);

            if (workingTree is not null)
            {
                await workingTree.DisposeAsync();
            }

            if (released is not null)
            {
                await released.DisposeAsync();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The workload
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// What each node schedules before the work is due, half of every workload: a thousand one-offs, twenty
    /// parent/continuation pairs, ten pinned each way across the two nodes, and its progress firings. The
    /// serial job's triggers come later, in <see cref="RunTheSerialWindows" />.
    /// </summary>
    private static async Task ScheduleHalf(MixedVersionNodeProcess node, int first, DateTimeOffset due)
    {
        int oneOffHalf = OneOffs / 2;
        int chainQuarter = ChainPairs / 4;

        await node.Send("schedule-one-offs", ("from", first * oneOffHalf), ("count", oneOffHalf), ("due", due));

        // Parents pinned to one node and their continuations to the other, both ways round, so a
        // completion on each version releases a continuation the other version has to run.
        int chain = first * 2 * chainQuarter;
        await node.Send("schedule-chain", ("from", chain), ("count", chainQuarter), ("due", due), ("parentNode", Released), ("childNode", WorkingTree));
        await node.Send("schedule-chain", ("from", chain + chainQuarter), ("count", chainQuarter), ("due", due), ("parentNode", WorkingTree), ("childNode", Released));

        // Progress firings pinned to this node, due in the middle of the drain. Only the working tree
        // reports; the released node's firings are the fired-trigger rows written beside them.
        for (int i = 0; i < ProgressFirings; i++)
        {
            await node.Send("schedule",
                ("name", $"progress-{node.InstanceId}-{i.ToString(CultureInfo.InvariantCulture)}"), ("group", "progress"),
                ("job", "progress"), ("type", "progress"), ("start", due + TimeSpan.FromSeconds(2)), ("intervalMs", 1000),
                ("repeat", 0), ("pin", node.InstanceId));
        }
    }

    /// <summary>
    /// The pause and overlap state, written by the working-tree node before the work is due.
    /// </summary>
    private static async Task<StateArrangement> ArrangeWorkingTreeState(MixedVersionNodeProcess workingTree, DateTimeOffset due)
    {
        // Fired by the released node every second, and updated by it.
        await workingTree.Send("schedule",
            ("name", "overlap-fired"), ("group", "state"), ("job", "state"), ("type", "state"),
            ("start", due), ("intervalMs", 1000), ("pin", Released), ("policy", nameof(OverlapPolicy.Skip)));

        // Refusing any retry policy it would inherit, and fired by the released node.
        await workingTree.Send("schedule",
            ("name", "retry-none"), ("group", "state"), ("job", "state"), ("type", "state"),
            ("start", due), ("intervalMs", 1000), ("pin", Released), ("retry", "none"));

        // Measuring its interval from completion, which the released node cannot do (#4140): it fires the
        // trigger, and updates it, without naming FIXED_DELAY.
        long fixedDelayFirst = long.Parse((await workingTree.Send("schedule",
            ("name", "fixed-delay"), ("group", "state"), ("job", "state"), ("type", "state"),
            ("start", due), ("intervalMs", FixedDelayIntervalMs), ("pin", Released), ("fromCompletion", true)))["first"], CultureInfo.InvariantCulture);

        // Paused with a reason, and updated by the released node while it is. Its start is past the run, so
        // nothing but the pause is keeping it from firing.
        await workingTree.Send("schedule",
            ("name", "paused-with-reason"), ("group", "state"), ("job", "state"), ("type", "state"),
            ("start", due + TimeSpan.FromMinutes(10)), ("intervalMs", 1000), ("policy", nameof(OverlapPolicy.BufferOne)));

        DateTimeOffset beforeTriggerPause = DateTimeOffset.UtcNow;
        await workingTree.Send("pause-trigger", ("name", "paused-with-reason"), ("group", "state"), ("reason", PauseReason), ("by", PausedBy));
        DateTimeOffset afterTriggerPause = DateTimeOffset.UtcNow;

        // Its neighbour, which the released node pauses and resumes.
        await workingTree.Send("schedule",
            ("name", "sibling"), ("group", "state"), ("job", "state"), ("type", "state"), ("start", due), ("intervalMs", 1000));

        // A trigger group and a job group paused with a reason, which the released node stores into and
        // pauses again.
        DateTimeOffset beforeGroupPauses = DateTimeOffset.UtcNow;
        await workingTree.Send("pause-trigger-group", ("group", "held"), ("reason", PauseReason), ("by", PausedBy));
        await workingTree.Send("pause-job-group", ("group", "held-jobs"), ("reason", PauseReason), ("by", PausedBy));
        DateTimeOffset afterGroupPauses = DateTimeOffset.UtcNow;

        // Born paused into that group, and pinned to the released node, which resumes it and then fires it.
        // The test makes it ten minutes overdue before the resume, so that the resume applies the
        // misfire policy and rewrites the row.
        await workingTree.Send("schedule",
            ("name", "overlap-misfired"), ("group", "held"), ("job", "state"), ("type", "state"),
            ("start", due), ("intervalMs", 1000), ("pin", Released), ("policy", nameof(OverlapPolicy.BufferOne)));

        return new StateArrangement(beforeTriggerPause, afterTriggerPause, beforeGroupPauses, afterGroupPauses, fixedDelayFirst);
    }

    /// <summary>
    /// The released node at work on and beside the rows that carry the working tree's pause and overlap columns.
    /// </summary>
    private static async Task<AroundTheState> WorkAroundTheState(MixedVersionNodeProcess released, DateTimeOffset due)
    {
        DateTimeOffset updated = DateTimeOffset.UtcNow;
        await released.Send("update-trigger", ("name", "overlap-fired"), ("group", "state"), ("description", UpdatedDescription), ("priority", UpdatedPriority));
        await released.Send("update-trigger", ("name", "paused-with-reason"), ("group", "state"), ("description", UpdatedDescription), ("priority", UpdatedPriority));
        await released.Send("update-trigger", ("name", "fixed-delay"), ("group", "state"), ("description", UpdatedDescription), ("priority", UpdatedPriority));

        (await released.Send("pause-trigger", ("name", "sibling"), ("group", "state")))["paused"].Should().Be("True");
        (await released.Send("resume-trigger", ("name", "sibling"), ("group", "state")))["resumed"].Should().Be("True");
        DateTimeOffset siblingResumed = DateTimeOffset.UtcNow;

        // Into the trigger group the working tree paused with a reason: born paused, by the released node's
        // reading of that row. Then the group is paused again, which finds it paused already.
        await released.Send("schedule",
            ("name", "born-paused"), ("group", "held"), ("job", "state"), ("type", "state"), ("start", due), ("intervalMs", 1000));
        await released.Send("pause-trigger-group", ("group", "held"));

        // Its overdue neighbour resumed: the misfire policy is applied and the row rewritten by the released node.
        DateTimeOffset resumed = DateTimeOffset.UtcNow;
        (await released.Send("resume-trigger", ("name", "overlap-misfired"), ("group", "held")))["resumed"].Should().Be("True");

        // A trigger of a job in the job group the working tree paused, and that group paused again.
        await released.Send("schedule",
            ("name", "parked"), ("group", "state"), ("job", "parked"), ("jobGroup", "held-jobs"), ("type", "state"),
            ("start", due), ("intervalMs", 1000));
        await released.Send("pause-job-group", ("group", "held-jobs"));

        return new AroundTheState(updated, siblingResumed, resumed);
    }

    /// <summary>
    /// Moves a paused trigger's next fire time into the past, which is what a trigger left paused for that
    /// long looks like to the node that resumes it.
    /// </summary>
    /// <remarks>
    /// The database is moved rather than waited on, as <see cref="ClusteredJobStoreTestBase" /> ages a
    /// check-in: the scheduler will not store an overdue repeating trigger — a start in the past is moved
    /// to now — and waiting out the minute-long misfire threshold would pay for a row update in wall time.
    /// </remarks>
    private static async Task Backdate(string connectionString, string group, string name, TimeSpan age)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(BackdatePausedTrigger, connection);
        command.Parameters.AddWithValue("age", age.Ticks);
        command.Parameters.AddWithValue("schedulerName", SchedulerName);
        command.Parameters.AddWithValue("group", group);
        command.Parameters.AddWithValue("name", name);

        (await command.ExecuteNonQueryAsync()).Should().Be(1,
            "{0}.{1} is stored paused, and the backdating is what makes the released node's resume a misfire", group, name);
    }

    /// <summary>
    /// The serial job's three windows, each opened once the one before it has fired its share: the released
    /// node's pinned triggers, the working tree's, then triggers either node may fire. <see cref="SerialFloor" />
    /// says why windows, and why each closes on a count.
    /// </summary>
    /// <remarks>
    /// The first opens once the one-offs have drained, not beside them, because of what a node does with a
    /// trigger of a <see cref="DisallowConcurrentExecutionAttribute" /> job that is running on the other
    /// node: it skips that one, and when its batch holds nothing else it waits before it looks again. With
    /// the serial job due during the drain, a released 4.2 node, which acquired one trigger at a time and
    /// waited out its whole idle wait, sat most of it out: 51 of 2,000 one-offs in one run.
    /// </remarks>
    private static async Task RunTheSerialWindows(string connectionString, MixedVersionNodeProcess released, MixedVersionNodeProcess workingTree, DateTimeOffset due)
    {
        await WaitForRuns(connectionString, due + Deadline,
            runs => runs.Where(x => x.TriggerGroup == "one-off").Select(x => x.TriggerName).Distinct(StringComparer.Ordinal).Count() >= OneOffs);

        foreach (MixedVersionNodeProcess node in (MixedVersionNodeProcess[]) [released, workingTree])
        {
            string pinned = $"serial-{node.InstanceId}-";
            for (int i = 0; i < SerialTriggersEach; i++)
            {
                await node.Send("schedule",
                    ("name", pinned + i.ToString(CultureInfo.InvariantCulture)), ("group", "serial"), ("job", "serial"),
                    ("type", "serial"), ("start", DateTimeOffset.UtcNow), ("intervalMs", SerialIntervalMs), ("pin", node.InstanceId));
            }

            await WaitForRuns(connectionString, DateTimeOffset.UtcNow + SerialWindowLimit,
                runs => runs.Count(x => x.JobName == "serial" && x.TriggerName.StartsWith(pinned, StringComparison.Ordinal)) >= SerialFloor);

            for (int i = 0; i < SerialTriggersEach; i++)
            {
                await node.Send("pause-trigger", ("name", pinned + i.ToString(CultureInfo.InvariantCulture)), ("group", "serial"));
            }
        }

        // The race: half of the triggers from each node, pinned to neither.
        DateTimeOffset raceStart = DateTimeOffset.UtcNow;
        await released.Send("schedule-serial", ("from", 0), ("count", SerialTriggersEach), ("start", raceStart), ("intervalMs", SerialIntervalMs));
        await workingTree.Send("schedule-serial", ("from", SerialTriggersEach), ("count", SerialTriggersEach), ("start", raceStart), ("intervalMs", SerialIntervalMs));

        await WaitForRuns(connectionString, DateTimeOffset.UtcNow + SerialWindowLimit,
            runs => runs.Count(IsSerialRace) >= SerialFloor);
    }

    /// <summary>A firing of the serial job by a trigger pinned to neither node.</summary>
    private static bool IsSerialRace(Run run) =>
        run.JobName == "serial" && !run.TriggerName.StartsWith("serial-node-", StringComparison.Ordinal);

    /// <summary>
    /// Reads the runs table every half second until <paramref name="done" /> holds or
    /// <paramref name="giveUp" /> passes. Not a failure of its own when it gives up: the invariants say
    /// exactly what is missing.
    /// </summary>
    private static async Task WaitForRuns(string connectionString, DateTimeOffset giveUp, Func<List<Run>, bool> done)
    {
        while (!done(await ReadRuns(connectionString)) && DateTimeOffset.UtcNow < giveUp)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    private static async Task WaitForTheWorkload(string connectionString, DateTimeOffset due, AroundTheState around)
    {
        DateTimeOffset giveUp = due + Deadline;
        while (DateTimeOffset.UtcNow < giveUp)
        {
            List<Run> runs = await ReadRuns(connectionString);

            bool done = runs.Where(x => x.TriggerGroup == "one-off").Select(x => x.TriggerName).Distinct(StringComparer.Ordinal).Count() >= OneOffs
                        && runs.Where(x => x.TriggerGroup == "chain").Select(x => x.TriggerName).Distinct(StringComparer.Ordinal).Count() >= 2 * ChainPairs
                        && runs.Count(x => x.TriggerGroup == "progress") >= 2 * ProgressFirings
                        && runs.Count(x => x.Trigger == "state.overlap-fired" && x.FiredUtc > around.Updated.UtcTicks) >= 3
                        && runs.Count(x => x.Trigger == "held.overlap-misfired") >= 3
                        && runs.Count(x => x.Trigger == "state.retry-none") >= 3
                        && runs.Count(x => x.Trigger == "state.fixed-delay" && x.FiredUtc > around.Updated.UtcTicks) >= 3
                        && runs.Any(x => x.Trigger == "state.sibling" && x.FiredUtc > around.SiblingResumed.UtcTicks);

            if (done)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Not a failure of its own: the invariants below say exactly what is missing.
        TestContext.Out.WriteLine($"The workload was still incomplete {Deadline.TotalSeconds:0} s after it was due.");
    }

    /// <summary>
    /// Both nodes' rounds by lock, counted from the stand-down: the work that needs the lock whoever runs it
    /// — the serial job, and the triggers whose overlap policy or fixed delay the fire decides — is paused,
    /// and concurrent-allowed work due every half second, pinned to each node, takes its place. Polled until
    /// each node that may has run a round without the lock, and until the working tree has run
    /// <see cref="RoundsAfterStandDown" /> more when it may not.
    /// </summary>
    /// <remarks>
    /// Counted from the stand-down because a node alone before its peer joined decided by no peer at all,
    /// and the drain's rounds lose claims to each other and escalate for that: a round counted here ran
    /// beside a peer that has checked in, with nothing in it that needs the lock.
    /// </remarks>
    private static async Task<(RoundLocks Released, RoundLocks WorkingTree)> RoundLocksBesideEachOther(
        MixedVersionNodeProcess released,
        MixedVersionNodeProcess workingTree,
        bool lockFreeRounds)
    {
        foreach (string group in (string[]) ["serial", "state", "held"])
        {
            await workingTree.Send("pause-trigger-group", ("group", group));
        }

        RoundLocks releasedBefore = RoundLocks.From(await released.Send("round-locks"));
        RoundLocks workingTreeBefore = RoundLocks.From(await workingTree.Send("round-locks"));

        foreach (string node in (string[]) [Released, WorkingTree])
        {
            await workingTree.Send("schedule",
                ("name", "beside-" + node), ("group", "lock-free"), ("job", "lock-free"), ("type", "state"),
                ("start", DateTimeOffset.UtcNow), ("intervalMs", 500), ("pin", node));
        }

        DateTimeOffset giveUp = DateTimeOffset.UtcNow + LockFreeWithin;
        while (true)
        {
            RoundLocks releasedSince = RoundLocks.From(await released.Send("round-locks")).Since(releasedBefore);
            RoundLocks workingTreeSince = RoundLocks.From(await workingTree.Send("round-locks")).Since(workingTreeBefore);

            bool settled = releasedSince.LockFree > 0
                           && (lockFreeRounds ? workingTreeSince.LockFree > 0 : workingTreeSince.Locked >= RoundsAfterStandDown);

            // The assertion says what is wrong when the wait gives up.
            if (settled || DateTimeOffset.UtcNow >= giveUp)
            {
                return (releasedSince, workingTreeSince);
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Invariants
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Every one-off ran exactly once, no firing ran twice, and no node was recovered from.
    /// </summary>
    private static void AssertExactlyOnce(List<Run> runs)
    {
        Dictionary<string, List<Run>> oneOffs = runs
            .Where(x => x.TriggerGroup == "one-off")
            .GroupBy(x => x.TriggerName, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        NoViolations(
            $"exactly once, one-offs: every one of the {OneOffs} one-offs runs on one node or the other, and {oneOffs.Count} did",
            Enumerable.Range(0, OneOffs)
                .Select(i => "one-off-" + i.ToString(CultureInfo.InvariantCulture))
                .Where(x => !oneOffs.ContainsKey(x))
                .Select(x => "one-off." + x + " never ran"));

        NoViolations(
            "exactly once, one-offs: two executions of one one-off is two nodes firing the same row",
            oneOffs.Where(x => x.Value.Count > 1).Select(x => $"ran {x.Value.Count} times: {Describe(x.Value)}"));

        NoViolations(
            "exactly once, fire instances: a fire instance id names one firing, so two executions sharing one ran it twice",
            runs.GroupBy(x => x.FireInstanceId, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => Describe(x.ToList())));

        NoViolations(
            "exactly once, recurring triggers: one scheduled fire time of one trigger ran more than once",
            runs.Where(x => x.ScheduledFireUtc is not null)
                .GroupBy(x => (x.Trigger, x.ScheduledFireUtc))
                .Where(x => x.Count() > 1)
                .Select(x => Describe(x.ToList())));

        NoViolations(
            "exactly once, recovery: no node stopped checking in while it had work in flight, so a recovering execution is a "
            + "firing replayed because one node declared the other dead",
            runs.Where(x => x.Recovering).Select(x => x.ToString()));

        Dictionary<string, int> share = oneOffs.Values
            .Select(x => x[0].Node)
            .GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

        // At least one each, so that the exactly-once claim is about a mixed cluster. How large a share each
        // node takes varies from run to run, and the report says.
        foreach (string node in (string[]) [Released, WorkingTree])
        {
            share.GetValueOrDefault(node).Should().BeGreaterThan(0,
                "both nodes run one-offs, or the exactly-once check was not made across versions (shares: {0})",
                string.Join(", ", share.Select(x => $"{x.Key}={x.Value}")));
        }
    }

    /// <summary>
    /// No two executions of the serial job overlapped, whichever node ran either.
    /// </summary>
    private static void AssertNoOverlap(List<Run> runs)
    {
        List<Run> serial = runs.Where(x => x.JobName == "serial").OrderBy(x => x.Started).ToList();

        List<string> overlaps = [];
        for (int i = 1; i < serial.Count; i++)
        {
            if (serial[i].Started < serial[i - 1].Ended)
            {
                overlaps.Add($"{serial[i]} started {Milliseconds(serial[i - 1].Ended - serial[i].Started)} ms before {serial[i - 1]} ended");
            }
        }

        NoViolations(
            "no overlap: [DisallowConcurrentExecution] holds across the cluster, and the store is the only thing the two "
            + "versions share to hold it with",
            overlaps);

        serial.Count.Should().BeGreaterThanOrEqualTo(3 * SerialFloor,
            "each of the serial job's three windows stays open until it has fired {0} times, so an overlap check over a handful "
            + "of executions proves little", SerialFloor);
        serial.Count(IsSerialRace).Should().BeGreaterThanOrEqualTo(SerialFloor,
            "the race window stays open until the triggers either node may fire have fired {0} times", SerialFloor);

        foreach (string node in (string[]) [Released, WorkingTree])
        {
            serial.Count(x => x.Node == node).Should().BeGreaterThan(0,
                "the serial job ran on both versions, or the no-overlap check did not cross them ({0} ran none)", node);

            // The working tree's window opens as the released node's last firings of the job end, which is
            // #3988: a node that waited out its idle wait behind them, as 4.3 does, fired none of it.
            int pinnedHere = serial.Count(x => x.Node == node && x.TriggerName.StartsWith($"serial-{node}-", StringComparison.Ordinal));
            pinnedHere.Should().BeGreaterThanOrEqualTo(SerialFloor,
                "{0}'s window stays open until its pinned triggers have fired {1} times, so fewer is a node that could not fire "
                + "them in {2} s, even with the other node's firings of the job over", node, SerialFloor, SerialWindowLimit.TotalSeconds);
        }
    }

    /// <summary>
    /// Every parent ran once on the node it is pinned to, and released a continuation that ran once, on
    /// the other node, after the parent had finished; nothing is left awaiting.
    /// </summary>
    private static void AssertContinuationsSettled(List<Run> runs, ColumnsAfter columns)
    {
        Dictionary<string, List<Run>> chain = runs
            .Where(x => x.TriggerGroup == "chain")
            .GroupBy(x => x.TriggerName, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        List<string> unsettled = [];
        for (int i = 0; i < ChainPairs; i++)
        {
            string index = i.ToString(CultureInfo.InvariantCulture);
            string parentNode = i % (ChainPairs / 2) < ChainPairs / 4 ? Released : WorkingTree;
            string childNode = parentNode == Released ? WorkingTree : Released;

            List<Run> parent = chain.GetValueOrDefault("parent-" + index, []);
            List<Run> continuation = chain.GetValueOrDefault("continuation-" + index, []);

            if (parent.Count != 1 || parent[0].Node != parentNode)
            {
                unsettled.Add($"chain.parent-{index}, pinned to {parentNode}, ran {parent.Count} time(s): {Describe(parent)}");
                continue;
            }

            if (continuation.Count != 1)
            {
                unsettled.Add($"chain.continuation-{index}, whose parent completed on {parentNode}, ran {continuation.Count} time(s): "
                              + $"{Describe(continuation)}; its row is {columns.LeftoverChain.GetValueOrDefault("continuation-" + index, "gone")}");
                continue;
            }

            if (continuation[0].Node != childNode || continuation[0].Started < parent[0].Ended)
            {
                unsettled.Add($"chain.continuation-{index} ran on {continuation[0].Node}, pinned to {childNode}, "
                              + $"{Milliseconds(continuation[0].Started - parent[0].Ended)} ms after its parent ended on {parentNode}");
            }
        }

        NoViolations(
            "settled continuations: a parent's completion settles its continuation inside its own transaction on either "
            + "version, so each continuation runs exactly once, on the node it is pinned to, after its parent",
            unsettled);

        NoViolations(
            "settled continuations: every parent and continuation is a one-off, so a row left in the chain group is one "
            + "no completion settled",
            columns.LeftoverChain.Select(x => $"chain.{x.Key} is still stored, {x.Value}"));
    }

    /// <summary>
    /// The pause and overlap columns are still what the working tree wrote after the released node fired, misfired,
    /// updated, paused and resumed the rows they sit on and beside.
    /// </summary>
    /// <param name="releasedLiveUntil">
    /// When the released node stopped, in UTC ticks: a trigger pinned to it is its alone until then, and the
    /// working tree's to take over after.
    /// </param>
    private static void AssertWorkingTreeColumnsSurvived(List<Run> runs, ColumnsAfter columns, StateArrangement state, AroundTheState around, long releasedLiveUntil)
    {
        TriggerColumns fired = columns.Triggers["state.overlap-fired"];
        runs.Count(x => x.Trigger == "state.overlap-fired" && x.Node == Released && x.FiredUtc > around.Updated.UtcTicks).Should().BeGreaterThanOrEqualTo(3,
            "state.overlap-fired is pinned to the released node, which fires it every second after updating it");
        NoViolations("state.overlap-fired is pinned to the released node while it is live",
            runs.Where(x => x.Trigger == "state.overlap-fired" && x.Node != Released && x.FiredUtc <= releasedLiveUntil).Select(x => x.ToString()));
        fired.OverlapPolicy.Should().Be((int) OverlapPolicy.Skip,
            "state.overlap-fired's OVERLAP_POLICY survives the released node firing the trigger and updating its details");
        fired.Description.Should().Be(UpdatedDescription, "the released node's update of state.overlap-fired took effect");
        fired.Priority.Should().Be(UpdatedPriority);

        TriggerColumns misfired = columns.Triggers["held.overlap-misfired"];
        List<Run> misfiredRuns = runs.Where(x => x.Trigger == "held.overlap-misfired").ToList();
        misfiredRuns.Count.Should().BeGreaterThan(0, "the released node resumed held.overlap-misfired, which it then fires");
        NoViolations("held.overlap-misfired is pinned to the released node while it is live",
            misfiredRuns.Where(x => x.Node != Released && x.FiredUtc <= releasedLiveUntil).Select(x => x.ToString()));
        NoViolations(
            "held.overlap-misfired: the released node's resume applied the misfire policy, which moves the trigger past its ten "
            + "overdue minutes rather than firing them",
            misfiredRuns.Where(x => !(x.ScheduledFireUtc >= around.Resumed.UtcTicks - TimeSpan.TicksPerSecond)).Select(x => x.ToString()));
        misfired.OverlapPolicy.Should().Be((int) OverlapPolicy.BufferOne,
            "held.overlap-misfired's OVERLAP_POLICY survives the released node's resume, its misfire rewrite of the row and its firings");

        TriggerColumns paused = columns.Triggers["state.paused-with-reason"];
        paused.State.Should().Be("PAUSED", "nothing resumed state.paused-with-reason");
        paused.OverlapPolicy.Should().Be((int) OverlapPolicy.BufferOne);
        paused.PauseReason.Should().Be(PauseReason,
            "state.paused-with-reason's PAUSE_REASON survives the released node updating its details and pausing and resuming its sibling");
        paused.PausedBy.Should().Be(PausedBy);
        paused.PausedAt.Should().BeInRange(state.BeforeTriggerPause.UtcTicks - TimeSpan.TicksPerSecond, state.AfterTriggerPause.UtcTicks + TimeSpan.TicksPerSecond,
            "PAUSED_AT is when the working tree paused state.paused-with-reason");
        paused.Description.Should().Be(UpdatedDescription, "the released node's update of state.paused-with-reason took effect");
        NoViolations("state.paused-with-reason stayed paused", runs.Where(x => x.Trigger == "state.paused-with-reason").Select(x => x.ToString()));

        runs.Count(x => x.Trigger == "state.sibling" && x.FiredUtc > around.SiblingResumed.UtcTicks).Should().BeGreaterThan(0,
            "state.sibling, which the released node paused and resumed, kept firing");

        columns.TriggerGroupPause.Should().Be(new PauseColumns(PauseReason, PausedBy, columns.TriggerGroupPause?.PausedAt),
            "trigger group held's pause reason survives the released node storing into the group and pausing it again");
        columns.TriggerGroupPause?.PausedAt.Should().BeInRange(state.BeforeGroupPauses.UtcTicks - TimeSpan.TicksPerSecond, state.AfterGroupPauses.UtcTicks + TimeSpan.TicksPerSecond);
        columns.Triggers["held.born-paused"].State.Should().Be("PAUSED", "the released node read the trigger group row the working tree wrote");
        NoViolations("held.born-paused was born paused", runs.Where(x => x.Trigger == "held.born-paused").Select(x => x.ToString()));

        columns.JobGroupPause.Should().Be(new PauseColumns(PauseReason, PausedBy, columns.JobGroupPause?.PausedAt),
            "job group held-jobs's pause reason survives the released node storing a trigger for a job in it and pausing it again");
        columns.JobGroupPause?.PausedAt.Should().BeInRange(state.BeforeGroupPauses.UtcTicks - TimeSpan.TicksPerSecond, state.AfterGroupPauses.UtcTicks + TimeSpan.TicksPerSecond);
        columns.Triggers["state.parked"].State.Should().Be("PAUSED", "the released node read the job group row the working tree wrote");
        NoViolations("state.parked was born paused", runs.Where(x => x.Trigger == "state.parked").Select(x => x.ToString()));
    }

    /// <summary>
    /// A trigger the working tree gave <c>RetryPolicy.None</c> is stored as <c>none</c>, which a node from 4.4
    /// on parses: the released node fires the trigger and writes the column back as it read it.
    /// </summary>
    private static void AssertRetryPolicyNoneSurvivesTheReleasedNode(List<Run> runs, ColumnsAfter columns, string asScheduled, long releasedLiveUntil)
    {
        asScheduled.Should().Be("none", "the working tree stores RetryPolicy.None as a value of its own, distinct from an empty column");

        runs.Count(x => x.Trigger == "state.retry-none" && x.Node == Released).Should().BeGreaterThanOrEqualTo(3,
            "the released node reads RetryPolicy.None as 4.4 taught it to, and fires the trigger like any other");
        NoViolations("state.retry-none is pinned to the released node while it is live",
            runs.Where(x => x.Trigger == "state.retry-none" && x.Node != Released && x.FiredUtc <= releasedLiveUntil).Select(x => x.ToString()));

        columns.Triggers["state.retry-none"].RetryPolicy.Should().Be("none",
            "the released node writes the trigger it fired back with the policy it read");
    }

    /// <summary>
    /// A trigger the working tree made measure its interval from completion is one the released node fires at
    /// a fixed rate, and leaves <c>FIXED_DELAY</c> as the working tree wrote it (#4140): it never names the
    /// column.
    /// </summary>
    private static void AssertTheReleasedNodeRunsAFixedDelayAtAFixedRate(
        List<Run> runs,
        ColumnsAfter columns,
        bool? asScheduled,
        StateArrangement state,
        AroundTheState around,
        long releasedLiveUntil)
    {
        asScheduled.Should().BeTrue("the working tree stores a trigger that measures from completion with FIXED_DELAY set");

        List<Run> released = runs.Where(x => x.Trigger == "state.fixed-delay" && x.Node == Released).ToList();
        released.Count(x => x.FiredUtc > around.Updated.UtcTicks).Should().BeGreaterThanOrEqualTo(3,
            "state.fixed-delay is pinned to the released node, which fires it every second after updating it");
        NoViolations("state.fixed-delay is pinned to the released node while it is live",
            runs.Where(x => x.Trigger == "state.fixed-delay" && x.Node != Released && x.FiredUtc <= releasedLiveUntil).Select(x => x.ToString()));

        // A fixed delay would put each run an interval after the last one's completion, which is off this grid
        // by however long the run and the firing took.
        long interval = FixedDelayIntervalMs * TimeSpan.TicksPerMillisecond;
        NoViolations(
            "state.fixed-delay: the released node fires a trigger that measures from completion at a fixed rate, on the grid "
            + "of its start time and interval",
            released.Where(x => x.ScheduledFireUtc is not { } scheduled || scheduled < state.FixedDelayFirst || (scheduled - state.FixedDelayFirst) % interval != 0)
                .Select(x => x.ToString()));

        TriggerColumns columnsAfter = columns.Triggers["state.fixed-delay"];
        columnsAfter.FixedDelay.Should().BeTrue(
            "the released node fires the trigger and updates its details without naming FIXED_DELAY, so it leaves the column as the working tree wrote it");
        columnsAfter.Description.Should().Be(UpdatedDescription, "the released node's update of state.fixed-delay took effect");
    }

    /// <summary>
    /// What survived is also what the working tree's own API reads back.
    /// </summary>
    private static void AssertTheWorkingTreeReadsThemBack(
        ColumnsAfter columns,
        Dictionary<string, string> triggerPause,
        Dictionary<string, string> groupPause,
        Dictionary<string, string> jobGroupPause)
    {
        foreach ((string what, Dictionary<string, string> pause, long? pausedAt) in (IEnumerable<(string, Dictionary<string, string>, long?)>)
                 [
                     ("trigger state.paused-with-reason", triggerPause, columns.Triggers["state.paused-with-reason"].PausedAt),
                     ("trigger group held", groupPause, columns.TriggerGroupPause?.PausedAt),
                     ("job group held-jobs", jobGroupPause, columns.JobGroupPause?.PausedAt)
                 ])
        {
            pause.GetValueOrDefault("paused").Should().Be("True", $"the working tree still reads the {what} as paused");
            pause.GetValueOrDefault("reason").Should().Be(PauseReason, $"the working tree reads the {what}'s reason back");
            pause.GetValueOrDefault("by").Should().Be(PausedBy);
            pause.GetValueOrDefault("at").Should().Be(pausedAt?.ToString(CultureInfo.InvariantCulture),
                $"the working tree reads the {what}'s PAUSED_AT as the column holds it");
        }
    }

    /// <summary>
    /// Progress a working-tree job reported is still on its fired-trigger row after holding there through
    /// the released node's fired-row writes, its firings of the same job among them.
    /// </summary>
    private static void AssertProgressSurvived(List<Run> runs)
    {
        List<Run> progress = runs.Where(x => x.TriggerGroup == "progress").ToList();

        progress.Count(x => x.Node == WorkingTree).Should().Be(ProgressFirings, "the working tree ran each of its progress firings once");
        progress.Count(x => x.Node == Released).Should().Be(ProgressFirings, "the released node ran each of its progress firings once");

        NoViolations(
            "progress: what a working-tree job reported is what its fired-trigger row still holds after the released node's writes",
            progress
                .Where(x => x.Node == WorkingTree && (x.Progress != 57 || x.ProgressMessage != "reported by " + x.FireInstanceId))
                .Select(x => $"{x} read back {x.Progress?.ToString(CultureInfo.InvariantCulture) ?? "no progress"}, '{x.ProgressMessage}'"));
    }

    /// <summary>
    /// Every execution either node ran is in the shared history once, under the node that ran it.
    /// </summary>
    private static void AssertEachRunIsInTheHistoryOnce(List<Run> runs, HistoryAfter history)
    {
        history.Rows.Count.Should().BeGreaterThan(OneOffs, "every one-off ran, and both nodes keep their history in the database");

        foreach (string node in (string[]) [Released, WorkingTree])
        {
            Dictionary<string, int> ran = CountByJob(runs.Where(x => x.Node == node).Select(x => x.Job));
            Dictionary<string, int> recorded = CountByJob(history.Rows.Where(x => x.InstanceName == node).Select(x => x.Job));

            NoViolations(
                $"history, {node}: each execution is recorded once, under the node that ran it",
                ran.Keys.Union(recorded.Keys, StringComparer.Ordinal)
                    .Where(job => ran.GetValueOrDefault(job) != recorded.GetValueOrDefault(job))
                    .Select(job => $"{job} ran {ran.GetValueOrDefault(job)} time(s) on {node} and has {recorded.GetValueOrDefault(job)} row(s)"));
        }

        NoViolations(
            "history: every row names a node of this cluster",
            history.Rows.Where(x => x.InstanceName != Released && x.InstanceName != WorkingTree).Select(x => x.ToString()));
    }

    /// <summary>
    /// Both nodes write the 4.4 outcome columns and keep <c>QRTZ_JOB_STATUS</c>. The released node's jobs
    /// report nothing, so its rows carry the result and the firing and no report; the working tree's carry
    /// what its jobs reported; and each job's status counts both nodes' runs.
    /// </summary>
    private static void AssertEachNodeWritesTheOutcomeItsJobsReport(List<Run> runs, HistoryAfter history)
    {
        NoViolations(
            "history, released node: a job that reports nothing is recorded succeeded, with its firing and no summary or metrics",
            history.Rows
                .Where(x => x.InstanceName == Released
                            && (x.Result != (int) JobRunResult.Succeeded || x.Summary is not null || x.Metrics is not null || x.Manual != false || x.FireInstanceId is null))
                .Select(x => x.ToString()));

        NoViolations(
            "history, working tree: each row carries its result, the summary and metrics its job reported, MANUAL and its firing",
            history.Rows
                .Where(x => x.InstanceName == WorkingTree
                            && (x.Result != (int) JobRunResult.Succeeded
                                || x.Summary != $"{WorkingTree} ran {x.FireInstanceId}"
                                || x.Metrics?.Contains(WorkingTree, StringComparison.Ordinal) != true
                                || x.Manual != false
                                || x.FireInstanceId is null))
                .Select(x => x.ToString()));

        foreach (string node in (string[]) [Released, WorkingTree])
        {
            history.Rows.Where(x => x.InstanceName == node).Select(x => x.FireInstanceId).Should().BeEquivalentTo(
                runs.Where(x => x.Node == node).Select(x => x.FireInstanceId),
                "FIRE_INSTANCE_ID links each row {0} wrote to the firing that ran", node);
        }

        Dictionary<string, int> ranAnywhere = CountByJob(runs.Select(x => x.Job));

        history.Statuses.Keys.Should().BeEquivalentTo(ranAnywhere.Keys,
            "both nodes keep QRTZ_JOB_STATUS, so every job either ran has a status");

        NoViolations(
            "job status: RUN_COUNT counts the job's runs on both nodes, and LAST_INSTANCE_NAME names one of them",
            history.Statuses
                .Where(x => x.Value.RunCount != ranAnywhere.GetValueOrDefault(x.Key) || (x.Value.LastInstanceName != Released && x.Value.LastInstanceName != WorkingTree))
                .Select(x => $"{x.Key}: RUN_COUNT {x.Value.RunCount}, LAST_INSTANCE_NAME {x.Value.LastInstanceName}; "
                             + $"it ran {ranAnywhere.GetValueOrDefault(x.Key)} time(s)"));
    }

    /// <summary>
    /// The working tree's own store reads both nodes' rows back with the result each carries, and the
    /// status both keep.
    /// </summary>
    private static void AssertTheWorkingTreeReadsTheHistoryBack(List<Run> runs, HistoryAfter history, HistoryThroughTheApi api)
    {
        api.Executions.Should().HaveCount(history.Rows.Count, "the store reads every row the two nodes wrote");

        NoViolations(
            "read back, released node: succeeded, with its firing and no summary",
            api.Executions
                .Where(x => x.SchedulerInstanceId == Released
                            && (x.Result != JobRunResult.Succeeded || x.EffectiveResult != JobRunResult.Succeeded || x.FireInstanceId is null || x.Summary is not null))
                .Select(x => $"{x.JobGroup}.{x.JobName} on {x.SchedulerInstanceId}: result {x.Result}, effective {x.EffectiveResult}, summary '{x.Summary}'"));

        NoViolations(
            "read back, working tree: the result and summary its job reported",
            api.Executions
                .Where(x => x.SchedulerInstanceId == WorkingTree
                            && (x.Result != JobRunResult.Succeeded || x.Summary != $"{WorkingTree} ran {x.FireInstanceId}"))
                .Select(x => $"{x.JobGroup}.{x.JobName} on {x.SchedulerInstanceId}: result {x.Result}, summary '{x.Summary}'"));

        api.SucceededCount.Should().Be(api.Executions.Count, "every run on either node succeeded");

        api.OneOffStatus.Should().NotBeNull("both nodes ran one-offs, and the store keeps their job's status");
        api.OneOffStatus.RunCount.Should().Be(runs.Count(x => x.TriggerGroup == "one-off"),
            "the one-off job's status counts both nodes' runs");
        api.OneOffStatus.LastSchedulerInstanceId.Should().BeOneOf(Released, WorkingTree);
    }

    /// <summary>
    /// Neither node keeps its rounds locked for the other (#4067): each reads the other's version stamp as
    /// 4.6 or later, so beside it a round of concurrent-allowed work takes no lock — unless the working
    /// tree's <c>LockFreeFiringRounds</c> is off, in which case none of its rounds does, beside a released
    /// node whose rounds still go without.
    /// </summary>
    private static void AssertNeitherNodeLocksForTheOther(RoundLocks released, RoundLocks workingTree, bool lockFreeRounds)
    {
        released.LockFree.Should().BeGreaterThan(0,
            "the released node reads the working tree's stamp as a node whose writes a lock-free round can run beside "
            + "(its rounds since the stand-down: {0})", released);
        released.Untagged.Should().Be(0, "a 4.6 node tags every round with the lock it ran under");
        workingTree.Untagged.Should().Be(0, "the working tree tags every round with the lock it ran under");

        if (lockFreeRounds)
        {
            workingTree.LockFree.Should().BeGreaterThan(0,
                "the working tree reads the released node's 4.6 stamp as a node whose writes a lock-free round can run beside "
                + "(its rounds since the stand-down: {0})", workingTree);
        }
        else
        {
            workingTree.LockFree.Should().Be(0,
                "with LockFreeFiringRounds off every round is locked, whatever the peers (its rounds since the stand-down: {0})", workingTree);
            workingTree.Locked.Should().BeGreaterThanOrEqualTo(RoundsAfterStandDown, "the working tree kept running rounds");
            workingTree.Reasons.Should().MatchRegex("^option_off:[0-9]+$", "the option is the only reason any of its rounds gave");
        }
    }

    /// <summary>
    /// What a node's <c>round-locks</c> command answered: its firing rounds by the lock each said it ran
    /// under, and the locked ones by reason.
    /// </summary>
    private sealed record RoundLocks(int LockFree, int Locked, int Untagged, string Reasons)
    {
        public static RoundLocks From(Dictionary<string, string> answer) => new(
            int.Parse(answer["lockfree"], CultureInfo.InvariantCulture),
            int.Parse(answer["locked"], CultureInfo.InvariantCulture),
            int.Parse(answer["untagged"], CultureInfo.InvariantCulture),
            answer.GetValueOrDefault("reasons", ""));

        /// <summary>
        /// The rounds run since <paramref name="before" /> was read; the reasons stay the whole run's.
        /// </summary>
        public RoundLocks Since(RoundLocks before) => new(LockFree - before.LockFree, Locked - before.Locked, Untagged - before.Untagged, Reasons);

        public override string ToString() => $"none={LockFree}, TRIGGER_ACCESS={Locked}, untagged={Untagged}; reasons over the run: {Reasons}";
    }

    private static Dictionary<string, int> CountByJob(IEnumerable<string> jobs) => jobs
        .GroupBy(x => x, StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

    /// <summary>
    /// Fails, inside the caller's assertion scope, with every violation an invariant found.
    /// </summary>
    private static void NoViolations(string invariant, IEnumerable<string> violations)
    {
        const int shown = 50;
        List<string> found = violations.ToList();

        string report = found.Count == 0
            ? ""
            : $"{found.Count} violation(s):{Environment.NewLine}" + string.Join(Environment.NewLine, found.Take(shown))
              + (found.Count > shown ? $"{Environment.NewLine}and {found.Count - shown} more" : "");

        report.Should().BeEmpty(invariant);
    }

    // ---------------------------------------------------------------------------------------------
    // The database
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The released schema's fresh install, every PostgreSQL migration through the released node's minor
    /// over it, and the runs table: the database the released node starts on.
    /// </summary>
    private static async Task PrepareDatabase(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        // Whatever an earlier run left under this fixture's prefix, which is its own and nobody else's.
        List<string> leftovers = [];
        await using (NpgsqlCommand select = new(SelectLeftoverTables, connection))
        await using (NpgsqlDataReader reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                leftovers.Add(reader.GetString(0));
            }
        }

        foreach (string table in leftovers)
        {
            await Execute(connection, "DROP TABLE IF EXISTS \"" + table + "\" CASCADE");
        }

        await Execute(connection, MigrationScriptTest.BaselineScript(ReleasedSchema, "postgres", TablePrefix));

        // What an operator on the released minor has run, the optional history ones included: both nodes keep
        // their history here.
        Version releasedMinor = ReleasedMinor();
        foreach ((string version, string name) in MigrationChains.Since(ReleasedSchema).Where(x => Version.Parse(x.Version) <= releasedMinor))
        {
            await Execute(connection, MigrationScriptTest.MigrationScript(version, name, "postgres", TablePrefix));
        }

        await Execute(connection, CreateRunsTable);
    }

    /// <summary>
    /// Every PostgreSQL migration since the released node's minor, run while that node is up: what the
    /// migration guide tells an operator to do before rolling the nodes.
    /// </summary>
    private static async Task MigrateWhileReleasedIsUp(string connectionString)
    {
        (string Version, string Name)[] migrations = MigrationChains.Since(ReleasedMinor().ToString());
        migrations.Should().NotBeEmpty("the gate is the migration since the released minor, so there has to be one");

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        foreach ((string version, string name) in migrations)
        {
            await Execute(connection, MigrationScriptTest.MigrationScript(version, name, "postgres", TablePrefix));
        }
    }

    /// <summary>The released node's minor, which names its folder of migrations: <c>4.6</c> for 4.6.0.</summary>
    private static Version ReleasedMinor()
    {
        Version released = Version.Parse(ReleasedQuartz);
        return new Version(released.Major, released.Minor);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<Run>> ReadRuns(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(SelectRuns, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        List<Run> runs = [];
        while (await reader.ReadAsync())
        {
            runs.Add(new Run(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetBoolean(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
        }

        return runs;
    }

    /// <summary>
    /// Every history row and every job status row the two nodes wrote, read straight from the columns.
    /// </summary>
    private static async Task<HistoryAfter> ReadHistory(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        List<HistoryRow> rows = [];
        await using (NpgsqlCommand command = new(SelectHistory, connection))
        {
            command.Parameters.AddWithValue("schedulerName", SchedulerName);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new HistoryRow(
                    reader.GetString(0),
                    reader.GetString(1) + "." + reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        Dictionary<string, (long RunCount, string LastInstanceName)> statuses = new(StringComparer.Ordinal);
        await using (NpgsqlCommand command = new(SelectJobStatus, connection))
        {
            command.Parameters.AddWithValue("schedulerName", SchedulerName);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                statuses[reader.GetString(0) + "." + reader.GetString(1)] = (reader.GetInt64(2), reader.GetString(3));
            }
        }

        return new HistoryAfter(rows, statuses);
    }

    /// <summary>
    /// The same history as the working tree's own store reads it: every execution, the executions a
    /// filter on <see cref="JobRunResult.Succeeded" /> selects, and the one-off job's status.
    /// </summary>
    /// <remarks>
    /// The store is built in this process, over the same database, without a scheduler: it is the reader a
    /// dashboard or the HTTP API resolves.
    /// </remarks>
    private static async Task<HistoryThroughTheApi> ReadHistoryThroughTheApi(string connectionString)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(o => o.InstanceName = SchedulerName);
            q.UsePersistentStore(store =>
            {
                store.ConfigureStore(o => o.TablePrefix = TablePrefix);
                store.UsePostgres(NpgsqlFactory.Instance, connectionString);
                store.UseSystemTextJsonSerializer();
                store.UseExecutionHistory();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IExecutionHistoryStore history = provider.GetRequiredService<IExecutionHistoryStore>();

        PagedResult<ExecutionHistoryEntry> all = await history.QueryExecutions(
            new ExecutionHistoryQuery { SchedulerName = SchedulerName, Take = PagedQuery.All });
        PagedResult<ExecutionHistoryEntry> succeeded = await history.QueryExecutions(
            new ExecutionHistoryQuery { SchedulerName = SchedulerName, Take = PagedQuery.All, Results = [JobRunResult.Succeeded] });
        JobRunStatus oneOff = await history.GetJobRunStatus(SchedulerName, new JobKey("one-off", "gate"));

        return new HistoryThroughTheApi([.. all.Items], succeeded.Items.Count, oneOff);
    }

    private static async Task<TriggerColumns> ReadTriggerColumns(string connectionString, string group, string name)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        return await ReadTriggerColumns(connection, group, name);
    }

    private static async Task<TriggerColumns> ReadTriggerColumns(NpgsqlConnection connection, string group, string name)
    {
        await using NpgsqlCommand command = new(SelectTriggerColumns, connection);
        command.Parameters.AddWithValue("schedulerName", SchedulerName);
        command.Parameters.AddWithValue("group", group);
        command.Parameters.AddWithValue("name", name);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            // Asserted on with everything else rather than here, so one gone row does not hide the rest.
            return new TriggerColumns("<no row>", null, null, null, null, null, null, null, null);
        }

        return new TriggerColumns(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetBoolean(8));
    }

    private static async Task<ColumnsAfter> ReadColumns(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        Dictionary<string, TriggerColumns> triggers = new(StringComparer.Ordinal);
        foreach ((string group, string name) in (IEnumerable<(string, string)>)
                 [
                     ("state", "overlap-fired"), ("state", "paused-with-reason"), ("held", "overlap-misfired"), ("held", "born-paused"),
                     ("state", "parked"), ("state", "retry-none"), ("state", "fixed-delay")
                 ])
        {
            triggers[group + "." + name] = await ReadTriggerColumns(connection, group, name);
        }

        Dictionary<string, string> leftoverChain = new(StringComparer.Ordinal);
        await using (NpgsqlCommand command = new(SelectLeftoverChain, connection))
        {
            command.Parameters.AddWithValue("schedulerName", SchedulerName);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                leftoverChain[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return new ColumnsAfter(
            triggers,
            await ReadPause(connection, SelectTriggerGroupPause, "held"),
            await ReadPause(connection, SelectJobGroupPause, "held-jobs"),
            leftoverChain);
    }

    private static async Task<PauseColumns> ReadPause(NpgsqlConnection connection, string sql, string group)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("schedulerName", SchedulerName);
        command.Parameters.AddWithValue("group", group);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PauseColumns(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    private static async Task<string> Durability(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand fsync = new("SHOW fsync", connection);
        await using NpgsqlCommand synchronousCommit = new("SHOW synchronous_commit", connection);
        return $"fsync = {await fsync.ExecuteScalarAsync()}, synchronous_commit = {await synchronousCommit.ExecuteScalarAsync()}";
    }

    // ---------------------------------------------------------------------------------------------
    // Reporting
    // ---------------------------------------------------------------------------------------------

    private static string Report(
        List<Run> runs,
        DateTimeOffset due,
        TimeSpan elapsed,
        MixedVersionNodeProcess released,
        MixedVersionNodeProcess workingTree,
        string durability,
        RoundLocks releasedRounds,
        RoundLocks workingTreeRounds)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Mixed-version cluster on PostgreSQL ({durability}): {Released} = Quartz {released.QuartzVersion}, {WorkingTree} = Quartz {workingTree.QuartzVersion}.");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Rounds by lock since the stand-down: {Released} {releasedRounds}; {WorkingTree} {workingTreeRounds}.");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"| Workload | {Released} | {WorkingTree} | Total |");
        report.AppendLine("|---|---:|---:|---:|");

        foreach ((string name, Func<Run, bool> filter) in (IEnumerable<(string, Func<Run, bool>)>)
                 [
                     ("one-offs", x => x.TriggerGroup == "one-off"),
                     ("serial job", x => x.JobName == "serial"),
                     ($"serial job, pinned to {Released}", x => x.TriggerName.StartsWith($"serial-{Released}-", StringComparison.Ordinal)),
                     ($"serial job, pinned to {WorkingTree}", x => x.TriggerName.StartsWith($"serial-{WorkingTree}-", StringComparison.Ordinal)),
                     ("serial job, the race", IsSerialRace),
                     ("parents", x => x.JobName == "parent"),
                     ("continuations", x => x.JobName == "continuation"),
                     ("progress", x => x.TriggerGroup == "progress"),
                     ("state.overlap-fired (pinned to the released node)", x => x.Trigger == "state.overlap-fired"),
                     ("held.overlap-misfired (pinned to the released node)", x => x.Trigger == "held.overlap-misfired"),
                     ("state.fixed-delay (pinned to the released node)", x => x.Trigger == "state.fixed-delay"),
                     ("state.sibling", x => x.Trigger == "state.sibling"),
                     ("lock-free (after the stand-down, one pinned to each node)", x => x.TriggerGroup == "lock-free")
                 ])
        {
            List<Run> selected = runs.Where(filter).ToList();
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {selected.Count(x => x.Node == Released)} | {selected.Count(x => x.Node == WorkingTree)} | {selected.Count} |");
        }

        List<Run> oneOffs = runs.Where(x => x.TriggerGroup == "one-off").OrderBy(x => x.StartedUtc).ToList();
        if (oneOffs.Count > 0)
        {
            TimeSpan drain = TimeSpan.FromTicks(oneOffs[^1].StartedUtc - due.UtcTicks);
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture,
                $"One-offs drained {drain.TotalSeconds:F1} s after they were due ({oneOffs.Count / Math.Max(drain.TotalSeconds, 0.001):F0}/s); the whole run took {elapsed.TotalSeconds:F0} s.");

            double releasedShare = 100.0 * oneOffs.Count(x => x.Node == Released) / oneOffs.Count;
            double workingTreeShare = 100.0 * oneOffs.Count(x => x.Node == WorkingTree) / oneOffs.Count;
            string starved = releasedShare < 10
                ? $" — {Released} starved: it idled after losing its acquisitions to {WorkingTree}."
                : ".";
            report.AppendLine(CultureInfo.InvariantCulture,
                $"One-off share: {Released} {releasedShare:F1} %, {WorkingTree} {workingTreeShare:F1} %{starved}");
        }

        return report.ToString();
    }

    private static string Describe(List<Run> runs) => runs.Count == 0 ? "<none>" : string.Join("; ", runs.Select(x => x.ToString()));

    private static string Milliseconds(long stopwatchTicks) =>
        (stopwatchTicks * 1000.0 / Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture);

    private static TimeSpan Until(DateTimeOffset instant)
    {
        TimeSpan remaining = instant - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// One build of the node, where <c>Compile</c> leaves it.
    /// </summary>
    /// <param name="releasedQuartz">The released Quartz it was built against, or null for the working tree.</param>
    private static string NodeAssembly(string releasedQuartz)
    {
        const string project = "Quartz.Tests.Integration.MixedVersionNode";

        DirectoryInfo configuration = new(AppContext.BaseDirectory);
        DirectoryInfo bin = configuration.Parent?.Parent
            ?? throw new InvalidOperationException($"Cannot locate the artifacts directory from {AppContext.BaseDirectory}.");

        string folder = releasedQuartz is null ? project : project + "." + releasedQuartz;
        string path = Path.Combine(bin.FullName, folder, configuration.Name, project + ".dll");

        File.Exists(path).Should().BeTrue(
            $"the gate runs both nodes out of process; 'dotnet build src/{project}/{project}.csproj -c {configuration.Name}' "
            + "builds the working-tree node and the released one beside it");

        return path;
    }

    /// <summary>One execution, as the job that ran wrote it.</summary>
    private sealed record Run(
        string TriggerGroup,
        string TriggerName,
        string JobGroup,
        string JobName,
        string FireInstanceId,
        string Node,
        long? ScheduledFireUtc,
        long FiredUtc,
        long StartedUtc,
        long Started,
        long Ended,
        bool Recovering,
        int? Progress,
        string ProgressMessage)
    {
        public string Trigger => TriggerGroup + "." + TriggerName;

        public string Job => JobGroup + "." + JobName;

        public override string ToString()
        {
            string scheduled = ScheduledFireUtc is { } ticks
                ? new DateTimeOffset(ticks, TimeSpan.Zero).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                : "-";
            return $"{Trigger} on {Node} (fire instance {FireInstanceId}, scheduled {scheduled})";
        }
    }

    private sealed record TriggerColumns(
        string State,
        int? OverlapPolicy,
        string PauseReason,
        string PausedBy,
        long? PausedAt,
        string Description,
        int? Priority,
        string RetryPolicy,
        bool? FixedDelay);

    private sealed record PauseColumns(string PauseReason, string PausedBy, long? PausedAt);

    private sealed record ColumnsAfter(
        Dictionary<string, TriggerColumns> Triggers,
        PauseColumns TriggerGroupPause,
        PauseColumns JobGroupPause,
        Dictionary<string, string> LeftoverChain);

    private sealed record StateArrangement(
        DateTimeOffset BeforeTriggerPause,
        DateTimeOffset AfterTriggerPause,
        DateTimeOffset BeforeGroupPauses,
        DateTimeOffset AfterGroupPauses,
        long FixedDelayFirst);

    private sealed record AroundTheState(DateTimeOffset Updated, DateTimeOffset SiblingResumed, DateTimeOffset Resumed);

    /// <summary>One <c>QRTZ_EXECUTION_HISTORY</c> row, as the columns hold it.</summary>
    private sealed record HistoryRow(
        string InstanceName,
        string Job,
        bool Succeeded,
        int? Result,
        string Summary,
        string Metrics,
        bool? Manual,
        string FireInstanceId);

    /// <summary>The history rows, and each job's <c>QRTZ_JOB_STATUS</c> run count and last node.</summary>
    private sealed record HistoryAfter(
        List<HistoryRow> Rows,
        Dictionary<string, (long RunCount, string LastInstanceName)> Statuses);

    /// <summary>What the working tree's history store answers.</summary>
    private sealed record HistoryThroughTheApi(
        List<ExecutionHistoryEntry> Executions,
        int SucceededCount,
        JobRunStatus OneOffStatus);
}
