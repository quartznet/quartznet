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
/// <para>
/// <c>QUARTZ_CENSUS_DIALECT=sqlserver</c> runs it against <c>QUARTZ_BENCHMARK_SQLSERVER</c> instead (#4117):
/// commits are the database's <c>Transactions/sec</c> counter, statements are
/// <c>sys.dm_exec_query_stats</c>' executions under the database (the plan cache is cleared at the start of
/// each window), and there is no "no reset" arm, which is an Npgsql setting. The run says whether the
/// database has <c>READ_COMMITTED_SNAPSHOT</c> on, which decides whether its rounds take no lock.
/// </para>
/// </remarks>
internal static class OneOffCensus
{
    /// <summary>
    /// The database the census runs against: PostgreSQL, or SQL Server with
    /// <c>QUARTZ_CENSUS_DIALECT=sqlserver</c>.
    /// </summary>
    private static readonly BenchmarkDialect dialect = string.Equals(
        Environment.GetEnvironmentVariable("QUARTZ_CENSUS_DIALECT"), "sqlserver", StringComparison.OrdinalIgnoreCase)
        ? BenchmarkDialect.SqlServer
        : BenchmarkDialect.Postgres;

    private static readonly string SchedulerName = dialect == BenchmarkDialect.SqlServer ? "SqlServerOneOffCensus" : "PostgresOneOffCensus";

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

    /// <summary>
    /// Whether every store keeps its execution history in the database, from
    /// <c>QUARTZ_CENSUS_EXECUTION_HISTORY=true</c>: the history row and the job's status are then part of
    /// what a firing costs.
    /// </summary>
    /// <remarks>
    /// Every firing runs the one durable job of its job type, so the job's status row is inserted by the
    /// first firing and updated by every other. The history's tables are cleared once per run rather than
    /// per arm, so that first firing is in the first arm.
    /// </remarks>
    private static readonly bool executionHistory = string.Equals(
        Environment.GetEnvironmentVariable("QUARTZ_CENSUS_EXECUTION_HISTORY"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether that history is written behind, from <c>QUARTZ_CENSUS_HISTORY_WRITE_BEHIND=true</c> beside
    /// <c>QUARTZ_CENSUS_EXECUTION_HISTORY=true</c>: a completion queues its row, and a writer flushes the
    /// queue every <see cref="HistoryModes.WriteBehindInterval" /> with one status update per job (#3979).
    /// </summary>
    /// <remarks>
    /// The last flush of a drain lands inside the counted tail, which is longer than the interval, so the
    /// statement and commit columns still carry every firing's history.
    /// </remarks>
    private static readonly bool historyWriteBehind = executionHistory && string.Equals(
        Environment.GetEnvironmentVariable("QUARTZ_CENSUS_HISTORY_WRITE_BEHIND"), "true", StringComparison.OrdinalIgnoreCase);

    public static void Run()
    {
        RunCore().GetAwaiter().GetResult();
    }

    private static async Task RunCore()
    {
        await using BenchmarkDatabase database = await BenchmarkDatabase.Open(dialect).ConfigureAwait(false);
        bool statements = await HasStatementStatistics(database).ConfigureAwait(false);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"One-off census: {Firings} firings per arm, MaxConcurrency {MaxConcurrency}, one durable job and one single-shot trigger per firing."));
        if (dialect == BenchmarkDialect.SqlServer)
        {
            Console.WriteLine(statements
                ? "sys.dm_exec_query_stats is readable; the statement columns are counts, and the commits are the database's Transactions/sec counter."
                : "sys.dm_exec_query_stats is NOT readable by this login (VIEW SERVER STATE), so the statement columns are blank.");
            Console.WriteLine(await database.Scalar("SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()").ConfigureAwait(false) == 1
                ? "READ_COMMITTED_SNAPSHOT is ON: firing rounds may take no lock, and fire apart."
                : "READ_COMMITTED_SNAPSHOT is OFF: every firing round is locked, as 4.5's were.");
        }
        else
        {
            Console.WriteLine(statements
                ? "pg_stat_statements is loaded; the statement columns are counts."
                : "pg_stat_statements is NOT loaded on this server, so the statement columns are blank. Start the container with -c shared_preload_libraries=pg_stat_statements.");
        }

        if (executionHistory)
        {
            Console.WriteLine(historyWriteBehind
                ? "Execution history is on and written behind (QUARTZ_CENSUS_HISTORY_WRITE_BEHIND): completions are queued and flushed once a second, one status update per flush."
                : "Execution history is on (QUARTZ_CENSUS_EXECUTION_HISTORY): the first arm's first firing inserts the job's status row, every other firing updates it.");
            await ClearExecutionHistoryRows(database, SchedulerName).ConfigureAwait(false);
        }

        Console.WriteLine(RoundTripLatency.Describe());
        Console.WriteLine(LockFreeRoundsArm.Describe());
        Console.WriteLine();

        List<string> tables = [];

        Console.WriteLine("| Profile                | Window (s) | Firings/s | Commits | Commits/firing | Statements | Statements/firing | Idle stmts/s |");
        Console.WriteLine("|------------------------|-----------:|----------:|--------:|---------------:|-----------:|------------------:|-------------:|");

        foreach (OneOffProfile profile in Enum.GetValues<OneOffProfile>())
        {
            // "No Reset On Close" is an Npgsql setting; SQL Server has no such arm.
            foreach (bool noResetOnClose in dialect == BenchmarkDialect.Postgres ? (bool[]) [false, true] : [false])
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
                if (dialect == BenchmarkDialect.SqlServer)
                {
                    store.UseSqlServer(connectionString);
                }
                else
                {
                    store.UsePostgres(connectionString);
                }

                store.UseSystemTextJsonSerializer();
                RoundTripLatency.Inject(store, database.ProviderName, connectionString);
                LockFreeRoundsArm.Apply(store);

                if (executionHistory)
                {
                    store.UseExecutionHistory();
                }

                if (historyWriteBehind)
                {
                    quartz.Services.AddQuartzExecutionHistory(options => options.WriteBehindInterval = HistoryModes.WriteBehindInterval);
                }
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

    /// <summary>
    /// Deletes what a scheduler name wrote with execution history on. Apart from <see cref="ClearRows" />,
    /// because the census clears these once a run rather than once an arm: the job's status row is
    /// inserted by the first firing that finds none and updated by every other, and the census wants
    /// that first firing in its first arm alone.
    /// </summary>
    public static async Task ClearExecutionHistoryRows(BenchmarkDatabase database, string schedulerName)
    {
        foreach (string table in (string[]) ["QRTZ_EXECUTION_HISTORY", "QRTZ_MISFIRE_HISTORY", "QRTZ_JOB_STATUS"])
        {
            await database.Execute($"DELETE FROM {table} WHERE SCHED_NAME = '{schedulerName}'").ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasStatementStatistics(BenchmarkDatabase database)
    {
        if (dialect == BenchmarkDialect.SqlServer)
        {
            return await database.Scalar("SELECT COUNT_BIG(*) FROM sys.dm_exec_query_stats").ConfigureAwait(false) >= 0;
        }

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

    /// <summary>
    /// The statements SQL Server's plan cache says ran under this database, which is how its executions are
    /// told apart from other databases' on the same server: an ad hoc statement's text carries no database.
    /// </summary>
    private const string SqlServerQueryStatsOfDatabase =
        "FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_plan_attributes(qs.plan_handle) pa "
        + "WHERE pa.attribute = 'dbid' AND CONVERT(int, pa.value) = DB_ID()";

    private static Task<long> Commits(BenchmarkDatabase database)
    {
        return database.Scalar(dialect == BenchmarkDialect.SqlServer
            ? "SELECT cntr_value FROM sys.dm_os_performance_counters WHERE counter_name = 'Transactions/sec' AND object_name LIKE '%:Databases%' AND instance_name = DB_NAME()"
            : "SELECT xact_commit FROM pg_stat_database WHERE datname = current_database()");
    }

    private static Task<long> Statements(BenchmarkDatabase database, bool statements)
    {
        if (!statements)
        {
            return Task.FromResult(0L);
        }

        return database.Scalar(dialect == BenchmarkDialect.SqlServer
            ? "SELECT COALESCE(SUM(qs.execution_count), 0) " + SqlServerQueryStatsOfDatabase
            : "SELECT COALESCE(SUM(calls), 0) FROM pg_stat_statements");
    }

    private static async Task ResetStatements(BenchmarkDatabase database, bool statements)
    {
        if (statements)
        {
            // SQL Server keeps its counts with the cached plans, so clearing the cache is what resets them.
            await database.Execute(dialect == BenchmarkDialect.SqlServer ? "DBCC FREEPROCCACHE WITH NO_INFOMSGS" : "SELECT pg_stat_statements_reset()").ConfigureAwait(false);
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
        command.CommandText = dialect == BenchmarkDialect.SqlServer
            ? "SELECT TOP 40 qs.execution_count, CAST(qs.total_elapsed_time / 1000.0 AS float), "
              + "SUBSTRING(st.text, qs.statement_start_offset / 2 + 1, (CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2 + 1) "
              + SqlServerQueryStatsOfDatabase.Replace("CROSS APPLY", "CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st CROSS APPLY", StringComparison.Ordinal)
              + " AND qs.execution_count > 0 ORDER BY qs.execution_count DESC"
            : "SELECT calls, total_exec_time, query FROM pg_stat_statements WHERE calls > 0 ORDER BY calls DESC LIMIT 40";

        using DbDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new Statement(reader.GetInt64(0), reader.GetDouble(1), reader.GetString(2)));
        }

        return results;
    }

    private sealed record Statement(long Calls, double TotalMilliseconds, string Query);
}
