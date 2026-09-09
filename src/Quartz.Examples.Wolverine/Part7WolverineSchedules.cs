using Wolverine;

namespace Quartz.Examples.Wolverine;

/*
 * Part 7 — Wolverine's own recurring schedules, which since 6.34 answer most of what part 1 asks Quartz
 * for. This part is here so the page can compare them honestly rather than describe one of them.
 *
 * opts.Schedules.ScheduleRecurring publishes a message on a cron expression parsed by Cronos: five
 * fields, or six where the leading one is seconds, read in the schedule's own time zone — UTC unless
 * CronSchedule is handed another. One SingularAgent per cluster keeps the *next* occurrence of every
 * schedule pre-scheduled through the ordinary scheduled-message pipeline, so delivery, durability and
 * replay are the machinery Wolverine already shipped. Each occurrence carries a deterministic
 * deduplication id of the form "{name}:{occurrenceUtc:O}", so a failover that re-publishes one collapses
 * it at consumption rather than running it twice, and IRecurringScheduleControl pauses, resumes and
 * queries a schedule at run time.
 *
 * What it deliberately does not do, and where part 1 still earns its place: a missed occurrence is
 * skipped rather than decided about — there is no misfire policy to choose from — and the set of
 * schedules is fixed in code at UseWolverine, so nothing adds, moves or removes one while the host is
 * up. Anything faster than every five seconds is refused at the registration call site, because durable
 * scheduled messages replay on DurabilitySettings.ScheduledJobPollingTime and a faster cadence could not
 * be honoured.
 *
 * Registration lives inside UseWolverine rather than inside AddQuartz, which is the whole point: this
 * schedule is Wolverine's, and Quartz never learns it exists.
 */

/// <summary>
/// Published by Wolverine's own recurring schedule, not by a Quartz job. The occurrence time is the
/// moment the schedule says the run is for, which is what <c>context.ScheduledFireTimeUtc</c> is in part
/// 1 — not the moment the agent got round to publishing it.
/// </summary>
public sealed record ExpireUnpaidOrders(DateTimeOffset OccurrenceUtc);

/// <summary>
/// Consumes what Wolverine's schedule published. An ordinary handler: the cron machinery decides when an
/// occurrence is published and nothing about how it is processed.
/// </summary>
public static class ExpireUnpaidOrdersHandler
{
    public static void Handle(ExpireUnpaidOrders message)
    {
        Ledger.Record(Events.WolverineSchedulePublished, $"occurrence {message.OccurrenceUtc:O}");
    }
}

/// <summary>
/// Registers the recurring schedule. Called from <c>Program.cs</c> inside <c>UseWolverine</c>.
/// </summary>
public static class Part7WolverineSchedules
{
    /// <summary>
    /// The expression a real deployment would use: 03:15 on weekdays. Cronos' five-field grammar, so
    /// there is no seconds field and no day-of-month <c>?</c> — the two day fields are ANDed rather than
    /// unioned, which is one of the places the two grammars mean different things by the same string.
    /// </summary>
    public const string NightlyCron = "15 3 * * MON-FRI";

    /// <summary>
    /// The name the schedule is known by: the key for <see cref="Wolverine.Runtime.Recurring.IRecurringScheduleControl" />,
    /// half of every occurrence's deduplication id, and what the <c>recurring-schedule</c> header carries.
    /// </summary>
    public const string ScheduleName = "expire-unpaid-orders";

    public static void Register(WolverineOptions opts, string cron)
    {
        // Cronos' grammar, not Quartz's. The zone is the schedule's own, so a deployment that means
        // "03:15 local" says so here rather than hoping the host agrees — the same decision part 1
        // makes with InTimeZone, and one of the few this feature and a Quartz trigger both let you make.
        CronSchedule schedule = new(cron, TimeZoneInfo.Utc);

        // The factory is handed the occurrence time, which is this feature's answer to reading
        // context.ScheduledFireTimeUtc rather than the clock: the message describes the window the
        // schedule says it is for. Without a name the schedule is named for its message type, and
        // ScheduleRecurring<T>(cron) is the whole registration for a message with a parameterless
        // constructor.
        opts.Schedules.ScheduleRecurring(ScheduleName, schedule, occurrence => new ExpireUnpaidOrders(occurrence));
    }
}
