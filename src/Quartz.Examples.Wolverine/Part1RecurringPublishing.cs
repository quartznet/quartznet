using Microsoft.Extensions.Logging;

using Wolverine;

namespace Quartz.Examples.Wolverine;

/*
 * Part 1 — publishing a Wolverine message on a cron schedule.
 *
 * What Wolverine lacks: any recurring or cron concept whatsoever. IMessageBus.ScheduleAsync defers one
 * message to one moment and that is the whole of it; there is no crontab, no calendar, no "every
 * weekday at 03:00". Wolverine's maintainer closed the request for it — JasperFx/wolverine#1403,
 * "Lack of recurring (Cron) scheduling" — with "We're not doing this, ever. *Maybe* there'll be a move
 * to integrate Quartz.net or Hangfire *with* Wolverine". The nearest thing Wolverine ships is
 * opts.EnableHeartbeats(), a fixed interval, which answers "every N" and not "at 03:00 on weekdays".
 *
 * What Quartz supplies: the cron expression, the time zone the expression is read in, what happens
 * when the process was down at 03:00 (the misfire instruction), and — with a persistent store — the
 * guarantee that one node in the cluster fires it rather than all of them.
 *
 * The job is the only place the two libraries meet: an IJob<TInput> that resolves IMessageBus and
 * publishes. Nothing downstream can tell the message came from a scheduler.
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
                // firing; FireAndProceed publishes one catch-up message. Wolverine has no equivalent
                // decision to make, because it has nothing to miss.
                .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .UsingInput(new ReconciliationWindow(TimeSpan.FromDays(1))));
    }
}
