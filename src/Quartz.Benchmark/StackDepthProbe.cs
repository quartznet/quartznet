using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Quartz.Benchmark;

/// <summary>
/// How deep the stack is when a fired job's <c>Execute</c> is entered: the frames on the thread's stack,
/// read with <see cref="StackTrace" /> inside the job on a scheduler that fires as fast as it can (#4214).
/// </summary>
/// <remarks>
/// <para>
/// Runtime async changes what an <c>await</c> costs, and part of that is frames. A compiler state machine
/// puts a stub, the builder's <c>Start</c> and <c>MoveNext</c> on the stack for each async method between
/// the dispatch and the job, where runtime async puts the method alone. Walking the stack costs far more
/// than a firing, and this run times nothing, so it reports the shape of the fire path and not its cost.
/// </para>
/// <para>
/// The scheduler is <see cref="FireThroughputBenchmark" />'s at <c>MaxConcurrency</c> 10 and a batch of
/// one, from <see cref="FireThroughput.StartScheduler" />, with a probe job's hundred triggers beside one
/// no-op trigger, each repeating every millisecond and always overdue. With <c>QUARTZ_BENCHMARK_POSTGRES</c> set the
/// run is repeated against PostgreSQL at <see cref="FireThroughputPostgresBenchmark" />'s settings.
/// </para>
/// </remarks>
internal static class StackDepthProbe
{
    /// <summary>Firings recorded and thrown away first, so that every path has been taken and jitted.</summary>
    private const int WarmupFirings = 500;

    /// <summary>How many firings the depths are reported over.</summary>
    private const int Samples = 2_000;

    /// <summary>How many triggers the probe job has.</summary>
    private const int Triggers = 100;

    private const string SchedulerName = nameof(StackDepthProbe);

    private static readonly TimeSpan timeout = TimeSpan.FromMinutes(2);

    private static readonly ConcurrentDictionary<int, string> traces = new();

    private static int[] depths = [];
    private static int started;
    private static int written;
    private static ManualResetEventSlim done = new(false);

    /// <summary>Measures <c>RAMJobStore</c>, then PostgreSQL when it is configured.</summary>
    public static void Run()
    {
        Measure("RAMJobStore", quartz => quartz.UseInMemoryStore(), FireBatching.One, database: null).GetAwaiter().GetResult();

        if (Environment.GetEnvironmentVariable("QUARTZ_BENCHMARK_POSTGRES") is null)
        {
            return;
        }

        BenchmarkDatabase database = BenchmarkDatabase.Open(BenchmarkDialect.Postgres).GetAwaiter().GetResult();
        try
        {
            string connectionString = database.Provider.ConnectionString;
            string providerName = database.ProviderName;

            Measure("PostgreSQL", quartz => quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.UseSystemTextJsonSerializer();
                RoundTripLatency.Inject(store, providerName, connectionString);
                LockFreeRoundsArm.Apply(store);
            }), batching: null, database).GetAwaiter().GetResult();
        }
        finally
        {
            database.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task Measure(string store, Action<IQuartzBuilder> configureStore, FireBatching? batching, BenchmarkDatabase? database)
    {
        depths = new int[Samples];
        started = 0;
        written = 0;
        traces.Clear();
        done = new ManualResetEventSlim(false);

        if (database is not null)
        {
            await OneOffCensus.ClearRows(database, SchedulerName).ConfigureAwait(false);
        }

        // One no-op trigger, which is all StartScheduler needs to see the scheduler firing. More would starve
        // the probe on PostgreSQL: a store that fires fewer than a thousand times a second per trigger leaves
        // the triggers scheduled first ever further behind the clock, so they are always acquired before the
        // probe's, and a round of ten would never reach them. One of them takes one place in a round.
        IScheduler scheduler = await FireThroughput.StartScheduler(
            instanceName: SchedulerName,
            maxConcurrency: 10,
            configureStore: configureStore,
            jobCount: 1,
            batching: batching,
            triggerCount: 1).ConfigureAwait(false);

        try
        {
            IJobDetail job = JobBuilder.Create<ProbeJob>().WithIdentity("stack-depth-probe", FireThroughput.Group).Build();
            ITrigger[] triggers = new ITrigger[Triggers];
            for (int i = 0; i < triggers.Length; i++)
            {
                triggers[i] = TriggerBuilder.Create()
                    .WithIdentity(string.Create(CultureInfo.InvariantCulture, $"stack-depth-probe-{i}"), FireThroughput.Group)
                    .ForJob(job)
                    .StartNow()
                    .WithSimpleSchedule(simple => simple
                        .RepeatForever()
                        .WithInterval(TimeSpan.FromMilliseconds(1))
                        .WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires))
                    .Build();
            }

            await scheduler.ScheduleJob(job, triggers).ConfigureAwait(false);

            if (!done.Wait(timeout))
            {
                throw new InvalidOperationException($"Waited {timeout} for {WarmupFirings + Samples} probe firings and saw {Volatile.Read(ref started)}.");
            }
        }
        finally
        {
            await FireThroughput.StopScheduler(scheduler).ConfigureAwait(false);

            if (database is not null)
            {
                await OneOffCensus.ClearRows(database, SchedulerName).ConfigureAwait(false);
            }
        }

        Report(store);
    }

    private static void Report(string store)
    {
        int[] sorted = [.. depths.Order()];

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Frames on the stack at IJob.Execute, {store}, {Samples:N0} firings after {WarmupFirings:N0} thrown away: "
            + $"min {sorted[0]}, p50 {sorted[Samples / 2]}, p99 {sorted[Samples * 99 / 100]}, max {sorted[^1]}"));
        Console.WriteLine();
        Console.WriteLine("| Frames | Firings |");
        Console.WriteLine("|-------:|--------:|");
        foreach (IGrouping<int, int> group in sorted.GroupBy(x => x))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {group.Key,6} | {group.Count(),7:N0} |"));
        }

        // A firing whose fire path completed synchronously enters the job on the dispatching thread's stack;
        // one that suspended on the way enters it from a continuation, which is shallower. Each depth seen is
        // printed once, so the difference can be read rather than inferred.
        foreach (int depth in sorted.Distinct())
        {
            Console.WriteLine();
            Console.WriteLine($"The stack at {depth} frames:");
            Console.WriteLine(traces[depth]);
        }
    }

    /// <summary>Records the depth its <c>Execute</c> was entered at, and the stack once for each depth seen.</summary>
    private sealed class ProbeJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            int index = Interlocked.Increment(ref started) - 1;
            if (index >= WarmupFirings + Samples)
            {
                return default;
            }

            StackTrace trace = new(fNeedFileInfo: false);
            if (index < WarmupFirings)
            {
                return default;
            }

            int depth = trace.FrameCount;
            depths[index - WarmupFirings] = depth;
            if (!traces.ContainsKey(depth))
            {
                traces.TryAdd(depth, trace.ToString());
            }

            if (Interlocked.Increment(ref written) == Samples)
            {
                done.Set();
            }

            return default;
        }
    }
}
