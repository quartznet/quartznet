using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

using Microsoft.Data.SqlClient;

using MySqlConnector;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Benchmark;

/// <summary>
/// The databases a real-database benchmark can be pointed at.
/// </summary>
public enum BenchmarkDialect
{
    Postgres,
    SqlServer,
    MySql,
}

/// <summary>
/// Hands out the acquisition statement a delegate will actually run.
/// </summary>
/// <remarks>
/// The statement is assembled by <c>GetSelectNextTriggerToAcquireSql</c>, which each dialect overrides
/// to add its row limit and, on MySQL, an index hint. A plan captured for anything else would be a plan
/// for a copy kept in step by eye, so the delegates the benchmarks use expose the real one.
/// </remarks>
internal interface IAcquisitionSqlSource
{
    string AcquisitionSql(int maxCount);
}

/// <summary>
/// One open connection to a real database, with the schema from <c>database/tables/</c> applied and
/// the handful of dialect differences a benchmark has to know about answered in one place.
/// </summary>
/// <remarks>
/// <para>
/// The databases are started outside the process and named by an environment variable, because
/// BenchmarkDotNet runs a process per benchmark case and a container owned by a benchmark would be
/// started and thrown away once per case. See the remarks on <see cref="ExecutionCeilingBenchmark" />
/// and <see cref="AcquisitionIndexBenchmark" /> for the commands.
/// </para>
/// <para>
/// One connection is opened per parameter set and reused: pool acquisition is the same on both sides
/// of every question these benchmarks ask, and would only add variance.
/// </para>
/// </remarks>
internal sealed class BenchmarkDatabase : IAsyncDisposable
{
    private const string DatabaseName = "quartznet";

    private BenchmarkDatabase(BenchmarkDialect dialect, DbProvider provider, DbConnection connection)
    {
        Dialect = dialect;
        Provider = provider;
        Connection = connection;
        Holder = new ConnectionAndTransactionHolder(connection, null);
    }

    public BenchmarkDialect Dialect { get; }

    public DbProvider Provider { get; }

    public DbConnection Connection { get; }

    /// <summary>The holder the driver delegates take, wrapping <see cref="Connection" /> with no transaction.</summary>
    public ConnectionAndTransactionHolder Holder { get; }

    /// <summary>The boolean literal this dialect accepts in an <c>INSERT</c>.</summary>
    public string True => Dialect == BenchmarkDialect.Postgres ? "TRUE" : "1";

    /// <inheritdoc cref="True" />
    public string False => Dialect == BenchmarkDialect.Postgres ? "FALSE" : "0";

    public static async Task<BenchmarkDatabase> Open(BenchmarkDialect dialect)
    {
        string variable = dialect switch
        {
            BenchmarkDialect.Postgres => "QUARTZ_BENCHMARK_POSTGRES",
            BenchmarkDialect.SqlServer => "QUARTZ_BENCHMARK_SQLSERVER",
            _ => "QUARTZ_BENCHMARK_MYSQL",
        };

        string connectionString = Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException($"{variable} is not set; see the remarks on {nameof(BenchmarkDatabase)} for how to start the databases.");

        connectionString = dialect switch
        {
            // A fresh SQL Server container has only the system databases, and the table script's USE has
            // to land somewhere.
            BenchmarkDialect.SqlServer => await EnsureSqlServerDatabase(connectionString).ConfigureAwait(false),

            // The MySQL table script switches on a @DropDb user variable, which MySqlConnector reads as a
            // parameter placeholder unless the connection asks for user variables.
            BenchmarkDialect.MySql => new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true }.ConnectionString,

            _ => connectionString,
        };

        string providerName = dialect switch
        {
            BenchmarkDialect.Postgres => "Npgsql",
            BenchmarkDialect.SqlServer => "SqlServer",
            _ => "MySqlConnector",
        };

        DbProvider provider = new(providerName, connectionString);
        DbConnection connection = provider.CreateConnection();
        await connection.OpenAsync().ConfigureAwait(false);

        BenchmarkDatabase database = new(dialect, provider, connection);
        await database.EnsureSchema().ConfigureAwait(false);
        return database;
    }

    /// <summary>
    /// A driver delegate for this dialect, initialized against the connection this instance holds.
    /// </summary>
    public StdAdoDelegate CreateDelegate(string schedulerName, string instanceId, string tablePrefix)
    {
        StdAdoDelegate driverDelegate = Dialect switch
        {
            BenchmarkDialect.Postgres => new BenchmarkPostgreSQLDelegate(),
            BenchmarkDialect.SqlServer => new BenchmarkSqlServerDelegate(),
            _ => new BenchmarkMySQLDelegate(),
        };

        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = tablePrefix,
            SchedulerName = schedulerName,
            InstanceId = instanceId,
            TypeLoader = new SimpleTypeLoader(),
            DbProvider = Provider,
        });

        return driverDelegate;
    }

    public async ValueTask DisposeAsync()
    {
        Holder.Dispose();
        await Connection.DisposeAsync().ConfigureAwait(false);
    }

    public async Task Execute(string sql)
    {
        using DbCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 300;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a list of statements in chunks, so that seeding a hundred thousand rows does not arrive as
    /// one multi-megabyte command.
    /// </summary>
    public async Task ExecuteBatched(IReadOnlyList<string> statements, int chunkSize = 500)
    {
        StringBuilder chunk = new();
        for (int i = 0; i < statements.Count; i++)
        {
            chunk.Append(statements[i]).Append(";\n");
            if ((i + 1) % chunkSize == 0 || i == statements.Count - 1)
            {
                await Execute(chunk.ToString()).ConfigureAwait(false);
                chunk.Clear();
            }
        }
    }

    /// <summary>
    /// Runs a scalar query, answering -1 when the statement fails — which is how <see cref="EnsureSchema" />
    /// discovers that the tables are not there yet.
    /// </summary>
    public async Task<long> Scalar(string sql)
    {
        try
        {
            using DbCommand command = Connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 300;
            object? value = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return value is null or DBNull ? -1 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (DbException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The engine's plan for a statement, as lines fit to paste into an issue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every dialect here is asked for an <em>executed</em> plan rather than an estimated one, because
    /// the question an index audit asks — did the access path change, is there still a sort, how much
    /// was read — is only answered by row counts the engine actually saw. PostgreSQL and MySQL run the
    /// statement to produce one and SQL Server's <c>STATISTICS XML</c> is a by-product of running it, so
    /// this must not be pointed at a statement whose effects matter.
    /// </para>
    /// <para>
    /// A diagnostic degrades rather than takes the run down with it: anything thrown here comes back as
    /// the line it would have printed.
    /// </para>
    /// </remarks>
    public async Task<List<string>> Explain(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        try
        {
            if (Dialect == BenchmarkDialect.SqlServer)
            {
                return await ExplainSqlServer(sql, parameters).ConfigureAwait(false);
            }

            bool select = sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
            string prefix = Dialect switch
            {
                BenchmarkDialect.Postgres => "EXPLAIN (ANALYZE, BUFFERS) ",

                // MySQL 8.0 takes EXPLAIN ANALYZE for SELECT only; an UPDATE gets the estimated plan,
                // which still names the access path and the rows examined.
                _ => select ? "EXPLAIN ANALYZE " : "EXPLAIN ",
            };

            return await ReadRows(prefix + sql, parameters).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return ["<plan unavailable: " + exception.Message.Replace('\n', ' ') + ">"];
        }
    }

    /// <summary>
    /// SQL Server's actual plan, condensed to one line per operator, beside the logical reads
    /// <c>STATISTICS IO</c> reports. The raw showplan is tens of kilobytes of XML; what an index audit
    /// wants out of it is which object each operator touched, whether a sort survived, and how many rows
    /// really went through.
    /// </summary>
    private async Task<List<string>> ExplainSqlServer(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        SqlConnection sqlConnection = (SqlConnection) Connection;
        List<string> io = [];
        void OnInfoMessage(object sender, SqlInfoMessageEventArgs e) => io.Add(e.Message.Replace('\n', ' ').Trim());

        sqlConnection.InfoMessage += OnInfoMessage;
        try
        {
            await Execute("SET STATISTICS IO ON").ConfigureAwait(false);
            await Execute("SET STATISTICS XML ON").ConfigureAwait(false);

            List<string> raw = await ReadAllResults(sql, parameters).ConfigureAwait(false);
            List<string> lines = [];
            foreach (string candidate in raw)
            {
                if (candidate.StartsWith("<ShowPlanXML", StringComparison.Ordinal))
                {
                    lines.AddRange(CondenseShowPlan(candidate));
                }
            }

            lines.AddRange(io);
            return lines;
        }
        finally
        {
            sqlConnection.InfoMessage -= OnInfoMessage;
            await Execute("SET STATISTICS XML OFF").ConfigureAwait(false);
            await Execute("SET STATISTICS IO OFF").ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> CondenseShowPlan(string xml)
    {
        XNamespace showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        foreach (XElement relOp in XDocument.Parse(xml).Descendants(showplan + "RelOp"))
        {
            int depth = relOp.Ancestors(showplan + "RelOp").Count();
            string? index = relOp.Descendants(showplan + "Object").FirstOrDefault()?.Attribute("Index")?.Value;
            string? actual = relOp.Descendants(showplan + "RunTimeCountersPerThread").FirstOrDefault()?.Attribute("ActualRows")?.Value;

            yield return string.Create(CultureInfo.InvariantCulture,
                $"{new string(' ', depth * 2)}{relOp.Attribute("PhysicalOp")?.Value}{(index is null ? "" : " " + index)} est={relOp.Attribute("EstimateRows")?.Value} act={actual ?? "?"}");
        }
    }

    private async Task<List<string>> ReadRows(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        using DbCommand command = CreateCommand(sql, parameters);
        List<string> rows = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(RowText(reader));
        }

        return rows;
    }

    /// <summary>
    /// The same across every result set, which is where SQL Server puts the showplan it was asked for.
    /// </summary>
    private async Task<List<string>> ReadAllResults(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        using DbCommand command = CreateCommand(sql, parameters);
        List<string> rows = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        do
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                rows.Add(RowText(reader));
            }
        }
        while (await reader.NextResultAsync().ConfigureAwait(false));

        return rows;
    }

    private static string RowText(DbDataReader reader)
    {
        string[] fields = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "";
        }

        return string.Join(" | ", fields);
    }

    private DbCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        DbCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 300;

        foreach (KeyValuePair<string, object?> parameter in parameters)
        {
            DbParameter bound = command.CreateParameter();
            bound.ParameterName = "@" + parameter.Key;
            bound.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(bound);
        }

        return command;
    }

    /// <summary>
    /// Refreshes the optimizer's statistics for a table. A freshly seeded and freshly indexed table has
    /// none worth the name on any of these engines, and a plan chosen from nothing is not the plan a
    /// deployment gets.
    /// </summary>
    public Task UpdateStatistics(string table)
    {
        return Execute(Dialect switch
        {
            BenchmarkDialect.Postgres => "ANALYZE " + table,
            BenchmarkDialect.SqlServer => "UPDATE STATISTICS " + table + " WITH FULLSCAN",
            _ => "ANALYZE TABLE " + table,
        });
    }

    /// <summary>Applies <c>database/tables/</c> when the tables are not there yet.</summary>
    private async Task EnsureSchema()
    {
        if (await Scalar("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS").ConfigureAwait(false) >= 0)
        {
            return;
        }

        string script = ReadScript(Dialect switch
        {
            BenchmarkDialect.Postgres => "tables_postgres.sql",
            BenchmarkDialect.SqlServer => "tables_sqlServer.sql",
            _ => "tables_mysql_innodb.sql",
        });

        foreach (string batch in Batches(script))
        {
            await Execute(batch).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates the <c>quartznet</c> database if the server has not got one, and returns a connection
    /// string that names it.
    /// </summary>
    private static async Task<string> EnsureSqlServerDatabase(string connectionString)
    {
        SqlConnectionStringBuilder builder = new(connectionString) { TrustServerCertificate = true };
        string master = new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog = "master" }.ConnectionString;

        await using (SqlConnection connection = new(master))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID('{DatabaseName}') IS NULL CREATE DATABASE {DatabaseName}";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        builder.InitialCatalog = DatabaseName;
        return builder.ConnectionString;
    }

    /// <summary>
    /// Splits a script on the <c>GO</c> separators SQL Server's tooling uses; the PostgreSQL and MySQL
    /// scripts have none and come back whole.
    /// </summary>
    private static IEnumerable<string> Batches(string script)
    {
        StringBuilder batch = new();
        foreach (string line in script.Split('\n'))
        {
            if (line.Trim().TrimEnd('\r').Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (batch.Length > 0)
                {
                    yield return batch.ToString();
                    batch.Clear();
                }

                continue;
            }

            batch.Append(line.Replace("[enter_db_name_here]", "[" + DatabaseName + "]").Replace("[enter_path_here]", "/tmp")).Append('\n');
        }

        if (batch.ToString().Trim().Length > 0)
        {
            yield return batch.ToString();
        }
    }

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

        throw new FileNotFoundException("Could not locate the schema script.", fileName);
    }

    private sealed class BenchmarkPostgreSQLDelegate : PostgreSQLDelegate, IAcquisitionSqlSource
    {
        public string AcquisitionSql(int maxCount) => ReplaceTablePrefix(GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, 0)));
    }

    private sealed class BenchmarkSqlServerDelegate : SqlServerDelegate, IAcquisitionSqlSource
    {
        public string AcquisitionSql(int maxCount) => ReplaceTablePrefix(GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, 0)));
    }

    private sealed class BenchmarkMySQLDelegate : MySQLDelegate, IAcquisitionSqlSource
    {
        public string AcquisitionSql(int maxCount) => ReplaceTablePrefix(GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, 0)));
    }
}
