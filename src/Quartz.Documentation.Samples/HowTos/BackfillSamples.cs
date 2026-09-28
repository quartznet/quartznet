namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/backfill.md.
/// </summary>
public static class BackfillSamples
{
    public static async ValueTask BackfillingThreeNights(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_backfill_three_nights

        // The nightly export did not run on the 1st, 2nd and 3rd: run each of those nights now,
        // five minutes apart.
        BackfillResult result = await scheduler.Backfill(
            new TriggerKey("nightly-export", "reports"),
            from: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            to: new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero),
            new BackfillOptions { Spacing = TimeSpan.FromMinutes(5) },
            cancellationToken);

        Console.WriteLine($"{result.SlotsFound} slots: {result.Scheduled} scheduled, {result.AlreadyScheduled} already scheduled");

        #endregion
    }

    #region sample_backfill_slot_reader

    public sealed class NightlyExportJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            // A backfilled firing runs now: the night it is for is its slot.
            DateTimeOffset night = context.GetBackfillSlot() ?? context.ScheduledFireTimeUtc ?? context.FireTimeUtc;
            return Export(night, cancellationToken);
        }

        private static ValueTask Export(DateTimeOffset night, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    #endregion
}
