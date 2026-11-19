using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Wolverine;

namespace Quartz.Examples.Wolverine;

/*
 * Part 1 — publishing a Wolverine message on a cron schedule.
 *
 * Wolverine has a cron of its own since 6.34, and part 7 is it: opts.Schedules.ScheduleRecurring says
 * "every weekday at 03:00" and publishes a message when it comes round. For a schedule that is only
 * ever "publish this message, skip what the process was down for", part 7 is the answer and this part
 * is not.
 *
 * What a Quartz trigger adds over it, all of it visible in Register below: a misfire instruction, so a
 * firing the process was down for has a decided outcome rather than a fixed one; a schedule that lives
 * in the store rather than in the registration code, so it can be added, moved, paused or removed while
 * the host is up; a job with data, a priority, a retry policy and a calendar of dates to skip; and a
 * backfill of a past range. Part 7's header comment is the other half of this comparison, and the page
 * has the table.
 *
 * The job is the only place the two libraries meet: an IJob<TInput> that resolves IMessageBus and
 * publishes. It stamps the message with the deduplication id and the two headers Wolverine stamps on
 * its own schedules' occurrences, so the handler side treats it as one of them: the same span tags,
 * metric tags, dead-letter filter and, where the store can back it, deduplication.
 */

/// <summary>
/// How far back a reconciliation run should look. A record rather than a <c>JobDataMap</c> entry
/// because <see cref="IJob{TInput}" /> hands it to the job as a typed parameter.
/// </summary>
public sealed record ReconciliationWindow(TimeSpan Length);

/// <summary>
/// Publishes <see cref="RunReconciliation" /> into Wolverine whenever its cron trigger fires.
/// </summary>
public sealed class ReconciliationJob : IJob<ReconciliationWindow>
{
    /// <summary>
    /// The name Wolverine's telemetry knows this schedule by. Not a name registered with
    /// <c>opts.Schedules</c>: the two would share deduplication ids, metric tags and dead letters.
    /// </summary>
    public const string ScheduleName = "reconciliation";

    private readonly IMessageBus bus;
    private readonly ILogger<ReconciliationJob> logger;

    public ReconciliationJob(IMessageBus bus, ILogger<ReconciliationJob> logger)
    {
        this.bus = bus;
        this.logger = logger;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        ReconciliationWindow input,
        CancellationToken cancellationToken = default)
    {
        // The occurrence this firing serves, not the clock: the same instant on a retry, a late
        // misfire catch-up and a recovery, and the slot on a backfill. The window is the one it names.
        FiringOccurrence occurrence = context.GetOccurrence();
        DateTimeOffset to = occurrence.OccurrenceUtc;

        RunReconciliation message = new(to - input.Length, to);
        await bus.PublishAsync(message, WolverineOccurrence.For(ScheduleName, context, occurrence));

        logger.LogInformation("Published {Message} for the window ending {To:O}", nameof(RunReconciliation), to);
    }
}

/// <summary>
/// What Wolverine stamps on an occurrence of its own schedules (<c>RecurringOccurrencePublisher</c>,
/// checked against Wolverine 6.64.2), for a Quartz firing.
/// </summary>
public static class WolverineOccurrence
{
    public static DeliveryOptions For(string scheduleName, IJobExecutionContext context, FiringOccurrence occurrence)
    {
        // Wolverine internals, not an integration point it documents for other schedulers: re-check
        // the id's form and the two header names on every Wolverine update.
        string at = occurrence.OccurrenceUtc.ToString("O", CultureInfo.InvariantCulture);

        DeliveryOptions options = new()
        {
            DeduplicationId = occurrence.Origin switch
            {
                // RecurringMessage.DeduplicationIdFor's form: a republish collapses at consumption.
                FiringOrigin.Schedule => $"{scheduleName}:{at}",

                // ManualDeduplicationIdFor's form: a run asked for by TriggerJob is a run of its own.
                FiringOrigin.Manual => $"{scheduleName}:manual:{at}",

                // A form of its own. The scheduled form would collapse a backfill into the run it
                // repeats; when each backfill started tells two backfills of one slot apart.
                FiringOrigin.Backfill => $"{scheduleName}:backfill:{at}:{context.ScheduledFireTimeUtc:O}",

                _ => throw new UnreachableException()
            }
        };

        // wolverine.schedule.name on the handler's span and schedule.name on its metrics, and what
        // DeadLetterEnvelopeQuery.Header filters dead letters by.
        options.Headers[RecurringMessage.HeaderKey] = scheduleName;

        // wolverine.schedule.occurrence on the handler's span.
        options.Headers[RecurringMessage.OccurrenceHeaderKey] = at;

        return options;
    }
}

/// <summary>
/// Consumes what the cron job published, standing in for whatever the application would really do.
/// </summary>
public static class RunReconciliationHandler
{
    public static void Handle(RunReconciliation message, Envelope envelope)
    {
        Ledger.Record(Events.ReconciliationPublished, $"{message.FromUtc:O} .. {message.ToUtc:O}");

        // The stamping as the handler side sees it, which is what --smoke checks it by: the two headers
        // arrived, and the deduplication id is the schedule's form built from them.
        bool stamped = envelope.TryGetHeader(RecurringMessage.HeaderKey, out string? schedule)
            && envelope.TryGetHeader(RecurringMessage.OccurrenceHeaderKey, out string? occurrence)
            && string.Equals(envelope.DeduplicationId, $"{schedule}:{occurrence}", StringComparison.Ordinal);

        if (stamped)
        {
            Ledger.Record(Events.ReconciliationStampedAsOccurrence, envelope.DeduplicationId!);
        }
    }
}

/// <summary>
/// Registers the cron trigger. Called from <c>Program.cs</c> inside <c>AddQuartz</c>.
/// </summary>
public static class Part1RecurringPublishing
{
    /// <summary>
    /// The expression a real deployment would use: 03:00 on weekdays.
    /// </summary>
    public const string NightlyCron = "0 0 3 ? * MON-FRI";

    public static void Register(IQuartzBuilder q, string cron)
    {
        q.ScheduleJob<ReconciliationJob>(trigger => trigger
            .WithIdentity("reconciliation", "recurring")
            .WithCronSchedule(cron, x => x
                // The expression is read in this zone, so a deployment that means "03:00 local" says
                // so here rather than hoping the host agrees.
                .InTimeZone(TimeZoneInfo.Utc)
                // What happens when the process was down at 03:00. DoNothing skips to the next
                // firing, which is what part 7's schedule does and all it does; FireAndProceed
                // publishes one catch-up message, which is the choice Wolverine's own schedules do
                // not offer.
                .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .UsingInput(new ReconciliationWindow(TimeSpan.FromDays(1))));
    }

    /// <summary>
    /// Makes Wolverine enforce the deduplication id the job stamps. Called from <c>Program.cs</c> inside
    /// <c>UseWolverine</c>, and only with a database.
    /// </summary>
    public static void RequireDeduplication(WolverineOptions opts)
    {
        // Wolverine does this by itself only for message types registered with opts.Schedules.
        // [Deduplicated] on the handler says the same. On a store that cannot deduplicate, either form
        // throws at the first message, which is why the in-memory run leaves it off.
        opts.Durability.MessageDeduplicationMode = MessageDeduplicationMode.CompareByHash;
        opts.Policies.RequireDeduplicationId(chain => chain.MessageType == typeof(RunReconciliation));
    }
}
