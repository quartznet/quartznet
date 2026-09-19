using System.Globalization;

using Hangfire;
using Hangfire.InMemory;

namespace Quartz.Benchmark.Competitors.Engines;

/// <summary>
/// Hangfire 1.8.25, over the storage the scenario names.
/// </summary>
/// <remarks>
/// <para>
/// <b>What one firing does.</b> A scheduled job sits in a sorted set until the
/// <c>DelayedJobScheduler</c> — which polls at <c>SchedulePollingInterval</c> — moves it to the
/// <c>Enqueued</c> state and puts its id on a queue. A worker fetches the id, reads the job's
/// <c>InvocationData</c> back out of storage and deserialises the method and its arguments,
/// transitions the job to <c>Processing</c>, runs the filter pipeline, activates the type through the
/// <c>JobActivator</c> (nothing to activate for a static method), invokes it through reflection, then
/// transitions to <c>Succeeded</c>. Every state transition is a storage write with a state-history
/// entry. Hangfire has no separate scheduling thread: the poll and the workers are all
/// <c>BackgroundProcess</c>es on one server.
/// </para>
/// <para>
/// <b>Two rows for the same work.</b> A job created with <c>Enqueue</c> skips the delayed-job poll
/// entirely and goes straight onto the queue, so it is the shape a reader should compare against
/// TickerQ's immediate-dispatch path rather than against either scheduler's loop. Both are published.
/// </para>
/// <para>
/// The engine uses <see cref="IBackgroundJobClient" /> and <see cref="RecurringJobManager" /> rather
/// than the static <c>BackgroundJob</c> / <c>RecurringJob</c> facades. Those facades are a
/// <c>JobStorage.Current</c> lookup in front of exactly these calls, and their client is cached in a
/// <c>Lazy</c> that binds the first storage it sees — which would quietly make every iteration after
/// the first write into the previous iteration's store.
/// </para>
/// </remarks>
internal sealed class HangfireEngine : IEngine
{
    private readonly Func<JobStorage> createStorage;
    private readonly TimeSpan schedulePollingInterval;
    private readonly bool startServerOnStart;
    private readonly bool enqueueRatherThanSchedule;
    private readonly List<string> recurringIds = [];

    private JobStorage? storage;
    private BackgroundJobServer? server;
    private IBackgroundJobClient? client;
    private RecurringJobManager? recurring;
    private int maxConcurrency;

    public HangfireEngine(
        string name,
        Func<JobStorage> createStorage,
        TimeSpan schedulePollingInterval,
        bool startServerOnStart = true,
        bool enqueueRatherThanSchedule = false)
    {
        Name = name;
        this.createStorage = createStorage;
        this.schedulePollingInterval = schedulePollingInterval;
        this.startServerOnStart = startServerOnStart;
        this.enqueueRatherThanSchedule = enqueueRatherThanSchedule;
    }

    public string Name { get; }

    /// <summary>The storage a scenario needs for its own bookkeeping.</summary>
    public JobStorage Storage => storage ?? throw new InvalidOperationException("Start has not been called.");

    private IBackgroundJobClient Client => client ?? throw new InvalidOperationException("Start has not been called.");

    public ValueTask Start(int maxConcurrency, CancellationToken cancellationToken = default)
    {
        this.maxConcurrency = maxConcurrency;

        storage = createStorage();

        // Some Hangfire internals resolve the ambient storage; the client and the server are both given
        // theirs explicitly, so this is belt and braces rather than the wiring.
        JobStorage.Current = storage;

        client = new BackgroundJobClient(storage);
        recurring = new RecurringJobManager(storage);

        if (startServerOnStart)
        {
            StartServer();
        }

        return default;
    }

    /// <summary>
    /// Starts the processing server.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Start" /> for the <c>Enqueue</c> row alone: an enqueued job is
    /// available the instant it is created, so a server that was already running would drain most of
    /// the batch while the batch was still being written — inside the iteration setup, where nothing is
    /// measured. The row therefore enqueues into a storage with no server and starts one inside the
    /// measured window, which costs it the server's startup. That is stated beside the number.
    /// </remarks>
    public void StartServer()
    {
        server = new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                // The default is Environment.ProcessorCount * 5, which on this machine is 160. Set to
                // the same worker limit as the other two so the row is about the scheduler.
                WorkerCount = maxConcurrency,
                SchedulePollingInterval = schedulePollingInterval,
                Queues = ["default"],
                ShutdownTimeout = TimeSpan.FromSeconds(15),
                ServerName = "competitors",
            },
            Storage);
    }

    /// <summary>
    /// Creates <paramref name="count" /> background jobs, one call each.
    /// </summary>
    /// <remarks>
    /// <b>Written concurrently, and that is not a measurement choice.</b> Hangfire has no batch
    /// creation API — the other two do, and are given theirs — so two thousand jobs against PostgreSQL
    /// is two thousand round trips, which took about sixteen seconds sequentially and made the lead
    /// time the harness needs longer than the work it is leading. Scheduling happens in the iteration
    /// setup and is not inside any measured window, so how fast it is changes nothing about the number;
    /// what it changes is how long the run takes. Eight at a time, which is inside the connection
    /// pool's default.
    /// </remarks>
    public ValueTask ScheduleOneOff(int count, DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        ParallelOptions options = new() { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken };

        if (enqueueRatherThanSchedule)
        {
            Parallel.For(0, count, options, _ => Client.Enqueue(() => HangfireJobs.Count()));
        }
        else
        {
            Parallel.For(0, count, options, _ => Client.Schedule(() => HangfireJobs.Count(), dueAt));
        }

        return default;
    }

    public ValueTask ScheduleRecurring(int count, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < count; i++)
        {
            string id = "recurring-" + i.ToString(CultureInfo.InvariantCulture);

            // Six fields: Hangfire hands a six-field expression to Cronos with
            // CronFormat.IncludeSeconds (RecurringJobEntity.ParseCronExpression), so seconds are
            // honoured. What the poll does with them is the finding this row reports.
            recurring!.AddOrUpdate(id, Hangfire.Common.Job.FromExpression(() => HangfireJobs.Count()), "* * * * * *", new RecurringJobOptions());
            recurringIds.Add(id);
        }

        return default;
    }

    /// <summary>
    /// One schedule through the call <c>BackgroundJob.Schedule</c> and <c>RecurringJob.AddOrUpdate</c>
    /// delegate to, which is what the per-schedule cost scenario measures.
    /// </summary>
    public void ScheduleOne(int index, bool recurringJob, DateTimeOffset dueAt)
    {
        if (recurringJob)
        {
            recurring!.AddOrUpdate(
                "cost-" + index.ToString(CultureInfo.InvariantCulture),
                Hangfire.Common.Job.FromExpression(() => HangfireJobs.Count()),
                "0 12 * * *",
                new RecurringJobOptions());
        }
        else
        {
            Client.Schedule(() => HangfireJobs.Count(), dueAt);
        }
    }

    /// <summary>
    /// One job for right now, either straight onto the queue or through the delayed set with a zero
    /// delay. Both rows are published: the difference between them is Hangfire's scheduler poll.
    /// </summary>
    public void ScheduleNow(bool throughTheScheduler)
    {
        if (throughTheScheduler)
        {
            Client.Schedule(() => HangfireJobs.Count(), TimeSpan.Zero);
        }
        else
        {
            Client.Enqueue(() => HangfireJobs.Count());
        }
    }

    /// <summary>
    /// Drops the recurring jobs and replaces the storage, which is the only clear an in-memory Hangfire
    /// has: there is no "delete every background job" on <see cref="JobStorage" />, and a finished job
    /// lives on until <c>MaxExpirationTime</c> takes it.
    /// </summary>
    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        foreach (string id in recurringIds)
        {
            recurring!.RemoveIfExists(id);
        }

        recurringIds.Clear();
        return default;
    }

    public ValueTask DisposeAsync()
    {
        if (server is not null)
        {
            server.SendStop();
            server.Dispose();
            server = null;
        }

        (storage as IDisposable)?.Dispose();
        storage = null;
        client = null;
        recurring = null;

        return default;
    }

    /// <summary>
    /// The shipped in-memory storage, at its own defaults.
    /// </summary>
    /// <remarks>
    /// Including <c>MaxExpirationTime</c>, which is three hours: #3802 expected that to need lowering so
    /// that one iteration's finished jobs were gone before the next began, but every throughput scenario
    /// here builds a new engine per iteration and a new engine gets a new
    /// <see cref="InMemoryStorage" />, so there is nothing to carry over and no reason to move the
    /// setting. The two settings that are not Hangfire's defaults are the worker count and the poll
    /// interval, and both are stated.
    /// </remarks>
    public static JobStorage InMemory() => new InMemoryStorage();
}

/// <summary>The Hangfire side of the counting job.</summary>
/// <remarks>
/// Static and parameterless, which is the cheapest job Hangfire can be given: nothing to activate and
/// no arguments to serialise or deserialise.
/// </remarks>
public static class HangfireJobs
{
    public static void Count()
    {
        Completion.Record();
        Recurring.Record(scheduledFor: null);
    }
}
