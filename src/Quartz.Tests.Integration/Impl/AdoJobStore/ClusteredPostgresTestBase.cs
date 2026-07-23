using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

using Quartz.Impl;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Reusable base class for PostgreSQL-backed clustered integration tests.
/// Provides scheduler creation, job execution recording, and polling helpers.
/// Uses the assembly-wide PostgreSQL database (started once by TestAssemblySetup,
/// connection via <see cref="TestConstants.PostgresConnectionString"/>); it does not
/// provision its own container. All derived fixtures share that single database, with
/// <see cref="SchedulerName"/> as the only isolation axis — derived fixtures must use
/// a unique scheduler name and be marked <c>[NonParallelizable]</c> because they also
/// share the static <see cref="RecordingJob.Executions"/> queue.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public abstract class ClusteredPostgresTestBase
{
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
        using NpgsqlConnection connection = new NpgsqlConnection(TestConstants.PostgresConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM qrtz_fired_triggers WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_simple_triggers WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_cron_triggers WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_simprop_triggers WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_blob_triggers WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_triggers WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_job_details WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_calendars WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_paused_trigger_grps WHERE sched_name = @sched;" +
            "DELETE FROM qrtz_scheduler_state WHERE sched_name = @sched;";
        command.Parameters.AddWithValue("sched", SchedulerName);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    protected async Task<IScheduler> CreateScheduler(
        string instanceId,
        int checkinIntervalMs = 1000,
        int checkinMisfireThresholdMs = 2000,
        Action<NameValueCollection> configure = null)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = SchedulerName,
            ["quartz.scheduler.instanceId"] = instanceId,
            // Short idle wait so nodes notice remote changes (re-pins, failover resets)
            // within seconds instead of the 30 s default acquisition cycle
            ["quartz.scheduler.idleWaitTime"] = "2000",
            ["quartz.threadPool.threadCount"] = "2",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz",
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.clusterCheckinInterval"] = checkinIntervalMs.ToString(),
            ["quartz.jobStore.clusterCheckinMisfireThreshold"] = checkinMisfireThresholdMs.ToString(),
            ["quartz.dataSource.default.provider"] = TestConstants.PostgresProvider,
            ["quartz.dataSource.default.connectionString"] = TestConstants.PostgresConnectionString,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        configure?.Invoke(properties);

        StdSchedulerFactory factory = new StdSchedulerFactory(properties);
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);

        // Cluster nodes share the scheduler (instance) name, but StdSchedulerFactory's
        // non-proxy repository lookup is name-only: a second CreateScheduler call would
        // silently return the first, still-running node instead of creating a new one —
        // collapsing multi-node tests to a single node. Unbind each node right away so
        // every call builds a genuinely separate scheduler.
        SchedulerRepository.Instance.Remove(SchedulerName, scheduler.SchedulerInstanceId);

        return scheduler;
    }

    protected static async Task WaitForCondition(
        Func<Task<bool>> condition,
        int timeoutMs,
        string message)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }
            await Task.Delay(200).ConfigureAwait(false);
        }
        Assert.Fail($"Timed out waiting for condition: {message}");
    }

    /// <summary>
    /// Returns a snapshot of this scheduler's trigger, scheduler-state, and fired-trigger
    /// rows for diagnosing failed cluster assertions.
    /// </summary>
    protected async Task<string> DumpDatabaseState()
    {
        using NpgsqlConnection connection = new NpgsqlConnection(TestConstants.PostgresConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT 'TRIGGER: ' || trigger_name || ' state=' || trigger_state || ' pin=' || COALESCE(preferred_node, '<null>') || ' group=' || COALESCE(execution_group, '<null>') || ' next=' || next_fire_time FROM qrtz_triggers WHERE sched_name = @sched " +
            "UNION ALL SELECT 'STATE: ' || instance_name || ' lastCheckin=' || last_checkin_time FROM qrtz_scheduler_state WHERE sched_name = @sched " +
            "UNION ALL SELECT 'FIRED: ' || trigger_name || ' instance=' || instance_name || ' state=' || state FROM qrtz_fired_triggers WHERE sched_name = @sched";
        command.Parameters.AddWithValue("sched", SchedulerName);
        System.Text.StringBuilder result = new System.Text.StringBuilder();
        using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.AppendLine(reader.GetString(0));
            }
        }
        return result.Length > 0 ? result.ToString() : "<no rows>";
    }

    protected static async Task WaitForTriggerCompletion(
        IScheduler scheduler,
        TriggerKey triggerKey,
        int timeoutMs)
    {
        await WaitForCondition(
            async () =>
            {
                TriggerState state = await scheduler.GetTriggerState(triggerKey).ConfigureAwait(false);
                return state is TriggerState.Complete or TriggerState.None;
            },
            timeoutMs,
            $"trigger {triggerKey} to complete").ConfigureAwait(false);
    }

    protected static async Task WaitForExecutionCount(int count, int timeoutMs)
    {
        await WaitForCondition(
            () => Task.FromResult(RecordingJob.Executions.Count >= count),
            timeoutMs,
            $"at least {count} execution(s)").ConfigureAwait(false);
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

        public Task Execute(IJobExecutionContext context)
        {
            Executions.Enqueue(context.Scheduler.SchedulerInstanceId);
            return Task.CompletedTask;
        }
    }
}
