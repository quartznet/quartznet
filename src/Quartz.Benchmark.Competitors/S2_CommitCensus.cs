using System.Diagnostics;
using System.Globalization;

using Quartz.Benchmark.Competitors.Database;
using Quartz.Benchmark.Competitors.Engines;

namespace Quartz.Benchmark.Competitors;

/// <summary>
/// S2's second half: what one execution costs the database, counted at the database.
/// </summary>
/// <remarks>
/// <para>
/// A wall-clock figure for a persistent store is as much a statement about the disk it ran on as about
/// the scheduler. Commits per execution is not: it is a property of the fire path, it survives being
/// re-run on somebody else's hardware, and it is the figure #3802's D1 report used to show that half
/// of what PostgreSQL recorded as Quartz's transactions was the connection pool resetting a connection.
/// </para>
/// <para>
/// Counted over one complete window per engine rather than inside BenchmarkDotNet, because
/// <c>pg_stat_database.xact_commit</c> is database-wide and a benchmark case runs an unknown number of
/// warmup iterations either side of the measured ones. One window, one delta, one division.
/// </para>
/// <para>
/// The statement census beside it needs <c>pg_stat_statements</c>, which needs the server to have been
/// started with the extension preloaded. When it was not, the statement column says so.
/// </para>
/// </remarks>
internal static class S2CommitCensus
{
    public static void Run()
    {
        RunCore().GetAwaiter().GetResult();
    }

    /// <summary>
    /// How long the counters keep running after the last job started, so that its own completion write
    /// is inside the window.
    /// </summary>
    /// <remarks>
    /// The counter every arm increments is the executing job's first instruction, which is what makes
    /// the three comparable — but it means the drain ends when the last job <em>starts</em>, and every
    /// one of these engines writes the execution's outcome after the job body returns. Without the
    /// tail, TickerQ's census read one statement per execution short of the truth.
    /// </remarks>
    private static readonly TimeSpan tail = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long the engine is then watched doing nothing, to price its polling.
    /// </summary>
    /// <remarks>
    /// An idle Hangfire server polls its queue and its schedule at <c>QueuePollInterval</c> and
    /// <c>SchedulePollingInterval</c>, an idle TickerQ polls at <c>MinPollingInterval</c>, and an idle
    /// Quartz acquisition loop asks the store for triggers. That traffic is inside the window above as
    /// well, and it is part of what a scheduler costs a database — but a reader comparing the columns
    /// needs to know how much of each row is work and how much is watching, so it is measured rather
    /// than subtracted.
    /// </remarks>
    private static readonly TimeSpan idleSample = TimeSpan.FromSeconds(5);

    private static async Task RunCore()
    {
        await using PostgresFixture fixture = await PostgresFixture.Open().ConfigureAwait(false);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"S2 database census: {S2PostgresThroughputBenchmark.Executions} executions per engine, MaxConcurrency {Harness.MaxConcurrency}."));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"The window is the drain plus a {tail.TotalSeconds:F0} s tail, so the last execution's own completion write is inside it."));
        Console.WriteLine(fixture.HasStatementStatistics
            ? "pg_stat_statements is loaded; the statement columns are counts."
            : "pg_stat_statements is not loaded on this server, so the statement columns are blank. Start the container with -c shared_preload_libraries=pg_stat_statements for it.");
        Console.WriteLine();
        Console.WriteLine("| Engine                       | Window (s) | Commits | Commits/exec | Statements | Statements/exec | Idle stmts/s |");
        Console.WriteLine("|------------------------------|-----------:|--------:|-------------:|-----------:|----------------:|-------------:|");

        foreach (S2Arm arm in Enum.GetValues<S2Arm>())
        {
            await Measure(fixture, arm).ConfigureAwait(false);
        }
    }

    private static async Task Measure(PostgresFixture fixture, S2Arm arm)
    {
        await S2PostgresThroughputBenchmark.Reset(arm).ConfigureAwait(false);

        IEngine engine = S2PostgresThroughputBenchmark.Create(arm, PostgresFixture.ConnectionString);
        int executions = S2PostgresThroughputBenchmark.Executions;

        try
        {
            await engine.Start(Harness.MaxConcurrency).ConfigureAwait(false);

            // The schema provisioning and the scheduling both commit, and neither is a firing. The
            // counters are read after the work is in and before the window opens, so what the delta
            // holds is the drain.
            Completion.Arm(executions);
            DateTimeOffset dueAt = Harness.DueAt(S2PostgresThroughputBenchmark.Lead);
            await engine.ScheduleOneOff(executions, dueAt).ConfigureAwait(false);
            Harness.EnsureScheduledBeforeDue(dueAt, engine.Name);
            Harness.WaitUntil(dueAt);
            Harness.EnsureNothingRanEarly(executions, engine.Name);

            await fixture.ResetStatements().ConfigureAwait(false);
            long commitsBefore = await fixture.Commits().ConfigureAwait(false);
            long statementsBefore = await fixture.Statements().ConfigureAwait(false);
            long opened = Stopwatch.GetTimestamp();

            Completion.Await();
            await Task.Delay(tail).ConfigureAwait(false);

            TimeSpan window = Stopwatch.GetElapsedTime(opened);
            long commitsAfter = await fixture.Commits().ConfigureAwait(false);
            long statementsAfter = await fixture.Statements().ConfigureAwait(false);

            // The same engine, still running, with nothing left to do.
            await Task.Delay(idleSample).ConfigureAwait(false);
            long idleStatements = await fixture.Statements().ConfigureAwait(false) - statementsAfter;

            long commits = commitsAfter - commitsBefore;
            long statements = statementsAfter - statementsBefore;

            string statementCount = fixture.HasStatementStatistics
                ? statements.ToString("N0", CultureInfo.InvariantCulture)
                : "-";
            string statementsPer = fixture.HasStatementStatistics
                ? ((double) statements / executions).ToString("F2", CultureInfo.InvariantCulture)
                : "-";
            string idlePerSecond = fixture.HasStatementStatistics
                ? (idleStatements / idleSample.TotalSeconds).ToString("F1", CultureInfo.InvariantCulture)
                : "-";

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {arm,-28} | {window.TotalSeconds,10:F1} | {commits,7:N0} | {(double) commits / executions,12:F2} | {statementCount,10} | {statementsPer,15} | {idlePerSecond,12} |"));
        }
        finally
        {
            Completion.Disarm();
            await engine.DisposeAsync().ConfigureAwait(false);
        }
    }
}
