using System.Globalization;

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

    public static void Run()
    {
        RunCore().GetAwaiter().GetResult();
    }

    private static async Task RunCore()
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"S4 recurring accuracy: {Schedules} one-second schedules per engine, {window.TotalSeconds:F0} s window, MaxConcurrency {Harness.MaxConcurrency}."));
        Console.WriteLine();
        Console.WriteLine("| Engine                          |   Fired | Expected | ±50 ms  | ±250 ms |  Max (ms) | Last (ms) |");
        Console.WriteLine("|---------------------------------|--------:|---------:|--------:|--------:|----------:|----------:|");

        foreach ((string label, Func<IEngine> create) in Arms())
        {
            await Measure(label, create).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine("\"Last\" is the deviation of the final firing in the window, which is where drift shows.");
    }

    private static async Task Measure(string label, Func<IEngine> create)
    {
        IEngine engine = create();

        try
        {
            await engine.Start(Harness.MaxConcurrency).ConfigureAwait(false);
            await engine.ScheduleRecurring(Schedules).ConfigureAwait(false);

            DateTimeOffset open = QuartzEngine.NextSecondBoundary() + settle;
            Harness.WaitUntil(open);

            Recurring.Reset();
            Recurring.Capturing = true;

            Harness.WaitUntil(open + window);

            Recurring.Capturing = false;
            Console.WriteLine(Recurring.Summarise(Schedules, window).Describe(label));
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
}
