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
/// A released 4.3 node, which acquires a round of triggers and then fires it, and a working-tree node,
/// which fires what is due in the transaction that acquires it (#3864), share one PostgreSQL cluster, and
/// nothing is lost, doubled or overlapped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The fired-trigger row is what the two versions agree through: a 4.3 node reads the
/// working tree's rows to decide whether a <see cref="DisallowConcurrentExecutionAttribute" /> job is running
/// and whether a node it recovers from left anything, and the working tree writes those rows in one
/// statement where 4.3 writes them in two. The row it writes is the row a 4.3 fire leaves —
/// <c>EXECUTING</c>, naming the job — and this is the proof, on the drain that makes the working-tree node
/// fire most of its rounds as it acquires them.
/// </para>
/// <para>
/// <b>The workload.</b> A thousand one-offs, due at once, each node scheduling half; a serial job's recurring
/// triggers, pinned to one node, then to the other, then free to either; and parents pinned to one node with
/// continuations pinned to the other, both ways round. The nodes are
/// <c>Quartz.Tests.Integration.MixedVersionNode</c>, the processes <c>MixedVersionClusterPostgresTest</c>
/// runs; the released one is built against Quartz 4.3.0 from nuget.org the first time this runs, beside the
/// build that test uses. The schema is 4.3's fresh install with the 4.4 migration applied.
/// </para>
/// <para>
/// <b>What must hold.</b> Every one-off runs exactly once, some on each node; no firing runs twice; the serial
/// job never overlaps itself on either version; every continuation runs once, on its node, after its parent.
/// </para>
/// <para>
/// It runs in the PostgreSQL leg, and against a server at its shipped durability the same way
/// <c>MixedVersionClusterPostgresTest</c> does.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-postgres")]
[Category("migrations")]
public sealed class FireOnAcquireMixedVersionPostgresTest
{
    /// <summary>The released Quartz the old node runs.</summary>
    private const string ReleasedQuartz = "4.3.0";

    private const string NodeProject = "Quartz.Tests.Integration.MixedVersionNode";

    private const string SchedulerName = "FireOnAcquireGate";

    /// <summary>This fixture's own prefix, beside the ones the other PostgreSQL fixtures own.</summary>
    private const string TablePrefix = "QRTZF_";

    private const string RunsTable = "fire_on_acquire_runs";

    private const string Released = "node-43";
    private const string WorkingTree = "node-44";

    private const int OneOffs = 1_000;
    private const int SerialTriggersEach = 2;
    private const int ChainPairs = 20;

    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SerialAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SerialWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SerialFor = 3 * SerialWindow;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How long before the next window a pinned window's last firing is due.
    /// </summary>
    /// <remarks>
    /// A simple trigger's end time is inclusive, so a window ending where the next begins has its last
    /// firing due at the same instant as the next window's first, and only one of the two runs a serial
    /// job. The node that loses releases its trigger, finds nothing else to take, and waits out its whole
    /// idle wait, which is the rest of the run — the gap is what keeps the hand-over from being a race.
    /// </remarks>
    private static readonly TimeSpan WindowGap = TimeSpan.FromSeconds(2);

    private const string CreateRunsTable =
        "CREATE TABLE " + RunsTable + " ("
        + "id BIGSERIAL PRIMARY KEY, trigger_group TEXT NOT NULL, trigger_name TEXT NOT NULL, job_group TEXT NOT NULL, "
        + "job_name TEXT NOT NULL, fire_instance_id TEXT NOT NULL, node TEXT NOT NULL, scheduled_fire_utc BIGINT NULL, "
        + "fired_utc BIGINT NOT NULL, started_utc BIGINT NOT NULL, started BIGINT NOT NULL, ended BIGINT NOT NULL, "
        + "recovering BOOLEAN NOT NULL, progress INTEGER NULL, progress_message TEXT NULL)";

    private const string SelectRuns =
        "SELECT trigger_group, trigger_name, job_name, fire_instance_id, node, scheduled_fire_utc, started_utc, started, ended, recovering "
        + "FROM " + RunsTable + " ORDER BY started";

    private const string SelectLeftoverChain =
        "SELECT trigger_name, trigger_state FROM qrtzf_triggers WHERE sched_name = @schedulerName AND trigger_group = 'chain' ORDER BY trigger_name";

    private const string SelectLeftoverTables =
        "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() "
        + "AND (table_name LIKE 'qrtzf\\_%' OR table_name = '" + RunsTable + "')";

    [Test]
    public async Task AReleased43NodeAndAWorkingTreeNodeFiringOnAcquireShareOneCluster()
    {
        string connectionString = MigrationScriptTest.RequireConnectionString("PG_CONNECTION_STRING");
        string releasedAssembly = await ReleasedNodeAssembly();
        string workingTreeAssembly = NodeAssembly(folder: NodeProject);

        Stopwatch elapsed = Stopwatch.StartNew();
        await PrepareDatabase(connectionString);

        MixedVersionNodeProcess released = null;
        MixedVersionNodeProcess workingTree = null;
        try
        {
            released = await MixedVersionNodeProcess.Start(releasedAssembly, Released, SchedulerName, connectionString, TablePrefix, RunsTable);
            released.QuartzVersion.Should().StartWith(ReleasedQuartz, "the old node has to run the released package, not the working tree");
            await released.Send("start");

            DateTimeOffset due = DateTimeOffset.UtcNow + Lead;
            await ScheduleHalf(released, first: 0, due);

            workingTree = await MixedVersionNodeProcess.Start(workingTreeAssembly, WorkingTree, SchedulerName, connectionString, TablePrefix, RunsTable);
            workingTree.QuartzVersion.Should().NotStartWith(ReleasedQuartz, "the new node has to run the working tree");
            workingTree.Build.Should().Be("working-tree");
            await workingTree.Send("start");

            await ScheduleHalf(workingTree, first: 1, due);

            (DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1)).Should().BeBefore(due,
                "both nodes have to be up and every trigger written before the work is due");

            (await workingTree.Send("cluster-nodes"))["nodes"].Should().Be($"{Released}:Alive,{WorkingTree}:Alive");

            // The working tree's pinned window, scheduled once the released node's has ended: storing the
            // triggers wakes the working tree's loop, whatever idle wait a skipped serial trigger left it in.
            DateTimeOffset secondWindow = due + SerialAfter + SerialWindow;
            await Task.Delay(Until(secondWindow - TimeSpan.FromSeconds(1)));
            await SchedulePinnedWindow(workingTree, secondWindow);

            await WaitForTheWorkload(connectionString, due);

            // Settled before the reads: a duplicate that lost a race arrives after the last expected run.
            await Task.Delay(TimeSpan.FromSeconds(3));
            await Task.WhenAll(released.Stop(waitForJobs: true), workingTree.Stop(waitForJobs: true));

            List<Run> runs = await ReadRuns(connectionString);
            Dictionary<string, string> leftoverChain = await ReadLeftoverChain(connectionString);

            TestContext.Out.WriteLine(Report(runs, due, elapsed.Elapsed, released, workingTree));

            using (new AssertionScope())
            {
                AssertExactlyOnce(runs);
                AssertNoOverlap(runs);
                AssertContinuationsSettled(runs, leftoverChain);
            }
        }
        finally
        {
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, new[] { released, workingTree }.Where(x => x is not null).Select(x => x.Describe())));

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

    /// <summary>
    /// What each node schedules up front: half the one-offs, its half of the serial job's shared window, a
    /// quarter of the chains each way round, and — the released node — its pinned window of the serial job.
    /// </summary>
    private static async Task ScheduleHalf(MixedVersionNodeProcess node, int first, DateTimeOffset due)
    {
        int oneOffHalf = OneOffs / 2;
        int chainQuarter = ChainPairs / 4;

        // Pinned windows hand the serial job from one version to the other; the last one is a race.
        if (first == 0)
        {
            await SchedulePinnedWindow(node, due + SerialAfter);
        }

        await node.Send("schedule-serial",
            ("from", first * SerialTriggersEach), ("count", SerialTriggersEach), ("start", due + SerialAfter + 2 * SerialWindow), ("intervalMs", 1000));
        await node.Send("schedule-one-offs", ("from", first * oneOffHalf), ("count", oneOffHalf), ("due", due));

        int chain = first * 2 * chainQuarter;
        await node.Send("schedule-chain", ("from", chain), ("count", chainQuarter), ("due", due), ("parentNode", Released), ("childNode", WorkingTree));
        await node.Send("schedule-chain", ("from", chain + chainQuarter), ("count", chainQuarter), ("due", due), ("parentNode", WorkingTree), ("childNode", Released));
    }

    /// <summary>
    /// The serial job's triggers pinned to <paramref name="node" />, firing every second of its window and
    /// done <see cref="WindowGap" /> before the next one begins.
    /// </summary>
    private static async Task SchedulePinnedWindow(MixedVersionNodeProcess node, DateTimeOffset start)
    {
        for (int i = 0; i < SerialTriggersEach; i++)
        {
            await node.Send("schedule",
                ("name", $"serial-{node.InstanceId}-{i.ToString(CultureInfo.InvariantCulture)}"), ("group", "serial"), ("job", "serial"),
                ("type", "serial"), ("start", start), ("end", start + SerialWindow - WindowGap), ("intervalMs", 1000),
                ("pin", node.InstanceId));
        }
    }

    private static TimeSpan Until(DateTimeOffset instant)
    {
        TimeSpan remaining = instant - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static async Task WaitForTheWorkload(string connectionString, DateTimeOffset due)
    {
        DateTimeOffset giveUp = due + Deadline;
        while (DateTimeOffset.UtcNow < giveUp)
        {
            List<Run> runs = await ReadRuns(connectionString);

            bool done = runs.Where(x => x.TriggerGroup == "one-off").Select(x => x.TriggerName).Distinct(StringComparer.Ordinal).Count() >= OneOffs
                        && runs.Where(x => x.TriggerGroup == "chain").Select(x => x.TriggerName).Distinct(StringComparer.Ordinal).Count() >= 2 * ChainPairs
                        && DateTimeOffset.UtcNow >= due + SerialAfter + SerialFor;

            if (done)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Not a failure of its own: the invariants say exactly what is missing.
        TestContext.Out.WriteLine($"The workload was still incomplete {Deadline.TotalSeconds:0} s after it was due.");
    }

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
            "exactly once, fire instances: a fire instance id names one firing",
            runs.GroupBy(x => x.FireInstanceId, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => Describe(x.ToList())));

        NoViolations(
            "exactly once, recurring triggers: one scheduled fire time of one trigger ran more than once",
            runs.Where(x => x.ScheduledFireUtc is not null)
                .GroupBy(x => (x.Trigger, x.ScheduledFireUtc))
                .Where(x => x.Count() > 1)
                .Select(x => Describe(x.ToList())));

        NoViolations(
            "exactly once, recovery: no node stopped checking in, so a recovering execution is a firing replayed because one "
            + "node read the other's fired-trigger rows as a dead node's",
            runs.Where(x => x.Recovering).Select(x => x.ToString()));

        foreach (string node in (string[]) [Released, WorkingTree])
        {
            oneOffs.Values.Count(x => x[0].Node == node).Should().BeGreaterThan(0,
                "both nodes run one-offs, or the exactly-once check was not made across versions");
        }
    }

    private static void AssertNoOverlap(List<Run> runs)
    {
        List<Run> serial = runs.Where(x => x.JobName == "serial").OrderBy(x => x.Started).ToList();

        List<string> overlaps = [];
        for (int i = 1; i < serial.Count; i++)
        {
            if (serial[i].Started < serial[i - 1].Ended)
            {
                overlaps.Add($"{serial[i]} started before {serial[i - 1]} ended");
            }
        }

        NoViolations(
            "no overlap: [DisallowConcurrentExecution] holds across the cluster, through the fired-trigger rows each version "
            + "reads the other's firings from",
            overlaps);

        serial.Count.Should().BeGreaterThan((int) SerialFor.TotalSeconds,
            "the serial job's triggers fire every second for the run, so an overlap check over a handful proves little");

        foreach (string node in (string[]) [Released, WorkingTree])
        {
            serial.Count(x => x.Node == node).Should().BeGreaterThan(0, "the serial job ran on both versions ({0} ran none)", node);
        }
    }

    private static void AssertContinuationsSettled(List<Run> runs, Dictionary<string, string> leftoverChain)
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
            }
            else if (continuation.Count != 1)
            {
                unsettled.Add($"chain.continuation-{index} ran {continuation.Count} time(s): {Describe(continuation)}; "
                              + $"its row is {leftoverChain.GetValueOrDefault("continuation-" + index, "gone")}");
            }
            else if (continuation[0].Node != childNode || continuation[0].Started < parent[0].Ended)
            {
                unsettled.Add($"chain.continuation-{index} ran on {continuation[0].Node}, pinned to {childNode}, relative to its parent on {parentNode}");
            }
        }

        NoViolations("settled continuations: each runs exactly once, on its node, after its parent", unsettled);
        NoViolations("settled continuations: a row left in the chain group is one no completion settled",
            leftoverChain.Select(x => $"chain.{x.Key} is still stored, {x.Value}"));
    }

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

    /// <summary>
    /// 4.3.0's fresh install, the 4.4 PostgreSQL migration over it, and the runs table.
    /// </summary>
    private static async Task PrepareDatabase(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

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

        await Execute(connection, MigrationScriptTest.BaselineScript("4.3", "postgres", TablePrefix));

        const string suffix = "_postgres.sql";
        foreach (string migration in Directory.GetFiles(RepositoryDirectory("database", "migrations", "4.4"), "*" + suffix)
                     .Select(x => Path.GetFileName(x)[..^suffix.Length])
                     .Order(StringComparer.Ordinal))
        {
            await Execute(connection, MigrationScriptTest.MigrationScript("4.4", migration, "postgres", TablePrefix));
        }

        await Execute(connection, CreateRunsTable);
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
                reader.GetBoolean(9)));
        }

        return runs;
    }

    private static async Task<Dictionary<string, string>> ReadLeftoverChain(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(SelectLeftoverChain, connection);
        command.Parameters.AddWithValue("schedulerName", SchedulerName);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        Dictionary<string, string> leftover = new(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            leftover[reader.GetString(0)] = reader.GetString(1);
        }

        return leftover;
    }

    private static string Report(List<Run> runs, DateTimeOffset due, TimeSpan elapsed, MixedVersionNodeProcess released, MixedVersionNodeProcess workingTree)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Fire-on-acquire mixed-version cluster on PostgreSQL: {Released} = Quartz {released.QuartzVersion}, {WorkingTree} = Quartz {workingTree.QuartzVersion}.");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"| Workload | {Released} | {WorkingTree} | Total |");
        report.AppendLine("|---|---:|---:|---:|");

        foreach ((string name, Func<Run, bool> filter) in (IEnumerable<(string, Func<Run, bool>)>)
                 [
                     ("one-offs", x => x.TriggerGroup == "one-off"),
                     ("serial job", x => x.JobName == "serial"),
                     ("parents", x => x.JobName == "parent"),
                     ("continuations", x => x.JobName == "continuation"),
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
        }

        return report.ToString();
    }

    private static string Describe(List<Run> runs) => runs.Count == 0 ? "<none>" : string.Join("; ", runs.Select(x => x.ToString()));

    /// <summary>
    /// The node built against Quartz <see cref="ReleasedQuartz" />, built here the first time it is
    /// needed: the node project builds the released version <c>MixedVersionClusterPostgresTest</c> runs, and
    /// this one beside it, as that project's own <c>-p:ReleasedQuartzVersion</c> build.
    /// </summary>
    private static async Task<string> ReleasedNodeAssembly()
    {
        string folder = NodeProject + "." + ReleasedQuartz;
        string path = NodeAssemblyPath(folder);
        if (File.Exists(path))
        {
            return path;
        }

        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(Path.Combine(RepositoryDirectory("src", NodeProject), NodeProject + ".csproj"));
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(configuration.Equals("release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug");
        start.ArgumentList.Add("-p:ReleasedQuartzVersion=" + ReleasedQuartz);

        using Process build = Process.Start(start)!;
        Task<string> output = build.StandardOutput.ReadToEndAsync();
        Task<string> errors = build.StandardError.ReadToEndAsync();
        await build.WaitForExitAsync();

        build.ExitCode.Should().Be(0, $"the {ReleasedQuartz} node has to build: {await output}{await errors}");
        return NodeAssembly(folder);
    }

    private static string NodeAssembly(string folder)
    {
        string path = NodeAssemblyPath(folder);
        File.Exists(path).Should().BeTrue(
            $"the gate runs both nodes out of process; 'dotnet build src/{NodeProject}/{NodeProject}.csproj' builds the working-tree node");
        return path;
    }

    private static string NodeAssemblyPath(string folder)
    {
        DirectoryInfo configuration = new(AppContext.BaseDirectory);
        DirectoryInfo bin = configuration.Parent?.Parent
            ?? throw new InvalidOperationException($"Cannot locate the artifacts directory from {AppContext.BaseDirectory}.");

        return Path.Combine(bin.FullName, folder, configuration.Name, NodeProject + ".dll");
    }

    private static string RepositoryDirectory(params string[] segments)
    {
        DirectoryInfo current = new(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine([current.FullName, .. segments])))
        {
            current = current.Parent;
        }

        current.Should().NotBeNull($"{Path.Combine(segments)} is read from the repository this test was built in");
        return Path.Combine([current!.FullName, .. segments]);
    }

    /// <summary>One execution, as the job that ran wrote it.</summary>
    private sealed record Run(
        string TriggerGroup,
        string TriggerName,
        string JobName,
        string FireInstanceId,
        string Node,
        long? ScheduledFireUtc,
        long StartedUtc,
        long Started,
        long Ended,
        bool Recovering)
    {
        public string Trigger => TriggerGroup + "." + TriggerName;

        public override string ToString() => $"{Trigger} on {Node} (fire instance {FireInstanceId})";
    }
}
