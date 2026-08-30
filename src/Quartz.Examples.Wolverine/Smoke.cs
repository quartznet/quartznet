namespace Quartz.Examples.Wolverine;

/// <summary>
/// What <c>--smoke</c> does: waits for every part to have produced its observable effect, then exits
/// zero or one.
/// </summary>
/// <remarks>
/// <para>
/// The point of the flag is that a build server can prove the six parts still work, not merely that
/// the process starts. A run that starts, schedules nothing and exits zero would be worse than no
/// check at all.
/// </para>
/// <para>
/// It is deliberately not a unit test. What it exercises is two hosted runtimes agreeing about
/// startup order, a scheduling loop, and a message bus — none of which a test double would tell the
/// truth about.
/// </para>
/// </remarks>
public static class Smoke
{
    /// <summary>
    /// The order the program pays for before its reminder falls due. No reminder may fire for it.
    /// </summary>
    public const string PaidOrderId = "A-1002";

    /// <summary>
    /// The order whose reminder is left to fire.
    /// </summary>
    public const string RemindedOrderId = "A-1001";

    public static async Task<int> RunAsync(ExampleOptions options, TimeSpan timeout)
    {
        List<string> required =
        [
            Events.SchedulerStartedByWolverine,
            Events.ReconciliationPublished,
            Events.ReminderScheduled,
            Events.ReminderFired,
            Events.RemindersCancelled,
            Events.RawEnvelopeStored,
            Events.RawEnvelopeDelivered,
        ];

        if (options.HasDatabase)
        {
            required.Add(Events.RefundApprovedInTransaction);
        }

        DateTimeOffset deadline = TimeProvider.System.GetUtcNow() + timeout;
        while (TimeProvider.System.GetUtcNow() < deadline && required.Exists(static x => Ledger.Count(x) == 0))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        List<string> missing = required.FindAll(static x => Ledger.Count(x) == 0);

        Console.WriteLine();
        foreach (string name in required)
        {
            Console.WriteLine($"  {(Ledger.Count(name) == 0 ? "MISSING" : "ok     ")}  {name} x{Ledger.Count(name)}");
        }

        // The cancellation is only proved by the reminder that did not fire. Asserted against the
        // order rather than against a count, because part 6 publishes a reminder of its own in the
        // durable mode and a count would then mean two different things in two configurations.
        bool cancellationHeld = !Ledger.Details(Events.ReminderFired)
            .Exists(static x => x.Contains(PaidOrderId, StringComparison.Ordinal));

        if (!cancellationHeld)
        {
            Console.WriteLine($"  MISSING  no reminder should have fired for {PaidOrderId}, one did");
        }

        Console.WriteLine();
        if (missing.Count == 0 && cancellationHeld)
        {
            Console.WriteLine("smoke: ok");
            return 0;
        }

        Console.WriteLine("smoke: failed");
        return 1;
    }
}
