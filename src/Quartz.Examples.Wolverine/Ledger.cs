using System.Collections.Concurrent;

namespace Quartz.Examples.Wolverine;

/// <summary>
/// What the six parts did, so that <c>--smoke</c> can assert each one actually happened rather than
/// assert that the process survived.
/// </summary>
/// <remarks>
/// A static in an example is a shortcut a real application would not take; it is here because the
/// alternative — a hosted service, a channel and a result type — is scaffolding that would make every
/// sample on the page longer without teaching anything about scheduling.
/// </remarks>
public static class Ledger
{
    private static readonly ConcurrentDictionary<string, List<string>> Entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that something observable happened, and prints it so a plain <c>dotnet run</c> shows
    /// the same story the smoke run asserts.
    /// </summary>
    public static void Record(string what, string detail)
    {
        List<string> details = Entries.GetOrAdd(what, static _ => []);
        lock (details)
        {
            details.Add(detail);
        }

        Console.WriteLine($"  [{what}] {detail}");
    }

    /// <summary>
    /// How many times <paramref name="what" /> has been recorded.
    /// </summary>
    public static int Count(string what) => Details(what).Count;

    /// <summary>
    /// What was recorded against <paramref name="what" />, in order.
    /// </summary>
    public static List<string> Details(string what)
    {
        if (!Entries.TryGetValue(what, out List<string>? details))
        {
            return [];
        }

        lock (details)
        {
            return [.. details];
        }
    }
}

/// <summary>
/// The names the six parts record against, so <c>--smoke</c> and the parts cannot drift apart by a
/// typo in a string literal.
/// </summary>
public static class Events
{
    public const string ReconciliationPublished = "part1:reconciliation-published";
    public const string ReminderScheduled = "part2:reminder-scheduled";
    public const string ReminderFired = "part2:reminder-fired";
    public const string RemindersCancelled = "part2:reminders-cancelled";
    public const string RawEnvelopeStored = "part3:raw-envelope-stored";
    public const string RawEnvelopeDelivered = "part3:raw-envelope-delivered";
    public const string SchedulerStartedByWolverine = "part5:scheduler-started";
    public const string RefundApprovedInTransaction = "part6:refund-approved";
}
