using System.Globalization;
using System.Text;

using Npgsql;

namespace Quartz.Benchmark.Competitors.Database;

/// <summary>
/// The one PostgreSQL database the three libraries share, and the counters read off it.
/// </summary>
/// <remarks>
/// <para>
/// One database, three schemas, and none of them had to be invented: Quartz's shipped DDL creates
/// unqualified <c>qrtz_*</c> tables, so it lives in <c>public</c>; TickerQ's EF Core model defaults to
/// <c>ticker</c>; Hangfire's PostgreSQL storage defaults to <c>hangfire</c>. So each library is in its
/// own schema at its own defaults, and a reset of one cannot touch another.
/// </para>
/// <para>
/// The database is started outside the process and named by an environment variable, because
/// BenchmarkDotNet runs a process per benchmark case and a container owned by a benchmark would be
/// started and thrown away once per case. <c>README.md</c> has the <c>docker run</c>.
/// </para>
/// <para>
/// <b>Counting at the database.</b> <c>pg_stat_database.xact_commit</c> is what turns "6 ms a firing"
/// into "this many commits a firing", which is the figure #3802's D1 report used and the one that
/// survives being run on somebody else's disk. <c>pg_stat_statements</c> gives the statement census
/// beside it when the server was started with the extension preloaded; when it was not, the statement
/// columns say so rather than guessing.
/// </para>
/// </remarks>
internal sealed class PostgresFixture : IAsyncDisposable
{
    /// <summary>Where the connection string comes from — the same variable <c>Quartz.Benchmark</c> reads.</summary>
    public const string ConnectionStringVariable = "QUARTZ_BENCHMARK_POSTGRES";

    /// <summary>TickerQ's default EF Core schema.</summary>
    public const string TickerQSchema = "ticker";

    /// <summary>Hangfire's default PostgreSQL schema.</summary>
    public const string HangfireSchema = "hangfire";

    private readonly NpgsqlConnection connection;

    private PostgresFixture(NpgsqlConnection connection)
    {
        this.connection = connection;
    }

    /// <summary>The connection string every engine is pointed at.</summary>
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable)
        ?? throw new InvalidOperationException(
            $"{ConnectionStringVariable} is not set. README.md has the docker run and the connection string to set.");

    /// <summary>Whether the server has <c>pg_stat_statements</c> available.</summary>
    public bool HasStatementStatistics { get; private set; }

    public static async ValueTask<PostgresFixture> Open(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        PostgresFixture fixture = new(connection);
        fixture.HasStatementStatistics = await fixture.TryEnableStatementStatistics(cancellationToken).ConfigureAwait(false);

        return fixture;
    }

    /// <summary>
    /// Drops and recreates Quartz's tables from <c>database/tables/tables_postgres.sql</c>, which is
    /// what that script does when run twice.
    /// </summary>
    public async ValueTask ResetQuartz(CancellationToken cancellationToken = default)
    {
        await Execute(ReadScript("tables_postgres.sql"), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops a schema whole, which is the cheapest reset a library's own tables can have.</summary>
    public async ValueTask ResetSchema(string schema, CancellationToken cancellationToken = default)
    {
        await Execute($"DROP SCHEMA IF EXISTS {schema} CASCADE", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The database's committed-transaction counter.</summary>
    public async ValueTask<long> Commits(CancellationToken cancellationToken = default)
    {
        return await Scalar("SELECT xact_commit FROM pg_stat_database WHERE datname = current_database()", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// How many statements the server has executed, or <c>-1</c> when <c>pg_stat_statements</c> is not
    /// loaded.
    /// </summary>
    public async ValueTask<long> Statements(CancellationToken cancellationToken = default)
    {
        if (!HasStatementStatistics)
        {
            return -1;
        }

        return await Scalar("SELECT COALESCE(SUM(calls), 0) FROM pg_stat_statements", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Zeroes the statement census, so a run's statements can be attributed to that run.</summary>
    public async ValueTask ResetStatements(CancellationToken cancellationToken = default)
    {
        if (HasStatementStatistics)
        {
            await Execute("SELECT pg_stat_statements_reset()", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The statements the server ran most often, for the per-firing census.</summary>
    public async ValueTask<List<(long Calls, string Query)>> TopStatements(int count, CancellationToken cancellationToken = default)
    {
        List<(long, string)> results = [];
        if (!HasStatementStatistics)
        {
            return results;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT calls, query FROM pg_stat_statements ORDER BY calls DESC LIMIT "
            + count.ToString(CultureInfo.InvariantCulture);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<bool> TryEnableStatementStatistics(CancellationToken cancellationToken)
    {
        try
        {
            await Execute("CREATE EXTENSION IF NOT EXISTS pg_stat_statements", cancellationToken).ConfigureAwait(false);
            await Scalar("SELECT COALESCE(SUM(calls), 0) FROM pg_stat_statements", cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException)
        {
            // The extension needs shared_preload_libraries, which a plain postgres:15.1 container does
            // not have. The statement columns say "not collected" rather than being made up.
            return false;
        }
    }

    private async ValueTask Execute(string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<long> Scalar(string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Finds a script under <c>database/tables/</c> by walking up from the built assembly, which is
    /// what <c>Quartz.Benchmark</c>'s own fixture does.
    /// </summary>
    private static string ReadScript(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "database", "tables", fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate, Encoding.UTF8);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find database/tables/{fileName} above {AppContext.BaseDirectory}.");
    }
}
