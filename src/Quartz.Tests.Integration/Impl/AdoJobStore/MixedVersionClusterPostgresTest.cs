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

using Npgsql;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A released 4.2 node and a working-tree node share one PostgreSQL cluster through a rolling upgrade,
/// and nothing is lost, doubled or overwritten.
/// </summary>
/// <remarks>
/// <para>
/// <b>The upgrade.</b> The schema is 4.2.0's fresh install with every
/// <c>database/migrations/4.3/*_postgres.sql</c> applied over it, which is what an operator's database
/// looks like after the 4.3 migration. The released node — Quartz 4.2.2 from nuget.org — starts first
/// and schedules half of the work; the working-tree node joins and schedules the other half. Both run
/// with their shipped defaults, so the working-tree node batches its acquisitions and completes most
/// firings without the lock, while the released one does neither.
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
/// overlaps itself, whichever version runs it.
/// Every continuation is settled once, by a parent that completed on either node. And the 4.3-only
/// columns — <c>OVERLAP_POLICY</c>, the pause reason's three, a firing's progress — survive the 4.2
/// node firing, misfiring, updating, pausing and resuming the rows around them.
/// </para>
/// <para>
/// It runs in the PostgreSQL leg. Against a server at its shipped durability — the Testcontainers one
/// runs with <c>fsync</c> off — it runs like this, and the report goes to the detailed console log:
/// </para>
/// <code>
/// docker run -d --name g8-pg -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet -e POSTGRES_DB=quartznet -p 55432:5432 postgres:15.1
/// $env:QUARTZ_TEST_DATABASE = 'basic'   # start no container
/// $env:PG_CONNECTION_STRING = 'Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
/// dotnet test src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj `
///   --filter 'FullyQualifiedName~MixedVersionClusterPostgresTest' --logger 'console;verbosity=detailed'
/// </code>
/// </remarks>
[NonParallelizable]
[Category("db-postgres")]
[Category("migrations")]
public sealed class MixedVersionClusterPostgresTest
{
    /// <summary>The released Quartz the old node runs; the node project's csproj names the same one.</summary>
    private const string ReleasedQuartz = "4.2.2";

    private const string SchedulerName = "MixedVersionGate";

    /// <summary>
    /// This fixture's own, in the database every PostgreSQL fixture shares: <c>MigrationScriptTest</c> and
    /// <c>UpgradeRehearsalTest</c> own <c>QRTZM_</c>, <c>QRTZD_</c>, <c>QRTZS_</c> and <c>QRTZU_</c>.
    /// </summary>
    private const string TablePrefix = "QRTZV_";

    private const string RunsTable = "mixed_version_runs";

    private const string Released = "node-42";
    private const string WorkingTree = "node-43";

    private const int OneOffs = 2_000;

    /// <summary>
    /// The serial job's triggers each node schedules for each of its windows: two pinned to itself, and
    /// two either node may fire.
    /// </summary>
    private const int SerialTriggersEach = 2;

    private const int ChainPairs = 40;
    private const int ProgressFirings = 5;

    private const string PauseReason = "held for the 4.3 upgrade — ünïcode kept";
    private const string PausedBy = "g8-gate";
    private const string UpdatedDescription = "updated by the 4.2 node";
    private const int UpdatedPriority = 7;

    /// <summary>
    /// From the first trigger scheduled to the instant the work is due: long enough to start the
    /// working-tree node and have both nodes write their halves.
    /// </summary>
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long after the one-offs are due the serial job's triggers start: once the drain is over.
    /// </summary>
    /// <remarks>
    /// Not beside it, because of what the released node does with a trigger of a
    /// <see cref="DisallowConcurrentExecutionAttribute" /> job that is running on the other node. It
    /// acquires one trigger at a time by default, skips that one, finds nothing else in a batch of one,
    /// and waits out its whole idle wait — thirty seconds — before it looks again. With the serial job
    /// due during the drain, the 4.2 node sat most of it out: 51 of 2,000 one-offs in one run.
    /// </remarks>
    private static readonly TimeSpan SerialAfter = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long each of the serial job's three windows lasts: the 4.2 node's pinned triggers, then the
    /// working tree's, then triggers either node may fire.
    /// </summary>
    /// <remarks>
    /// Windows rather than every trigger at once, because a [DisallowConcurrentExecution] job stays with
    /// the node running it: that node's completion lets the job's other triggers go and its own loop is
    /// the first to reach for them. Left to that race, one node ran the job for the whole run as often
    /// as not. The pinned windows hand it from one version to the other; the last one is the race.
    /// </remarks>
    private static readonly TimeSpan SerialWindow = TimeSpan.FromSeconds(10);

    /// <summary>How long the serial job's triggers fire before the nodes stop.</summary>
    private static readonly TimeSpan SerialFor = 3 * SerialWindow;

    /// <summary>
    /// How long after the work is due the workload may take before the assertions say what is missing:
    /// twice the run, and short enough that a failing gate reports inside the leg's timeout.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    private const string CreateRunsTable =
        "CREATE TABLE " + RunsTable + " ("
        + "id BIGSERIAL PRIMARY KEY, trigger_group TEXT NOT NULL, trigger_name TEXT NOT NULL, job_group TEXT NOT NULL, "
        + "job_name TEXT NOT NULL, fire_instance_id TEXT NOT NULL, node TEXT NOT NULL, scheduled_fire_utc BIGINT NULL, "
        + "fired_utc BIGINT NOT NULL, started_utc BIGINT NOT NULL, started BIGINT NOT NULL, ended BIGINT NOT NULL, "
        + "recovering BOOLEAN NOT NULL, progress INTEGER NULL, progress_message TEXT NULL)";

    private const string SelectRuns =
        "SELECT trigger_group, trigger_name, job_name, fire_instance_id, node, scheduled_fire_utc, fired_utc, started_utc, "
        + "started, ended, recovering, progress, progress_message FROM " + RunsTable + " ORDER BY started";

    private const string SelectTriggerColumns =
        "SELECT trigger_state, overlap_policy, pause_reason, paused_by, paused_at, description, priority "
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

    [Test]
    public async Task AReleased42NodeAndAWorkingTreeNodeShareOneCluster()
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
            // The rolling upgrade: the 4.2 node is up, and writes its half, before the new node exists.
            released = await MixedVersionNodeProcess.Start(releasedAssembly, Released, SchedulerName, connectionString, TablePrefix, RunsTable);
            released.QuartzVersion.Should().StartWith(ReleasedQuartz, "the old node has to run the released package, not the working tree");
            released.Build.Should().Be("released");
            await released.Send("start");

            DateTimeOffset due = DateTimeOffset.UtcNow + Lead;
            await ScheduleHalf(released, first: 0, due);

            workingTree = await MixedVersionNodeProcess.Start(workingTreeAssembly, WorkingTree, SchedulerName, connectionString, TablePrefix, RunsTable);
            workingTree.QuartzVersion.Should().NotStartWith(ReleasedQuartz, "the new node has to run the working tree");
            workingTree.Build.Should().Be("working-tree");
            await workingTree.Send("start");

            await ScheduleHalf(workingTree, first: 1, due);
            StateArrangement state = await ArrangeWorkingTreeState(workingTree, due);
            await Backdate(connectionString, "held", "overlap-misfired", TimeSpan.FromMinutes(10));

            (DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1)).Should().BeBefore(due,
                "both nodes have to be up and every trigger written before the work is due, or part of the upgrade "
                + "happens before the cluster is mixed");

            string nodes = (await workingTree.Send("cluster-nodes"))["nodes"];
            nodes.Should().Be($"{Released}:Alive,{WorkingTree}:Alive", "each node sees the other checking in");

            // A few seconds into the drain, so that the 4.2 node has fired its overlap-policy trigger and
            // is writing fired rows for the one-offs while it works around the 4.3 rows.
            await Task.Delay(Until(due + TimeSpan.FromSeconds(3)));
            AroundTheState around = await WorkAroundTheState(released, due);

            await WaitForTheWorkload(connectionString, due, around);

            // Settled in both directions before the reads: a duplicate that lost a race by a few hundred
            // milliseconds arrives after the last expected execution, not before it.
            await Task.Delay(TimeSpan.FromSeconds(3));

            Dictionary<string, string> triggerPause = await workingTree.Send("trigger-pause", ("name", "paused-with-reason"), ("group", "state"));
            Dictionary<string, string> groupPause = await workingTree.Send("trigger-group-pause", ("group", "held"));
            Dictionary<string, string> jobGroupPause = await workingTree.Send("job-group-pause", ("group", "held-jobs"));

            await Task.WhenAll(released.Stop(waitForJobs: true), workingTree.Stop(waitForJobs: true));

            List<Run> runs = await ReadRuns(connectionString);
            ColumnsAfter columns = await ReadColumns(connectionString);

            TestContext.Out.WriteLine(Report(runs, due, elapsed.Elapsed, released, workingTree, await Durability(connectionString)));

            using (new AssertionScope())
            {
                AssertExactlyOnce(runs);
                AssertNoOverlap(runs);
                AssertContinuationsSettled(runs, columns);
                AssertWorkingTreeColumnsSurvived(runs, columns, state, around);
                AssertTheWorkingTreeReadsThemBack(columns, triggerPause, groupPause, jobGroupPause);
                AssertProgressSurvived(runs);
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
    /// What each node schedules, half of every workload: a thousand one-offs, twenty parent/continuation
    /// pairs, ten pinned each way across the two nodes, and three of the serial job's triggers.
    /// </summary>
    private static async Task ScheduleHalf(MixedVersionNodeProcess node, int first, DateTimeOffset due)
    {
        int oneOffHalf = OneOffs / 2;
        int chainQuarter = ChainPairs / 4;

        // This node's pinned window — the 4.2 node's first, then the working tree's — and its half of the
        // triggers either node may fire in the last one. SerialWindow says why.
        DateTimeOffset pinnedWindow = due + SerialAfter + first * SerialWindow;
        for (int i = 0; i < SerialTriggersEach; i++)
        {
            await node.Send("schedule",
                ("name", $"serial-{node.InstanceId}-{i.ToString(CultureInfo.InvariantCulture)}"), ("group", "serial"), ("job", "serial"),
                ("type", "serial"), ("start", pinnedWindow), ("end", pinnedWindow + SerialWindow), ("intervalMs", 1000),
                ("pin", node.InstanceId));
        }

        await node.Send("schedule-serial",
            ("from", first * SerialTriggersEach), ("count", SerialTriggersEach), ("start", due + SerialAfter + 2 * SerialWindow), ("intervalMs", 1000));
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
    /// The 4.3-only state, written by the working-tree node before the work is due.
    /// </summary>
    private static async Task<StateArrangement> ArrangeWorkingTreeState(MixedVersionNodeProcess workingTree, DateTimeOffset due)
    {
        // Fired by the 4.2 node every second, and updated by it.
        await workingTree.Send("schedule",
            ("name", "overlap-fired"), ("group", "state"), ("job", "state"), ("type", "state"),
            ("start", due), ("intervalMs", 1000), ("pin", Released), ("policy", nameof(OverlapPolicy.Skip)));

        // Paused with a reason, and updated by the 4.2 node while it is. Its start is past the run, so
        // nothing but the pause is keeping it from firing.
        await workingTree.Send("schedule",
            ("name", "paused-with-reason"), ("group", "state"), ("job", "state"), ("type", "state"),
            ("start", due + TimeSpan.FromMinutes(10)), ("intervalMs", 1000), ("policy", nameof(OverlapPolicy.BufferOne)));

        DateTimeOffset beforeTriggerPause = DateTimeOffset.UtcNow;
        await workingTree.Send("pause-trigger", ("name", "paused-with-reason"), ("group", "state"), ("reason", PauseReason), ("by", PausedBy));
        DateTimeOffset afterTriggerPause = DateTimeOffset.UtcNow;

        // Its neighbour, which the 4.2 node pauses and resumes.
        await workingTree.Send("schedule",
            ("name", "sibling"), ("group", "state"), ("job", "state"), ("type", "state"), ("start", due), ("intervalMs", 1000));

        // A trigger group and a job group paused with a reason, which the 4.2 node stores into and
        // pauses again.
        DateTimeOffset beforeGroupPauses = DateTimeOffset.UtcNow;
        await workingTree.Send("pause-trigger-group", ("group", "held"), ("reason", PauseReason), ("by", PausedBy));
        await workingTree.Send("pause-job-group", ("group", "held-jobs"), ("reason", PauseReason), ("by", PausedBy));
        DateTimeOffset afterGroupPauses = DateTimeOffset.UtcNow;

        // Born paused into that group, and pinned to the 4.2 node, which resumes it and then fires it.
        // The test makes it ten minutes overdue before the resume, so that the resume applies the
        // misfire policy and rewrites the row.
        await workingTree.Send("schedule",
            ("name", "overlap-misfired"), ("group", "held"), ("job", "state"), ("type", "state"),
            ("start", due), ("intervalMs", 1000), ("pin", Released), ("policy", nameof(OverlapPolicy.BufferOne)));

        return new StateArrangement(beforeTriggerPause, afterTriggerPause, beforeGroupPauses, afterGroupPauses);
    }

    /// <summary>
    /// The 4.2 node at work on and beside the rows that carry 4.3-only columns.
    /// </summary>
    private static async Task<AroundTheState> WorkAroundTheState(MixedVersionNodeProcess released, DateTimeOffset due)
    {
        DateTimeOffset updated = DateTimeOffset.UtcNow;
        await released.Send("update-trigger", ("name", "overlap-fired"), ("group", "state"), ("description", UpdatedDescription), ("priority", UpdatedPriority));
        await released.Send("update-trigger", ("name", "paused-with-reason"), ("group", "state"), ("description", UpdatedDescription), ("priority", UpdatedPriority));

        (await released.Send("pause-trigger", ("name", "sibling"), ("group", "state")))["paused"].Should().Be("True");
        (await released.Send("resume-trigger", ("name", "sibling"), ("group", "state")))["resumed"].Should().Be("True");
        DateTimeOffset siblingResumed = DateTimeOffset.UtcNow;

        // Into the trigger group the working tree paused with a reason: born paused, by the 4.2 node's
        // reading of that row. Then the group is paused again, which finds it paused already.
        await released.Send("schedule",
            ("name", "born-paused"), ("group", "held"), ("job", "state"), ("type", "state"), ("start", due), ("intervalMs", 1000));
        await released.Send("pause-trigger-group", ("group", "held"));

        // Its overdue neighbour resumed: the misfire policy is applied and the row rewritten by 4.2.
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
            "{0}.{1} is stored paused, and the backdating is what makes the 4.2 node's resume a misfire", group, name);
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
                        && runs.Any(x => x.Trigger == "state.sibling" && x.FiredUtc > around.SiblingResumed.UtcTicks)
                        && DateTimeOffset.UtcNow >= due + SerialAfter + SerialFor;

            if (done)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Not a failure of its own: the invariants below say exactly what is missing.
        TestContext.Out.WriteLine($"The workload was still incomplete {Deadline.TotalSeconds:0} s after it was due.");
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
            "exactly once, recovery: no node stopped checking in, so a recovering execution is a firing replayed because "
            + "one node declared the other dead",
            runs.Where(x => x.Recovering).Select(x => x.ToString()));

        Dictionary<string, int> share = oneOffs.Values
            .Select(x => x[0].Node)
            .GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

        // At least one each, so that the exactly-once claim is about a mixed cluster. How large a share the
        // released node takes varies from run to run, and the report says: acquiring one trigger at a
        // time, it waits out its idle wait after losing three races in a row to a node that takes ten.
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

        serial.Count.Should().BeGreaterThan((int) SerialFor.TotalSeconds,
            "the serial job's triggers fire every second for the run, so an overlap check over a handful of executions proves little");

        foreach (string node in (string[]) [Released, WorkingTree])
        {
            serial.Count(x => x.Node == node).Should().BeGreaterThan(0,
                "the serial job ran on both versions, or the no-overlap check did not cross them ({0} ran none)", node);
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
    /// The columns only 4.3 writes are still what 4.3 wrote after the 4.2 node fired, misfired,
    /// updated, paused and resumed the rows they sit on and beside.
    /// </summary>
    private static void AssertWorkingTreeColumnsSurvived(List<Run> runs, ColumnsAfter columns, StateArrangement state, AroundTheState around)
    {
        TriggerColumns fired = columns.Triggers["state.overlap-fired"];
        runs.Count(x => x.Trigger == "state.overlap-fired" && x.Node == Released && x.FiredUtc > around.Updated.UtcTicks).Should().BeGreaterThanOrEqualTo(3,
            "state.overlap-fired is pinned to the 4.2 node, which fires it every second after updating it");
        NoViolations("state.overlap-fired is pinned to the 4.2 node",
            runs.Where(x => x.Trigger == "state.overlap-fired" && x.Node != Released).Select(x => x.ToString()));
        fired.OverlapPolicy.Should().Be((int) OverlapPolicy.Skip,
            "state.overlap-fired's OVERLAP_POLICY survives the 4.2 node firing the trigger and updating its details");
        fired.Description.Should().Be(UpdatedDescription, "the 4.2 node's update of state.overlap-fired took effect");
        fired.Priority.Should().Be(UpdatedPriority);

        TriggerColumns misfired = columns.Triggers["held.overlap-misfired"];
        List<Run> misfiredRuns = runs.Where(x => x.Trigger == "held.overlap-misfired").ToList();
        misfiredRuns.Count.Should().BeGreaterThan(0, "the 4.2 node resumed held.overlap-misfired, which it then fires");
        NoViolations("held.overlap-misfired is pinned to the 4.2 node",
            misfiredRuns.Where(x => x.Node != Released).Select(x => x.ToString()));
        NoViolations(
            "held.overlap-misfired: the 4.2 node's resume applied the misfire policy, which moves the trigger past its ten "
            + "overdue minutes rather than firing them",
            misfiredRuns.Where(x => !(x.ScheduledFireUtc >= around.Resumed.UtcTicks - TimeSpan.TicksPerSecond)).Select(x => x.ToString()));
        misfired.OverlapPolicy.Should().Be((int) OverlapPolicy.BufferOne,
            "held.overlap-misfired's OVERLAP_POLICY survives the 4.2 node's resume, its misfire rewrite of the row and its firings");

        TriggerColumns paused = columns.Triggers["state.paused-with-reason"];
        paused.State.Should().Be("PAUSED", "nothing resumed state.paused-with-reason");
        paused.OverlapPolicy.Should().Be((int) OverlapPolicy.BufferOne);
        paused.PauseReason.Should().Be(PauseReason,
            "state.paused-with-reason's PAUSE_REASON survives the 4.2 node updating its details and pausing and resuming its sibling");
        paused.PausedBy.Should().Be(PausedBy);
        paused.PausedAt.Should().BeInRange(state.BeforeTriggerPause.UtcTicks - TimeSpan.TicksPerSecond, state.AfterTriggerPause.UtcTicks + TimeSpan.TicksPerSecond,
            "PAUSED_AT is when the working tree paused state.paused-with-reason");
        paused.Description.Should().Be(UpdatedDescription, "the 4.2 node's update of state.paused-with-reason took effect");
        NoViolations("state.paused-with-reason stayed paused", runs.Where(x => x.Trigger == "state.paused-with-reason").Select(x => x.ToString()));

        runs.Count(x => x.Trigger == "state.sibling" && x.FiredUtc > around.SiblingResumed.UtcTicks).Should().BeGreaterThan(0,
            "state.sibling, which the 4.2 node paused and resumed, kept firing");

        columns.TriggerGroupPause.Should().Be(new PauseColumns(PauseReason, PausedBy, columns.TriggerGroupPause?.PausedAt),
            "trigger group held's pause reason survives the 4.2 node storing into the group and pausing it again");
        columns.TriggerGroupPause?.PausedAt.Should().BeInRange(state.BeforeGroupPauses.UtcTicks - TimeSpan.TicksPerSecond, state.AfterGroupPauses.UtcTicks + TimeSpan.TicksPerSecond);
        columns.Triggers["held.born-paused"].State.Should().Be("PAUSED", "the 4.2 node read the trigger group row the working tree wrote");
        NoViolations("held.born-paused was born paused", runs.Where(x => x.Trigger == "held.born-paused").Select(x => x.ToString()));

        columns.JobGroupPause.Should().Be(new PauseColumns(PauseReason, PausedBy, columns.JobGroupPause?.PausedAt),
            "job group held-jobs's pause reason survives the 4.2 node storing a trigger for a job in it and pausing it again");
        columns.JobGroupPause?.PausedAt.Should().BeInRange(state.BeforeGroupPauses.UtcTicks - TimeSpan.TicksPerSecond, state.AfterGroupPauses.UtcTicks + TimeSpan.TicksPerSecond);
        columns.Triggers["state.parked"].State.Should().Be("PAUSED", "the 4.2 node read the job group row the working tree wrote");
        NoViolations("state.parked was born paused", runs.Where(x => x.Trigger == "state.parked").Select(x => x.ToString()));
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
    /// the 4.2 node's fired-row writes, its firings of the same job among them.
    /// </summary>
    private static void AssertProgressSurvived(List<Run> runs)
    {
        List<Run> progress = runs.Where(x => x.TriggerGroup == "progress").ToList();

        progress.Count(x => x.Node == WorkingTree).Should().Be(ProgressFirings, "the working tree ran each of its progress firings once");
        progress.Count(x => x.Node == Released).Should().Be(ProgressFirings, "the 4.2 node ran each of its progress firings once");

        NoViolations(
            "progress: what a 4.3 job reported is what its fired-trigger row still holds after the 4.2 node's writes",
            progress
                .Where(x => x.Node == WorkingTree && (x.Progress != 57 || x.ProgressMessage != "reported by " + x.FireInstanceId))
                .Select(x => $"{x} read back {x.Progress?.ToString(CultureInfo.InvariantCulture) ?? "no progress"}, '{x.ProgressMessage}'"));
    }

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
    /// 4.2.0's fresh install, every 4.3 PostgreSQL migration over it, and the runs table.
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

        await Execute(connection, MigrationScriptTest.BaselineScript("4.2", "postgres", TablePrefix));

        List<string> migrations = Migrations43();
        migrations.Should().Contain(["add_fire_progress", "add_overlap_policy", "add_pause_reason"],
            "the gate is the 4.3 migration, so it has to find the three scripts 4.3 requires");

        foreach (string migration in migrations)
        {
            await Execute(connection, MigrationScriptTest.MigrationScript("4.3", migration, "postgres", TablePrefix));
        }

        await Execute(connection, CreateRunsTable);
    }

    /// <summary>
    /// Every PostgreSQL script in <c>database/migrations/4.3/</c>, in the folder's order: what an operator
    /// running the whole folder runs, the optional history ones included.
    /// </summary>
    private static List<string> Migrations43()
    {
        DirectoryInfo current = new(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "database", "migrations", "4.3")))
        {
            current = current.Parent;
        }

        current.Should().NotBeNull("the 4.3 migrations are read from the repository this test was built in");

        const string suffix = "_postgres.sql";
        return Directory.GetFiles(Path.Combine(current!.FullName, "database", "migrations", "4.3"), "*" + suffix)
            .Select(x => Path.GetFileName(x)[..^suffix.Length])
            .Order(StringComparer.Ordinal)
            .ToList();
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
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return runs;
    }

    private static async Task<ColumnsAfter> ReadColumns(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        Dictionary<string, TriggerColumns> triggers = new(StringComparer.Ordinal);
        foreach ((string group, string name) in (IEnumerable<(string, string)>)
                 [("state", "overlap-fired"), ("state", "paused-with-reason"), ("held", "overlap-misfired"), ("held", "born-paused"), ("state", "parked")])
        {
            await using NpgsqlCommand command = new(SelectTriggerColumns, connection);
            command.Parameters.AddWithValue("schedulerName", SchedulerName);
            command.Parameters.AddWithValue("group", group);
            command.Parameters.AddWithValue("name", name);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                // Asserted on with everything else rather than here, so one gone row does not hide the rest.
                triggers[group + "." + name] = new TriggerColumns("<no row>", null, null, null, null, null, null);
                continue;
            }

            triggers[group + "." + name] = new TriggerColumns(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6));
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
        string durability)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Mixed-version cluster on PostgreSQL ({durability}): {Released} = Quartz {released.QuartzVersion}, {WorkingTree} = Quartz {workingTree.QuartzVersion}.");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"| Workload | {Released} | {WorkingTree} | Total |");
        report.AppendLine("|---|---:|---:|---:|");

        foreach ((string name, Func<Run, bool> filter) in (IEnumerable<(string, Func<Run, bool>)>)
                 [
                     ("one-offs", x => x.TriggerGroup == "one-off"),
                     ("serial job", x => x.JobName == "serial"),
                     ("parents", x => x.JobName == "parent"),
                     ("continuations", x => x.JobName == "continuation"),
                     ("progress", x => x.TriggerGroup == "progress"),
                     ("state.overlap-fired (pinned to 4.2)", x => x.Trigger == "state.overlap-fired"),
                     ("held.overlap-misfired (pinned to 4.2)", x => x.Trigger == "held.overlap-misfired"),
                     ("state.sibling", x => x.Trigger == "state.sibling")
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
                ? $" — {Released} starved: it acquires one trigger at a time and idles after three lost races."
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
        int? Priority);

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
        DateTimeOffset AfterGroupPauses);

    private sealed record AroundTheState(DateTimeOffset Updated, DateTimeOffset SiblingResumed, DateTimeOffset Resumed);
}
