using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/pausing-with-a-reason.md.
/// </summary>
public sealed class PauseReasonSamples
{
    public static async Task PausingWithAReason(IScheduler scheduler)
    {
        #region sample_pause_with_reason

        await scheduler.PauseTriggerWith(
            new TriggerKey("nightly-export"),
            new PauseDetails { Reason = "vendor API is down until 18:00", RequestedBy = "alice" });

        #endregion
    }

    public static async Task PausingAGroupWithAReason(IScheduler scheduler)
    {
        #region sample_pause_group_with_reason

        await scheduler.PauseJobGroupsWith(
            GroupMatcher<JobKey>.GroupEquals("billing"),
            new PauseDetails { Reason = "quarter close", RequestedBy = "finance-ops" });

        #endregion
    }

    public static async Task<int> PausingASetWithAReason(IScheduler scheduler)
    {
        #region sample_pause_set_with_reason

        List<TriggerKey> paused = await scheduler.PauseTriggersWith(
            [new TriggerKey("nightly-export"), new TriggerKey("hourly-sync")],
            new PauseDetails { Reason = "vendor API is down until 18:00", RequestedBy = "alice" });

        // The keys this call paused: a missing or already paused trigger is absent.
        return paused.Count;

        #endregion
    }

    public static async Task ReadingItBack(IScheduler scheduler)
    {
        #region sample_pause_read

        PauseInfo? pause = await scheduler.GetTriggerPause(new TriggerKey("nightly-export"));
        if (pause is not null)
        {
            // Either text may be null; a pause that said neither recorded nothing, so reads as null.
            Console.WriteLine($"Paused {pause.PausedAtUtc:u} by {pause.RequestedBy ?? "?"}: {pause.Reason}");
        }

        #endregion
    }

    public static void PausingWhenRetriesRunOut(IHostApplicationBuilder builder)
    {
        #region sample_pause_when_retries_exhausted

        builder.Services.AddQuartz(q =>
        {
            // A trigger whose retry policy gives up is paused, with the job's exception message as
            // the reason, until somebody resumes it.
            q.PauseTriggerWhenRetriesExhausted();

            q.AddJob<ExportJob>(j => j.WithIdentity("export"));
            q.AddTrigger(t => t
                .ForJob("export")
                .WithIdentity("nightly-export")
                .WithCronSchedule("0 0 2 * * ?")
                .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5))));
        });

        #endregion
    }

    public sealed class ExportJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
