using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Reusable base class for clustered integration tests, parameterized by the database behind them.
/// Provides scheduler creation, job execution recording, direct SQL against the store's own tables,
/// and polling helpers.
/// <para>
/// Uses the assembly-wide databases (started once by <see cref="TestcontainersDatabaseEnvironment"/>,
/// addressed through <see cref="ClusteredTestDatabase"/>); it does not provision its own container.
/// All derived fixtures share those databases, with <see cref="SchedulerName"/> as the only isolation
/// axis within one of them — derived fixtures must use a unique scheduler name and be marked
/// <c>[NonParallelizable]</c> because they also share static execution records.
/// </para>
/// <para>
/// <b>There is deliberately no <c>FakeTimeProvider</c> under this base class.</b> A clustered node
/// decides that a peer has died by comparing the peer's <c>LAST_CHECKIN_TIME</c> — an absolute instant
/// sitting in <c>QRTZ_SCHEDULER_STATE</c> — against its own clock, and the same is true of every other
/// node reading the same row. The database is the clock the cluster agrees on. Handing one node
/// a fake clock would move that node's arithmetic while the rows, the peers, the connection timeouts
/// and the server's own timing stayed on real time, so the test would exercise a cluster that does not
/// exist. Where a test needs the past, it moves the database instead: see
/// <see cref="BackdateCheckin"/>, which ages a check-in row rather than sleeping past a threshold.
/// A single-node fixture that drives a store's own misfire pass by hand has no such peers to agree
/// with, and does use a fake clock — see <c>MisfireThroughAStoreTestBase</c>.
/// </para>
/// </summary>
[NonParallelizable]
public abstract class ClusteredJobStoreTestBase
{
    protected ClusteredJobStoreTestBase(string provider)
    {
        Database = ClusteredTestDatabase.For(provider);
    }

    /// <summary>
    /// The database this fixture's nodes share.
    /// </summary>
    protected ClusteredTestDatabase Database { get; }

    protected virtual string SchedulerName => "ClusteredTest";

    [SetUp]
    public void ResetRecordingJob() => RecordingJob.Reset();

    [TearDown]
    public async Task CleanUpDatabaseState()
    {
        // Tests shut their schedulers down in finally blocks, but a clustered node's own
        // SCHEDULER_STATE row survives Shutdown (it is only deleted by another node's
        // ClusterRecover), and scheduler.Clear() does not touch it either. Remove all
        // rows for this fixture's scheduler so later tests start against a clean cluster.
        //
        // One statement per round trip rather than one semicolon-separated batch: Oracle has no
        // statement separator outside a PL/SQL block and Firebird's driver takes one statement per
        // command, so a batch here would work on three engines and fail on two.
        await ExecuteStatements(
            [
                "DELETE FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_CRON_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_SIMPROP_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_BLOB_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_JOB_DETAILS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_CALENDARS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_PAUSED_TRIGGER_GRPS WHERE SCHED_NAME = @schedulerName",
                "DELETE FROM QRTZ_SCHEDULER_STATE WHERE SCHED_NAME = @schedulerName",
            ],
            ("schedulerName", SchedulerName));
    }

    protected async Task<IScheduler> CreateScheduler(
        string instanceId,
        int checkinIntervalMs = 1000,
        int checkinMisfireThresholdMs = 2000,
        Action<NameValueCollection> configure = null)
    {
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = SchedulerName,
            ["quartz.scheduler.instanceId"] = instanceId,
            // Short idle wait so nodes notice remote changes (re-pins, failover resets)
            // within seconds instead of the 30 s default acquisition cycle
            ["quartz.scheduler.idleWaitTime"] = "2000",
            ["quartz.threadPool.maxConcurrency"] = "2",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
            ["quartz.jobStore.driverDelegateType"] = Database.DriverDelegateType,
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.clusterCheckinInterval"] = checkinIntervalMs.ToString(CultureInfo.InvariantCulture),
            ["quartz.jobStore.clusterCheckinMisfireThreshold"] = checkinMisfireThresholdMs.ToString(CultureInfo.InvariantCulture),
            ["quartz.dataSource.default.provider"] = Database.Provider,
            ["quartz.dataSource.default.connectionString"] = Database.ConnectionString,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        configure?.Invoke(properties);

        // Cluster nodes share the scheduler (instance) name, and a factory's repository lookup is
        // name-only — but each factory owns its own repository, so every call here builds a genuinely
        // separate node rather than handing back the first one.
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        return await factory.GetScheduler();
    }

    protected static Task WaitForCondition(
        Func<Task<bool>> condition,
        int timeoutMs,
        string message)
    {
        return WaitForCondition(condition, timeoutMs, () => Task.FromResult(message));
    }

    /// <summary>
    /// Polls until the condition holds or the deadline passes, failing with the message the callback
    /// produces. The message is deferred, and may itself query the database, so that it can describe
    /// what the state actually was at the moment of failure — building it eagerly on every successful
    /// poll would be both wasteful and, for a database dump, wrong.
    /// </summary>
    protected static async Task WaitForCondition(
        Func<Task<bool>> condition,
        int timeoutMs,
        Func<Task<string>> message)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        DateTimeOffset deadline = start.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(200);
        }
        Assert.Fail($"Timed out after {(DateTimeOffset.UtcNow - start).TotalSeconds:F1} s (budget {timeoutMs} ms) waiting for condition: {await message()}");
    }

    /// <summary>
    /// Ages a node's cluster check-in by <paramref name="age"/>, which is how every test here reaches
    /// the state a peer's death produces. The alternative — sleeping until the real check-in falls past
    /// the misfire threshold — pays ten seconds of wall time for a row update, and still only asserts
    /// that a timer expired rather than that the row said what recovery needs it to say.
    /// </summary>
    /// <remarks>
    /// Only meaningful for a node that is no longer checking in: a live node overwrites the row on its
    /// next check-in and undoes this. <c>LAST_CHECKIN_TIME</c> holds <see cref="DateTimeOffset.UtcTicks"/>,
    /// not Unix milliseconds — see <c>StdAdoDelegate.GetDbDateTimeValue</c>, which is the schema contract.
    /// </remarks>
    protected async Task BackdateCheckin(string instanceId, TimeSpan age)
    {
        int updated = await ExecuteNonQuery(
            "UPDATE QRTZ_SCHEDULER_STATE SET LAST_CHECKIN_TIME = LAST_CHECKIN_TIME - @age " +
            "WHERE SCHED_NAME = @schedulerName AND INSTANCE_NAME = @instanceName",
            ("age", age.Ticks),
            ("schedulerName", SchedulerName),
            ("instanceName", instanceId));

        updated.Should().Be(1,
            "backdating is what makes '{0}' look dead, so a test whose instance id never matched a "
            + "SCHEDULER_STATE row would go on to wait for a recovery that is never triggered", instanceId);
    }

    /// <summary>
    /// Runs a statement against the store's own tables.
    /// </summary>
    protected async Task<int> ExecuteNonQuery(string sql, params (string Name, object Value)[] parameters)
    {
        using DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();
        using DbCommand command = CreateCommand(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Runs several statements, each on its own command, over one connection. Statements that a
    /// fixture wants run together but that no single command can carry on every engine.
    /// </summary>
    protected async Task ExecuteStatements(IReadOnlyList<string> statements, params (string Name, object Value)[] parameters)
    {
        using DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();

        foreach (string sql in statements)
        {
            using DbCommand command = CreateCommand(connection, sql, parameters);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Counts the rows a query matches, for asserting that recovery removed the residue it should have.
    /// </summary>
    protected async Task<int> CountRows(string sql, params (string Name, object Value)[] parameters)
    {
        using DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();
        using DbCommand command = CreateCommand(connection, sql, parameters);
        object result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Prepares a fixture's statement for this engine's driver, rewriting the <c>@name</c> placeholders
    /// every fixture writes into whatever the driver spells them with.
    /// </summary>
    /// <remarks>
    /// The longest names go first, so that a rewrite cannot chop the head off a longer placeholder that
    /// happens to start with a shorter one's name.
    /// </remarks>
    private DbCommand CreateCommand(DbConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();

        string prefix = Database.ParameterPrefix;
        if (prefix != "@")
        {
            foreach ((string name, _) in parameters.OrderByDescending(x => x.Name.Length))
            {
                sql = sql.Replace("@" + name, prefix + name, StringComparison.Ordinal);
            }
        }

        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    /// <summary>
    /// Returns a snapshot of this scheduler's trigger, scheduler-state, and fired-trigger
    /// rows for diagnosing failed cluster assertions. The rows are formatted here rather than by the
    /// database, so that the same query text works on every engine.
    /// </summary>
    protected async Task<string> DumpDatabaseState()
    {
        var result = new StringBuilder();

        using DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();

        await AppendRows(
            "SELECT TRIGGER_NAME, TRIGGER_STATE, PREFERRED_NODE, PREFERRED_NODE_AUTO, EXECUTION_GROUP, NEXT_FIRE_TIME "
            + "FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName",
            reader => $"TRIGGER: {reader.GetString(0)} state={reader.GetString(1)} "
                      + $"pin={Text(reader, 2)} auto={Text(reader, 3)} group={Text(reader, 4)} next={Number(reader, 5)}");

        await AppendRows(
            "SELECT INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL FROM QRTZ_SCHEDULER_STATE WHERE SCHED_NAME = @schedulerName",
            reader => $"STATE: {reader.GetString(0)} lastCheckin={Number(reader, 1)} interval={Number(reader, 2)}");

        await AppendRows(
            "SELECT TRIGGER_NAME, INSTANCE_NAME, STATE, ENTRY_ID FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName",
            reader => $"FIRED: {reader.GetString(0)} instance={reader.GetString(1)} state={reader.GetString(2)} entry={reader.GetString(3)}");

        return result.Length > 0 ? result.ToString() : "<no rows>";

        async Task AppendRows(string sql, Func<DbDataReader, string> format)
        {
            using DbCommand command = CreateCommand(connection, sql, [("schedulerName", SchedulerName)]);
            using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.AppendLine(format(reader));
            }
        }

        // Read through the boxed value rather than a typed accessor: a boolean is a `bit` on SQL Server,
        // a `boolean` on MySQL, a `VARCHAR2(1)` on Oracle and a `SMALLINT` on Firebird, and a big number
        // is `BIGINT` on four engines and `NUMBER` — which comes back a decimal — on Oracle. This is a
        // diagnostic, and one that threw while explaining a failure would replace the explanation.
        static string Text(DbDataReader reader, int ordinal)
            => reader.IsDBNull(ordinal) ? "<null>" : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

        static string Number(DbDataReader reader, int ordinal)
            => reader.IsDBNull(ordinal) ? "<null>" : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }

    protected static async Task WaitForTriggerCompletion(
        IScheduler scheduler,
        TriggerKey triggerKey,
        int timeoutMs)
    {
        await WaitForCondition(
            async () =>
            {
                var state = await scheduler.GetTriggerState(triggerKey);
                return state is TriggerState.Complete or TriggerState.None;
            },
            timeoutMs,
            $"trigger {triggerKey} to complete");
    }

    protected static async Task WaitForExecutionCount(int count, int timeoutMs)
    {
        await WaitForCondition(
            () => Task.FromResult(RecordingJob.Executions.Count >= count),
            timeoutMs,
            $"at least {count} execution(s)");
    }

    /// <summary>
    /// Job that records which scheduler instance executed it, proving placement.
    /// Thread-safe via <see cref="ConcurrentQueue{T}"/>.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class RecordingJob : IJob
    {
        private static volatile ConcurrentQueue<string> executions = new();

        public static ConcurrentQueue<string> Executions => executions;

        public static void Reset() => Interlocked.Exchange(ref executions, new ConcurrentQueue<string>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executions.Enqueue(context.Scheduler.SchedulerInstanceId);
            return default;
        }
    }
}
