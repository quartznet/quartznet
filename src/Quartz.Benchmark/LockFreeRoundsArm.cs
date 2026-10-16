namespace Quartz.Benchmark;

/// <summary>
/// The 4.5 shape of a firing round for the real-database benchmarks, opted into with an environment variable.
/// </summary>
/// <remarks>
/// Since 4.6 a firing round takes <c>TRIGGER_ACCESS</c> only when something in it needs the lock (#4067).
/// <c>QUARTZ_BENCHMARK_LOCK_FREE_ROUNDS=false</c> sets <c>AdoJobStoreOptions.LockFreeFiringRounds</c> off on
/// every store a benchmark builds, which is 4.5's round — a round of one acquired without the lock and fired
/// under it in a second transaction, every other round locked throughout — on 4.6's code. Unset, or anything
/// else, is what ships.
/// </remarks>
internal static class LockFreeRoundsArm
{
    public const string Variable = "QUARTZ_BENCHMARK_LOCK_FREE_ROUNDS";

    /// <summary>Whether the stores keep the shipped default.</summary>
    public static bool Enabled { get; } = !string.Equals(Environment.GetEnvironmentVariable(Variable), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns lock-free rounds off on the store when the variable says so.</summary>
    public static void Apply(IPersistentStoreBuilder store)
    {
        if (!Enabled)
        {
            store.ConfigureStore(static options => options.LockFreeFiringRounds = false);
        }
    }

    /// <summary>A line for a benchmark's output saying which round shape the stores run.</summary>
    public static string Describe()
    {
        return Enabled
            ? $"{Variable} is not false: a firing round takes TRIGGER_ACCESS only when something in it needs the lock."
            : $"{Variable}=false: every firing round takes TRIGGER_ACCESS, as 4.5's did.";
    }
}
