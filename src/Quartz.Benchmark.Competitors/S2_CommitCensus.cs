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
/// <b>Three counters, because they answer different questions.</b> Commits are every transaction
/// PostgreSQL counted, a pooled connection's <c>DISCARD ALL</c> and a connection's start-up included,
/// and are read only after every other backend has published them — see
/// <see cref="PostgresFixture.Commits" />, which is what #3861 found missing. Writes are the
/// transactions that wrote something and paid for a flush, and are exact the instant they happen.
/// Statements need <c>pg_stat_statements</c>, which needs the server to have been started with the
/// extension preloaded; when it was not, those columns say so.
/// </para>
/// <para>
/// <b>What an engine does before the work is due is counted too, in columns of its own.</b> TickerQ
/// claims a ticker with an <c>UPDATE</c> of its own as soon as its loop sees it, which in this scenario
/// is twenty seconds before the ticker is due, so a window that opens at the due instant holds half of
/// what a TickerQ execution costs. The "ahead" columns run from the moment the scheduling call returned
/// to half a second before the due instant, and include whatever the engine polls while it waits — its
/// idle rate is the last column, to net it out by.
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
    /// one of these engines writes the execution's outcome after the job body returns.
    /// </remarks>
    private static readonly TimeSpan tail = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How far ahead of the due instant the window opens, so that publishing the commit counter — which
    /// closes every pooled connection and waits for its backend to exit — is done before anything is due.
    /// </summary>
    private static readonly TimeSpan openAhead = TimeSpan.FromMilliseconds(500);

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
            $"The window runs from {openAhead.TotalSeconds:F1} s before the due instant to {tail.TotalSeconds:F0} s after the last execution started, so the last completion write is inside it."));
        Console.WriteLine("First and last are when the first and the last execution started, in seconds after the due instant.");
        Console.WriteLine(fixture.HasStatementStatistics
            ? "pg_stat_statements is loaded; the statement columns are counts."
            : "pg_stat_statements is not loaded on this server, so the statement columns are blank. Start the container with -c shared_preload_libraries=pg_stat_statements for it.");
        Console.WriteLine();
        Console.WriteLine("| Engine         | First (s) | Last (s) | Commits/exec | Writes/exec | Statements/exec | Ahead: writes/exec | Ahead: statements/exec | Ahead (s) | Idle stmts/s |");
        Console.WriteLine("|----------------|----------:|---------:|-------------:|------------:|----------------:|-------------------:|-----------------------:|----------:|-------------:|");

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
        Completion.CaptureEntryTimestamp = true;

        try
        {
            await engine.Start(Harness.MaxConcurrency).ConfigureAwait(false);

            // The schema provisioning and the scheduling both commit, and neither is a firing, so the
            // first reading is taken after the scheduling call has returned.
            Completion.Arm(executions);
            DateTimeOffset dueAt = Harness.DueAt(S2PostgresThroughputBenchmark.Lead);
            await engine.ScheduleOneOff(executions, dueAt).ConfigureAwait(false);
            Harness.EnsureScheduledBeforeDue(dueAt, engine.Name);

            long aheadFrom = Stopwatch.GetTimestamp();
            long aheadWritesBefore = await fixture.Writes().ConfigureAwait(false);
            long aheadStatementsBefore = await fixture.Statements().ConfigureAwait(false);

            Harness.WaitUntil(dueAt - openAhead);

            long aheadWrites = await fixture.Writes().ConfigureAwait(false) - aheadWritesBefore;
            long aheadStatements = await fixture.Statements().ConfigureAwait(false) - aheadStatementsBefore;
            TimeSpan ahead = Stopwatch.GetElapsedTime(aheadFrom);

            await fixture.ResetStatements().ConfigureAwait(false);
            long commitsBefore = await fixture.Commits().ConfigureAwait(false);
            long writesBefore = await fixture.Writes().ConfigureAwait(false);
            long statementsBefore = await fixture.Statements().ConfigureAwait(false);

            Harness.WaitUntil(dueAt);
            long due = Stopwatch.GetTimestamp();
            Harness.EnsureNothingRanEarly(executions, engine.Name);

            Completion.Await();
            double first = Stopwatch.GetElapsedTime(due, Completion.FirstEntryTimestamp).TotalSeconds;
            double last = Stopwatch.GetElapsedTime(due, Completion.LastEntryTimestamp).TotalSeconds;
            await Task.Delay(tail).ConfigureAwait(false);

            long statementsAfter = await fixture.Statements().ConfigureAwait(false);
            long writes = await fixture.Writes().ConfigureAwait(false) - writesBefore;
            long commits = await fixture.Commits().ConfigureAwait(false) - commitsBefore;

            // The same engine, still running, with nothing left to do. The second read counts the first,
            // because pg_stat_statements counts a statement as it ends, so one is taken off.
            long idleFrom = await fixture.Statements().ConfigureAwait(false);
            await Task.Delay(idleSample).ConfigureAwait(false);
            long idleStatements = await fixture.Statements().ConfigureAwait(false) - idleFrom - 1;

            long statements = statementsAfter - statementsBefore;

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {arm,-14} | {first,9:F2} | {last,8:F2} | {(double) commits / executions,12:F2} | {(double) writes / executions,11:F2} | {PerExecution(fixture, statements, executions),15} | {(double) aheadWrites / executions,18:F2} | {PerExecution(fixture, aheadStatements, executions),22} | {ahead.TotalSeconds,9:F1} | {(fixture.HasStatementStatistics ? (idleStatements / idleSample.TotalSeconds).ToString("F1", CultureInfo.InvariantCulture) : "-"),12} |"));
        }
        finally
        {
            Completion.Disarm();
            Completion.CaptureEntryTimestamp = false;
            await engine.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string PerExecution(PostgresFixture fixture, long count, int executions)
    {
        return fixture.HasStatementStatistics
            ? ((double) count / executions).ToString("F2", CultureInfo.InvariantCulture)
            : "-";
    }
}
