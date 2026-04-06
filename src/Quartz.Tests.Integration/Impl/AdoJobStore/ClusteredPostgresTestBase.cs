using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Reusable base class for PostgreSQL-backed clustered integration tests.
/// Provides scheduler creation, job execution recording, and polling helpers.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public abstract class ClusteredPostgresTestBase
{
    protected virtual string SchedulerName => "ClusteredTest";

    [SetUp]
    public void ResetRecordingJob() => RecordingJob.Reset();

    protected Task<IScheduler> CreateScheduler(
        string instanceId,
        int checkinIntervalMs = 1000,
        int checkinMisfireThresholdMs = 2000)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = SchedulerName,
            ["quartz.scheduler.instanceId"] = instanceId,
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

        StdSchedulerFactory factory = new StdSchedulerFactory(properties);
        return factory.GetScheduler();
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
