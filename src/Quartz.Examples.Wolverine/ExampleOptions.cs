namespace Quartz.Examples.Wolverine;

/// <summary>
/// The few knobs that differ between a plain <c>dotnet run</c>, the <c>--smoke</c> run that CI could
/// invoke, and the Postgres mode.
/// </summary>
/// <param name="Smoke">
/// Whether to run every part on a compressed clock, assert each one happened, and exit.
/// </param>
/// <param name="ReconciliationCron">
/// The cron expression part 1 registers. <see cref="Part1RecurringPublishing.NightlyCron" /> is what a
/// deployment would use; the smoke run needs something that fires while it is watching.
/// </param>
/// <param name="ReminderDelay">
/// How far ahead part 2 schedules its follow-up. Days in a real application, seconds here.
/// </param>
/// <param name="PostgresConnectionString">
/// Set from <c>QUARTZ_WOLVERINE_POSTGRES</c>. When it is absent — which is the case on every CI leg —
/// parts 5 and 6 fall back to the forms that need no database, and say so.
/// </param>
public sealed record ExampleOptions(
    bool Smoke,
    string ReconciliationCron,
    TimeSpan ReminderDelay,
    string? PostgresConnectionString)
{
    /// <summary>
    /// The environment variable that turns the durable half of the example on.
    /// </summary>
    public const string PostgresVariable = "QUARTZ_WOLVERINE_POSTGRES";

    /// <summary>
    /// The options the running process was started with.
    /// </summary>
    /// <remarks>
    /// A static, for the reason <see cref="Ledger" /> is one: the alternative is threading an options
    /// record through every handler signature on the page, which would obscure the scheduling.
    /// </remarks>
    public static ExampleOptions Current { get; private set; } = FromArguments([]);

    /// <summary>
    /// Whether the durable parts — Wolverine agents and the shared transaction — are reachable.
    /// </summary>
    public bool HasDatabase => !string.IsNullOrWhiteSpace(PostgresConnectionString);

    public static ExampleOptions FromArguments(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);

        Current = new ExampleOptions(
            Smoke: smoke,
            // Every two seconds, so a smoke run of a few seconds sees several firings; the nightly
            // expression otherwise, which is what the page shows.
            ReconciliationCron: smoke ? "0/2 * * * * ?" : Part1RecurringPublishing.NightlyCron,
            ReminderDelay: smoke ? TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(30),
            PostgresConnectionString: Environment.GetEnvironmentVariable(PostgresVariable));

        return Current;
    }
}
