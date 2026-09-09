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
 * the host is up; a job with data, a priority, a retry policy and a calendar of dates to skip; and no
 * floor on how often it may fire. Part 7's header comment is the other half of this comparison, and the
 * page has the table.
 *
 * The job is the only place the two libraries meet: an IJob<TInput> that resolves IMessageBus and
 * publishes. Nothing downstream can tell the message came from a scheduler rather than from a handler.
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
        // The scheduler's own clock, not DateTimeOffset.UtcNow: a trigger that misfired and is firing
        // late still reports the time it was scheduled for, which is the window the run is about.
        DateTimeOffset to = context.ScheduledFireTimeUtc ?? context.FireTimeUtc;

        RunReconciliation message = new(to - input.Length, to);
        await bus.PublishAsync(message);

        logger.LogInformation("Published {Message} for the window ending {To:O}", nameof(RunReconciliation), to);
    }
}

/// <summary>
/// Consumes what the cron job published, standing in for whatever the application would really do.
/// </summary>
public static class RunReconciliationHandler
{
    public static void Handle(RunReconciliation message)
    {
        Ledger.Record(Events.ReconciliationPublished, $"{message.FromUtc:O} .. {message.ToUtc:O}");
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
}
