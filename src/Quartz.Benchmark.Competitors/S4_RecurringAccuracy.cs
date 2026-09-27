using System.Globalization;

using Quartz.Benchmark.Competitors.Database;
using Quartz.Benchmark.Competitors.Engines;

namespace Quartz.Benchmark.Competitors;

/// <summary>
/// S4 — a hundred one-second schedules for a minute, and how close to the second each firing landed.
/// </summary>
/// <remarks>
/// <para>
/// Not a BenchmarkDotNet benchmark, because what it measures is not how long a call took: it is
/// whether a schedule that says "every second" produces a firing every second. BenchmarkDotNet's model
/// has nothing to say about that, and a harness that tried to express it would be measuring its own
/// waiting.
/// </para>
/// <para>
/// Each engine gets a hundred schedules, a settling period, and then a sixty-second capture window.
/// Every firing records what the engine says it was scheduled for and when it actually ran; the row
/// reports how many of those landed within 50 ms and 250 ms, the worst deviation, the last one's
/// deviation — which is where drift shows — and how many firings there were against the hundred times
/// sixty that were asked for.
/// </para>
/// <para>
/// <b>Hangfire's row measures something slightly different, and it has to.</b> A recurring job is
/// turned into an ordinary background job, and under the default <c>MisfireHandlingMode.Relaxed</c>
/// every missed occurrence is rewritten to the current instant before the job is created, so there is
/// no scheduled instant left for the job to be late for. Its deviations are therefore measured against
/// the nearest whole second. Hangfire does honour a six-field expression — it hands one to Cronos with
/// <c>CronFormat.IncludeSeconds</c> — so this is not the minute-granularity row #3802 allowed for; what
/// bounds it is the poll, which is set as low as it goes here.
/// </para>
/// </remarks>
internal static class S4RecurringAccuracy
{
    private const int Schedules = 100;

    private static readonly TimeSpan window = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long each engine is left alone after scheduling before the capture window opens.
    /// </summary>
    private static readonly TimeSpan settle = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The lighter load the PostgreSQL run adds, below what one node fires a second at one trigger a
    /// round, so that its rows say how punctual a store is that keeps up rather than how far behind one
    /// falls that cannot.
    /// </summary>
    private const int LightSchedules = 20;

    public static void Run()
    {
        RunCore(Arms(), [Schedules]).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The same question against Quartz's ADO store on PostgreSQL, one trigger a round against the
    /// automatic batch, at the S4 load and at a lighter one.
    /// </summary>
    /// <remarks>
    /// A run of its own rather than rows in <see cref="Run" />, because it needs a database and because
    /// a hundred one-second schedules is more than a node fires a second on this machine's PostgreSQL at
    /// one trigger a round (#3824 measured 87.6), so at that load the rows measure falling behind as
    /// much as punctuality. #3862 is the reason it exists: that default changed on this store.
    /// </remarks>
    public static void RunPostgres()
    {
        string connectionString = PostgresFixture.ConnectionString;
        RunCore(PostgresArms(connectionString), [Schedules, LightSchedules]).GetAwaiter().GetResult();
    }

    private static async Task RunCore(IEnumerable<(string Label, Func<IEngine> Create)> arms, int[] loads)
    {
        List<(string Label, Func<IEngine> Create)> chosen = [.. arms];

        foreach (int schedules in loads)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"S4 recurring accuracy: {schedules} one-second schedules per engine, {window.TotalSeconds:F0} s window, MaxConcurrency {Harness.MaxConcurrency}."));
            Console.WriteLine();
            Console.WriteLine("| Engine                          |   Fired | Expected | ±50 ms  | ±250 ms |  Max (ms) | Last (ms) |");
            Console.WriteLine("|---------------------------------|--------:|---------:|--------:|--------:|----------:|----------:|");

            foreach ((string label, Func<IEngine> create) in chosen)
            {
                await Measure(label, create, schedules).ConfigureAwait(false);
            }

            Console.WriteLine();
        }

        Console.WriteLine("\"Last\" is the deviation of the final firing in the window, which is where drift shows.");
    }

    private static async Task Measure(string label, Func<IEngine> create, int schedules)
    {
        IEngine engine = create();

        try
        {
            await engine.Start(Harness.MaxConcurrency).ConfigureAwait(false);
            await engine.ScheduleRecurring(schedules).ConfigureAwait(false);

            DateTimeOffset open = QuartzEngine.NextSecondBoundary() + settle;
            Harness.WaitUntil(open);

            Recurring.Reset();
            Recurring.Capturing = true;

            Harness.WaitUntil(open + window);

            Recurring.Capturing = false;
            Console.WriteLine(Recurring.Summarise(schedules, window).Describe(label));
        }
        finally
        {
            Recurring.Capturing = false;
            await engine.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static IEnumerable<(string Label, Func<IEngine> Create)> Arms()
    {
        yield return ("Quartz (simple, defaults)", static () => new QuartzEngine(
            QuartzProfile.Defaults, "S4Simple", quartz => quartz.UseInMemoryStore(), QuartzRecurringKind.Simple));

        yield return ("Quartz (cron, defaults)", static () => new QuartzEngine(
            QuartzProfile.Defaults, "S4Cron", quartz => quartz.UseInMemoryStore(), QuartzRecurringKind.Cron));

        // The throughput rows' settings, run against the accuracy question on purpose. The fire-ahead
        // window is what lets a batch hold more than the triggers due at one instant, and the way it
        // does that is by firing the later ones at the earliest one's time — so a second of window is a
        // second of "early" a punctual schedule does not want. Measured rather than asserted.
        yield return ("Quartz (simple, fire-ahead 1 s)", static () => new QuartzEngine(
            QuartzProfile.Tuned, "S4Tuned", quartz => quartz.UseInMemoryStore(), QuartzRecurringKind.Simple));

        yield return ("TickerQ (cron * * * * * *)", static () => new TickerQEngine(
            "TickerQ", TimeSpan.FromMilliseconds(100)));

        yield return ("Hangfire (cron * * * * * *)", static () => new HangfireEngine(
            "Hangfire", HangfireEngine.InMemory, TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// A hundred triggers due at the same second is a batch, and on a database a round is round trips,
    /// so whether a round takes one trigger or all of them is the punctuality question #3862's default
    /// changed the answer to. The explicit one is 4.2's default.
    /// </summary>
    private static IEnumerable<(string Label, Func<IEngine> Create)> PostgresArms(string connectionString)
    {
        yield return ("Quartz (simple, batch 1, PostgreSQL)", () => PostgresQuartz(connectionString, maxBatchSize: 1));
        yield return ("Quartz (simple, defaults, PostgreSQL)", () => PostgresQuartz(connectionString, maxBatchSize: null));
    }

    /// <summary>
    /// Quartz's ADO store on an emptied schema, at the defaults or at an explicit <c>MaxBatchSize</c>.
    /// </summary>
    private static QuartzEngine PostgresQuartz(string connectionString, int? maxBatchSize)
    {
        PostgresFixture fixture = PostgresFixture.Open().AsTask().GetAwaiter().GetResult();
        try
        {
            fixture.ResetQuartz().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            fixture.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return new QuartzEngine(
            QuartzProfile.Defaults,
            maxBatchSize is null ? "S4Postgres" : "S4PostgresBatchOfOne",
            quartz =>
            {
                if (maxBatchSize is { } explicitBatch)
                {
                    quartz.ConfigureScheduler(options => options.MaxBatchSize = explicitBatch);
                }

                quartz.UsePersistentStore(store =>
                {
                    store.UsePostgres(connectionString);
                    store.UseSystemTextJsonSerializer();
                });
            },
            QuartzRecurringKind.Simple);
    }
}
