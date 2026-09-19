using System.Data.Common;
using System.Diagnostics;
using System.Globalization;

namespace Quartz.Benchmark;

/// <summary>
/// What one one-off firing costs the database, counted at the database: commits, statements, and
/// every statement by name.
/// </summary>
/// <remarks>
/// <para>
/// A wall-clock figure for a persistent store is as much a statement about the disk it ran on as
/// about the scheduler. Commits and statements per firing are not — they are properties of the fire
/// path, they survive being re-run on somebody else's hardware, and they are what #3824 is counted
/// in. <see cref="OneOffThroughputPostgresBenchmark" /> is the wall clock beside them.
/// </para>
/// <para>
/// The census needs <c>pg_stat_statements</c>, which needs the server to have been started with the
/// extension preloaded; a plain <c>postgres:15.1</c> container has not. When it is missing the run
/// says so and reports commits only, rather than making the statement columns up.
/// </para>
/// <code>
/// docker run -d --name quartz-bench-pg -p 55432:5432 \
///   -e POSTGRES_DB=quartznet -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet \
///   postgres:15.1 -c shared_preload_libraries=pg_stat_statements
/// </code>
/// <para>
/// <b>What the window holds.</b> The counters are read after the firings are scheduled and the
/// scheduler is idling on them, and again after the drain plus a tail, so that the last firing's own
/// completion write is inside it. <c>DISCARD ALL</c> — Npgsql resetting a pooled connection on return
/// — is inside it too, and is its own implicit transaction, which is why the commit figure is larger
/// than the number of transactions Quartz opens. That is the finding, not an artefact: the census
/// names it as a row of its own.
/// </para>
/// </remarks>
internal static class OneOffCensus
{
    private const string SchedulerName = "PostgresOneOffCensus";

    /// <summary>How many firings each arm drains. Large enough that the per-firing division is steady.</summary>
    private const int Firings = 500;

    private const int MaxConcurrency = 10;

    /// <summary>
    /// How long the counters keep running after the last job body ran, so that its own completion
    /// write is inside the window.
    /// </summary>
    private static readonly TimeSpan tail = TimeSpan.FromSeconds(3);

    /// <summary>How long the scheduler is then watched with nothing to do, to price its polling.</summary>
    private static readonly TimeSpan idleSample = TimeSpan.FromSeconds(5);

    public static void Run()
    {
        RunCore().GetAwaiter().GetResult();
    }

    private static async Task RunCore()
    {
        await using BenchmarkDatabase database = await BenchmarkDatabase.Open(BenchmarkDialect.Postgres).ConfigureAwait(false);
        bool statements = await HasStatementStatistics(database).ConfigureAwait(false);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"One-off census: {Firings} firings per arm, MaxConcurrency {MaxConcurrency}, one durable job and one single-shot trigger per firing."));
        Console.WriteLine(statements
            ? "pg_stat_statements is loaded; the statement columns are counts."
            : "pg_stat_statements is NOT loaded on this server, so the statement columns are blank. Start the container with -c shared_preload_libraries=pg_stat_statements.");
        Console.WriteLine();

        List<string> tables = [];

        Console.WriteLine("| Profile                | Window (s) | Firings/s | Commits | Commits/firing | Statements | Statements/firing | Idle stmts/s |");
        Console.WriteLine("|------------------------|-----------:|----------:|--------:|---------------:|-----------:|------------------:|-------------:|");

        foreach (OneOffProfile profile in Enum.GetValues<OneOffProfile>())
        {
            foreach (bool noResetOnClose in (bool[]) [false, true])
            {
                tables.Add(await Measure(database, profile, noResetOnClose, statements).ConfigureAwait(false));
            }
        }

        foreach (string table in tables)
        {
            Console.WriteLine();
            Console.Write(table);
        }
    }

    private static async Task<string> Measure(BenchmarkDatabase database, OneOffProfile profile, bool noResetOnClose, bool statements)
    {
        await ClearRows(database, SchedulerName).ConfigureAwait(false);

        // Npgsql resets a pooled connection with DISCARD ALL on return unless the connection string
        // says otherwise, and each of those is a round trip and an implicit transaction of its own.
        // It is the operator's decision rather than a Quartz default, so it is measured as an arm
        // rather than changed (#3824).
        string connectionString = noResetOnClose
            ? database.Provider.ConnectionString + ";No Reset On Close=true"
            : database.Provider.ConnectionString;

        IScheduler scheduler = await OneOffThroughput.StartScheduler(
            instanceName: SchedulerName,
            maxConcurrency: MaxConcurrency,
            profile: profile,
            configureStore: quartz => quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.UseSystemTextJsonSerializer();
            })).ConfigureAwait(false);

        try
        {
            await OneOffThroughput.Seed(scheduler, Firings).ConfigureAwait(false);

            await ResetStatements(database, statements).ConfigureAwait(false);
            long commitsBefore = await Commits(database).ConfigureAwait(false);
            long statementsBefore = await Statements(database, statements).ConfigureAwait(false);
            long opened = Stopwatch.GetTimestamp();

            OneOffThroughput.Drain(Firings);
            TimeSpan drain = Stopwatch.GetElapsedTime(opened);
            await Task.Delay(tail).ConfigureAwait(false);

            TimeSpan window = Stopwatch.GetElapsedTime(opened);
            long commits = await Commits(database).ConfigureAwait(false) - commitsBefore;
            long statementsAfter = await Statements(database, statements).ConfigureAwait(false);
            long statementCount = statementsAfter - statementsBefore;
            List<Statement> census = await TopStatements(database, statements).ConfigureAwait(false);

            await Task.Delay(idleSample).ConfigureAwait(false);
            long idleStatements = await Statements(database, statements).ConfigureAwait(false) - statementsAfter;

            string arm = profile + (noResetOnClose ? " + no reset" : "");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {arm,-22} | {window.TotalSeconds,10:F1} | {Firings / drain.TotalSeconds,9:F1} | {commits,7:N0} | {(double) commits / Firings,14:F2} | {Format(statements, statementCount),10} | {Format(statements, (double) statementCount / Firings),17} | {Format(statements, idleStatements / idleSample.TotalSeconds),12} |"));

            return Itemise(arm, census, statements);
        }
        finally
        {
            await OneOffThroughput.StopScheduler(scheduler).ConfigureAwait(false);
            await ClearRows(database, SchedulerName).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Every statement the window ran, by call count, as a table to paste into the issue.
    /// </summary>
    private static string Itemise(string arm, List<Statement> census, bool statements)
    {
        if (!statements)
        {
            return "";
        }

        System.Text.StringBuilder builder = new();
        builder.Append(CultureInfo.InvariantCulture, $"### {arm}: every statement of the window\n\n");
        builder.Append("| Calls | Per firing | Total ms | Statement |\n");
        builder.Append("|------:|-----------:|---------:|-----------|\n");

        foreach (Statement statement in census)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"| {statement.Calls:N0} | {(double) statement.Calls / Firings:F2} | {statement.TotalMilliseconds:F1} | `{Condense(statement.Query)}` |\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// One line of SQL, short enough for a table cell and still recognisable as the statement it is.
    /// </summary>
    private static string Condense(string query)
    {
        string flat = string.Join(' ', query.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 160 ? flat : flat[..157] + "...";
    }

    private static string Format(bool statements, long value)
    {
        return statements ? value.ToString("N0", CultureInfo.InvariantCulture) : "-";
    }

    private static string Format(bool statements, double value)
    {
        return statements ? value.ToString("F2", CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>
    /// Deletes every row a scheduler name owns, children before parents, so an arm starts from the
    /// same empty schema the one before it did.
    /// </summary>
    public static async Task ClearRows(BenchmarkDatabase database, string schedulerName)
    {
        foreach (string table in (string[])
                 [
                     "QRTZ_FIRED_TRIGGERS",
                     "QRTZ_SIMPLE_TRIGGERS",
                     "QRTZ_CRON_TRIGGERS",
                     "QRTZ_SIMPROP_TRIGGERS",
                     "QRTZ_BLOB_TRIGGERS",
                     "QRTZ_TRIGGERS",
                     "QRTZ_JOB_DETAILS",
                     "QRTZ_PAUSED_TRIGGER_GRPS",
                     "QRTZ_SCHEDULER_STATE",
                     "QRTZ_LOCKS",
                 ])
        {
            await database.Execute($"DELETE FROM {table} WHERE SCHED_NAME = '{schedulerName}'").ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasStatementStatistics(BenchmarkDatabase database)
    {
        try
        {
            await database.Execute("CREATE EXTENSION IF NOT EXISTS pg_stat_statements").ConfigureAwait(false);
        }
        catch (DbException)
        {
            return false;
        }

        return await database.Scalar("SELECT COALESCE(SUM(calls), 0) FROM pg_stat_statements").ConfigureAwait(false) >= 0;
    }

    private static Task<long> Commits(BenchmarkDatabase database)
    {
        return database.Scalar("SELECT xact_commit FROM pg_stat_database WHERE datname = current_database()");
    }

    private static Task<long> Statements(BenchmarkDatabase database, bool statements)
    {
        return statements
            ? database.Scalar("SELECT COALESCE(SUM(calls), 0) FROM pg_stat_statements")
            : Task.FromResult(0L);
    }

    private static async Task ResetStatements(BenchmarkDatabase database, bool statements)
    {
        if (statements)
        {
            await database.Execute("SELECT pg_stat_statements_reset()").ConfigureAwait(false);
        }
    }

    private static async Task<List<Statement>> TopStatements(BenchmarkDatabase database, bool statements)
    {
        List<Statement> results = [];
        if (!statements)
        {
            return results;
        }

        using DbCommand command = database.Connection.CreateCommand();
        command.CommandText = "SELECT calls, total_exec_time, query FROM pg_stat_statements WHERE calls > 0 ORDER BY calls DESC LIMIT 40";

        using DbDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new Statement(reader.GetInt64(0), reader.GetDouble(1), reader.GetString(2)));
        }

        return results;
    }

    private sealed record Statement(long Calls, double TotalMilliseconds, string Query);
}
