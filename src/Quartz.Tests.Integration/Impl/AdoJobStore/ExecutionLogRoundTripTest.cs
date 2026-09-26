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

using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A captured log far larger than any string column, in characters of more than one byte, written into
/// <c>QRTZ_EXECUTION_HISTORY.EXECUTION_LOG</c> and read back whole — on every dialect.
/// </summary>
/// <remarks>
/// <para>
/// The column is a large object on every dialect and the parameter that fills it is what differs: the
/// managed Oracle driver binds a string as <c>Varchar2</c>, and more than 4,000 bytes of that into a
/// <c>CLOB</c> fails with <c>ORA-01461</c>; SQL Server's delegate sizes every string 4,000 wide, and
/// SqlClient cuts a longer one to that size without a word. Either way the history store logs the
/// failure and drops the row, so a unit test on SQLite could never see it — only a real database of
/// each dialect can.
/// </para>
/// <para>
/// The log is 20,000 characters of <c>é</c> and <c>日本</c>, which is more than 20,000 bytes in any
/// encoding a database uses for them, so a bind that measured characters where the database counts
/// bytes would fail here too.
/// </para>
/// </remarks>
public abstract class ExecutionLogRoundTripTest
{
    private const int LogLength = 20_000;

    private readonly string schedulerName;

    protected ExecutionLogRoundTripTest()
    {
        // A name of this run's own, so a row a previous run left in a container's database is not
        // what the listing below counts.
        schedulerName = "ExecutionLog_" + GetType().Name + "_" + Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>The Quartz provider name of the ADO.NET driver.</summary>
    protected abstract string DbProviderName { get; }

    /// <summary>The delegate that speaks this database's dialect.</summary>
    protected abstract StdAdoDelegate CreateDriverDelegate();

    /// <summary>Makes the database ready and answers the connection string to reach it with.</summary>
    protected abstract ValueTask<string> PrepareDatabase();

    [Test]
    public async Task ALongMultibyteLogRoundTripsAndTheListingLeavesItOut()
    {
        string log = CapturedLog();
        Encoding.UTF8.GetByteCount(log).Should().BeGreaterThan(log.Length * 2,
            "the point of the text is that its byte count is well past its character count");

        using AdoExecutionHistoryStore store = await CreateStore();

        string entryId = Guid.NewGuid().ToString("N");
        await store.AddExecution(new ExecutionHistoryEntry(
            SchedulerName: schedulerName,
            SchedulerInstanceId: "node-a",
            JobGroup: "capture",
            JobName: "long-log",
            TriggerGroup: "capture",
            TriggerName: "once",
            FiredAtUtc: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromSeconds(3),
            Succeeded: true,
            ExceptionMessage: null)
        {
            EntryId = entryId,
            Log = log
        });

        ExecutionHistoryEntry read = await store.GetExecution(schedulerName, entryId);

        read.Should().NotBeNull(
            "the history store drops a row whose insert fails, so a missing row is the insert failing — "
            + "on Oracle, the ORA-01461 a Varchar2 parameter raises past 4,000 bytes into a CLOB");
        read.Log.Should().HaveLength(LogLength,
            "a parameter bound narrower than the log, as SQL Server's 4,000-wide strings are, is cut silently");
        read.Log.Should().Be(log, "every character, multibyte ones included, has to come back as it went in");

        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = schedulerName });

        ExecutionHistoryEntry listed = page.Items.Should().ContainSingle().Subject;
        listed.EntryId.Should().Be(entryId, "the listing hands out the key the single read finds the row by");
        listed.Log.Should().BeNull("EXECUTION_LOG is not in the listing's SELECT, so a page never carries a log");
    }

    private static string CapturedLog()
    {
        StringBuilder text = new(LogLength);
        string[] pieces = ["é", "日本", "\n"];

        for (int i = 0; text.Length < LogLength; i++)
        {
            text.Append(pieces[i % pieces.Length]);
        }

        return text.ToString(0, LogLength);
    }

    private async ValueTask<AdoExecutionHistoryStore> CreateStore()
    {
        string connectionString = await PrepareDatabase();
        IDbProvider dbProvider = new DbProvider(DbProviderName, connectionString);

        StdAdoDelegate driverDelegate = CreateDriverDelegate();
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = schedulerName,
            InstanceId = "node-a",
            DbProvider = dbProvider,
            TypeLoader = new SimpleTypeLoader(),
        });

        return new AdoExecutionHistoryStore(
            dbProvider,
            driverDelegate,
            Options.Create(new ExecutionHistoryOptions()),
            Options.Create(new QuartzSchedulerOptions { InstanceName = schedulerName }));
    }

    /// <summary>
    /// The connection string the container this assembly started published, as the job store contract
    /// fixtures read it.
    /// </summary>
    protected static string ContainerConnectionString(string variableName)
    {
        string connectionString = Environment.GetEnvironmentVariable(variableName);

        connectionString.Should().NotBeNullOrWhiteSpace(
            "{0} is set by the container this assembly starts, so an empty one means the container for this leg "
            + "never started — run the fixture through its own QUARTZ_TEST_DATABASE leg",
            variableName);

        return connectionString;
    }
}

[TestFixture]
[NonParallelizable]
[Category("db-postgres")]
public sealed class PostgresExecutionLogRoundTripTest : ExecutionLogRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.Npgsql;

    protected override StdAdoDelegate CreateDriverDelegate() => new PostgreSQLDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("PG_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-sqlserver")]
public sealed class SqlServerExecutionLogRoundTripTest : ExecutionLogRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.SqlServer;

    protected override StdAdoDelegate CreateDriverDelegate() => new SqlServerDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("MSSQL_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-mysql")]
public sealed class MySqlExecutionLogRoundTripTest : ExecutionLogRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.MySqlConnector;

    protected override StdAdoDelegate CreateDriverDelegate() => new MySQLDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("MYSQL_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-oracle")]
public sealed class OracleExecutionLogRoundTripTest : ExecutionLogRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.Oracle;

    protected override StdAdoDelegate CreateDriverDelegate() => new OracleDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("ORACLE_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-firebird")]
public sealed class FirebirdExecutionLogRoundTripTest : ExecutionLogRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.Firebird;

    protected override StdAdoDelegate CreateDriverDelegate() => new FirebirdDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("FIREBIRD_CONNECTION_STRING"));
}

/// <summary>
/// The SQLite leg, on a file built from the fresh-install script — the one dialect that needs no container.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("db-sqlite")]
public sealed class SqliteExecutionLogRoundTripTest : ExecutionLogRoundTripTest
{
    private SqliteTestDatabase database;

    protected override string DbProviderName => "SQLite-Microsoft";

    protected override StdAdoDelegate CreateDriverDelegate() => new SQLiteDelegate();

    protected override async ValueTask<string> PrepareDatabase()
    {
        database = new SqliteTestDatabase("execution-log");

        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = new(await File.ReadAllTextAsync(path), connection);
        await command.ExecuteNonQueryAsync();

        return database.ConnectionString;
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database?.Dispose();
    }
}
