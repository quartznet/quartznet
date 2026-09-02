using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

using Npgsql;

using Quartz.Impl;

namespace Quartz.Benchmark;

/// <summary>
/// The 3.x baseline for <c>FireThroughputBenchmark</c> and <c>FireThroughputPostgresBenchmark</c> on
/// <c>main</c>: the same workload, the same settings and the same arithmetic, written against 3.x's
/// API so the two numbers are a comparison rather than two measurements.
/// </summary>
/// <remarks>
/// <para>
/// <b>This file is not compiled here.</b> It lives under <c>src/Quartz.Benchmark/baseline-3x/</c> on
/// <c>main</c>, excluded by the project file, so the baseline half of a published comparison is
/// reproducible without anybody having to reconstruct it. To run it, drop it into
/// <c>src/Quartz.Benchmark/</c> of a <c>3.x</c> checkout with the two package references and the two
/// project references named in <c>README.md</c> beside it, and run the filter below. Nothing is
/// committed on <c>3.x</c>.
/// </para>
/// <para>
/// <b>What is held identical to the 4.x arms</b>, because a difference in any of them would be
/// measured as a difference in the scheduler: two hundred triggers over twenty-five jobs; simple
/// triggers repeating indefinitely at a one-millisecond interval under the ignore-misfires
/// instruction, so every trigger is permanently overdue and the loop never waits;
/// <c>MaxBatchSize</c> equal to
/// <c>MaxConcurrency</c>; a one-second batch fire-ahead window; a one-second idle wait; a job that
/// counts and returns. The counting, the waiting and the fires-per-invocation constants are the same
/// too, so <c>Mean</c> means the same thing on both sides — the time one firing took, from which
/// fires per second is <c>1e9 / Mean(ns)</c>.
/// </para>
/// <para>
/// <b>Three things differ, because 3.x is 3.x.</b> The store is <c>JobStoreTX</c> rather than
/// <c>LocalTransactionJobStore</c> (the same store under its 3.x name); the scheduler is built from
/// flat properties through <c>StdSchedulerFactory</c>, because that is 3.x's configuration surface;
/// and <c>IJob.Execute</c> returns <c>Task</c> and takes no cancellation token. None of the three is
/// on the fire path being measured.
/// </para>
/// <para>
/// <b>The PostgreSQL arm assumes the schema is already there.</b> 3.x's benchmark project has no
/// equivalent of 4.x's <c>BenchmarkDatabase</c>, and building one to apply <c>database/tables/</c>
/// would be a second implementation to keep honest. Point it at the container the 4.x run used, which
/// has the tables; this one only clears the rows its own scheduler name owns.
/// </para>
/// <code>
/// $env:QUARTZ_BENCHMARK_POSTGRES='Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
/// dotnet run -c Release --project src/Quartz.Benchmark -- --filter '*FireThroughput*'
/// </code>
/// </remarks>
internal static class FireThroughputBaseline
{
    public const int RamFiresPerInvocation = 2_000;

    public const int AdoFiresPerInvocation = 250;

    public const string Group = "fireThroughput";

    private const int TriggerCount = 2_000;

    private const int JobCount = 100;

    /// <summary>
    /// The same millisecond the 4.x arms use, and for the same reason: it is the smallest
    /// interval a persistent store can carry, because StdAdoDelegate stores a TimeSpan as whole
    /// milliseconds on both branches.
    /// </summary>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(1);

    private static readonly TimeSpan FireTimeout = TimeSpan.FromMinutes(2);

    private static long fireCount;

    private static volatile Waiter pending;

    /// <summary>Blocks until <paramref name="count" /> more fires have happened.</summary>
    public static void AwaitFires(int count)
    {
        Waiter waiter = new Waiter(Interlocked.Read(ref fireCount) + count);
        pending = waiter;

        if (Interlocked.Read(ref fireCount) >= waiter.Target)
        {
            waiter.Done.Set();
        }

        bool completed = waiter.Done.Wait(FireTimeout);
        long seen = count - (waiter.Target - Interlocked.Read(ref fireCount));
        pending = null;

        if (!completed)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                "Waited {0} for {1} firings and saw {2}. The scheduler stopped firing, which is a broken harness rather than a slow one.",
                FireTimeout, count, seen));
        }
    }

    private static void Fired()
    {
        long count = Interlocked.Increment(ref fireCount);
        Waiter waiter = pending;
        if (waiter != null && count >= waiter.Target)
        {
            waiter.Done.Set();
        }
    }

    /// <summary>
    /// Builds the scheduler from flat properties — 3.x's configuration surface — schedules the
    /// workload, starts it and waits until it is firing.
    /// </summary>
    public static async Task<IScheduler> StartScheduler(
        string instanceName,
        int maxConcurrency,
        Action<NameValueCollection> configureStore)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = instanceName,
            ["quartz.scheduler.instanceId"] = "NODE-01",
            ["quartz.scheduler.idleWaitTime"] = "1000",
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = maxConcurrency.ToString(CultureInfo.InvariantCulture),
            ["quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow"] = "1000",
            ["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.maxConcurrency"] = maxConcurrency.ToString(CultureInfo.InvariantCulture),
        };

        configureStore(properties);

        ISchedulerFactory factory = new StdSchedulerFactory(properties);
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);

        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> schedule = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();
        for (int i = 0; i < JobCount; i++)
        {
            IJobDetail job = JobBuilder.Create<NoOpCountingJob>()
                .WithIdentity("job-" + i.ToString(CultureInfo.InvariantCulture), Group)
                .Build();

            int triggersForThisJob = TriggerCount / JobCount;
            ITrigger[] triggers = new ITrigger[triggersForThisJob];
            for (int j = 0; j < triggersForThisJob; j++)
            {
                triggers[j] = TriggerBuilder.Create()
                    .WithIdentity(string.Format(CultureInfo.InvariantCulture, "trigger-{0}-{1}", i, j), Group)
                    .ForJob(job)
                    .StartNow()
                    .WithSimpleSchedule(simple => simple
                        .RepeatForever()
                        .WithInterval(RepeatInterval)
                        .WithMisfireHandlingInstructionIgnoreMisfires())
                    .Build();
            }

            schedule.Add(job, triggers);
        }

        await scheduler.ScheduleJobs(schedule, replace: true).ConfigureAwait(false);
        await scheduler.Start().ConfigureAwait(false);

        AwaitFires(TriggerCount);

        return scheduler;
    }

    public static Task StopScheduler(IScheduler scheduler) => scheduler.Shutdown(waitForJobsToComplete: false);

    /// <summary>The job under measurement: it records that it ran and returns.</summary>
    public class NoOpCountingJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            Fired();
            return Task.CompletedTask;
        }
    }

    private sealed class Waiter
    {
        public Waiter(long target)
        {
            Target = target;
        }

        public long Target { get; }

        public ManualResetEventSlim Done { get; } = new ManualResetEventSlim(false);
    }
}

/// <summary>3.x's RAMJobStore arm, matching <c>FireThroughputBenchmark</c> on <c>main</c>.</summary>
[MemoryDiagnoser]
public class FireThroughputBaselineBenchmark
{
    [Params(10, 50)]
    public int MaxConcurrency { get; set; }

    private IScheduler scheduler;

    [GlobalSetup]
    public async Task Setup()
    {
        scheduler = await FireThroughputBaseline.StartScheduler(
            "RamThroughputBenchmark",
            MaxConcurrency,
            properties => properties["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await FireThroughputBaseline.StopScheduler(scheduler).ConfigureAwait(false);
    }

    [Benchmark(OperationsPerInvoke = FireThroughputBaseline.RamFiresPerInvocation)]
    public void Fire() => FireThroughputBaseline.AwaitFires(FireThroughputBaseline.RamFiresPerInvocation);
}

/// <summary>
/// 3.x's PostgreSQL arm, matching <c>FireThroughputPostgresBenchmark</c> on <c>main</c>. Single node,
/// not clustered, for the same reason: what is measured is the fire path rather than the check-in
/// loop.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
public class FireThroughputBaselinePostgresBenchmark
{
    private const string SchedulerName = "PostgresThroughputBenchmark";

    [Params(10, 50)]
    public int MaxConcurrency { get; set; }

    private string connectionString;
    private IScheduler scheduler;

    [GlobalSetup]
    public async Task Setup()
    {
        connectionString = Environment.GetEnvironmentVariable("QUARTZ_BENCHMARK_POSTGRES")
            ?? throw new InvalidOperationException("QUARTZ_BENCHMARK_POSTGRES is not set; point it at the same database the 4.x run used, which already has the schema.");

        await ClearOwnRows().ConfigureAwait(false);

        scheduler = await FireThroughputBaseline.StartScheduler(
            SchedulerName,
            MaxConcurrency,
            properties =>
            {
                properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
                properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";
                properties["quartz.jobStore.dataSource"] = "default";
                properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
                properties["quartz.dataSource.default.provider"] = "Npgsql";
                properties["quartz.dataSource.default.connectionString"] = connectionString;
                properties["quartz.serializer.type"] = "stj";
            }).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await FireThroughputBaseline.StopScheduler(scheduler).ConfigureAwait(false);
        await ClearOwnRows().ConfigureAwait(false);
    }

    [Benchmark(OperationsPerInvoke = FireThroughputBaseline.AdoFiresPerInvocation)]
    public void Fire() => FireThroughputBaseline.AwaitFires(FireThroughputBaseline.AdoFiresPerInvocation);

    private async Task ClearOwnRows()
    {
        string[] tables =
        {
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
        };

        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        foreach (string table in tables)
        {
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM " + table + " WHERE SCHED_NAME = @schedulerName";
            command.Parameters.AddWithValue("schedulerName", SchedulerName);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
