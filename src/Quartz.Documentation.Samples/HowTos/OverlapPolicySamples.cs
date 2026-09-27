using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/overlap-policy.md.
/// </summary>
public sealed class OverlapPolicySamples
{
    public static void GivingATriggerAPolicy(IHostApplicationBuilder builder)
    {
        #region sample_overlap_policy_builder

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<ReportJob>(j => j.WithIdentity("report"));

            q.AddTrigger(t => t
                .ForJob("report")
                .WithIdentity("every-five-minutes")
                .WithCronSchedule("0 0/5 * * * ?")
                // A report that is still running when the next one is due: drop the next one.
                .WithOverlapPolicy(OverlapPolicy.Skip));
        });

        #endregion
    }

    public static async Task ChangingIt(IScheduler scheduler)
    {
        #region sample_overlap_policy_update

        // Decides from the next firing that comes due; the running one keeps its own.
        await scheduler.UpdateTriggerDetails(
            new TriggerKey("every-five-minutes"),
            new TriggerDetailsUpdate().WithOverlapPolicy(OverlapPolicy.BufferOne));

        #endregion
    }

    public static async Task ReadingTheSkips(IExecutionHistoryStore history, string schedulerName)
    {
        #region sample_overlap_policy_history

        PagedResult<MisfireHistoryEntry> page = await history.QueryMisfires(
            new MisfireHistoryQuery { SchedulerName = schedulerName, Take = 50 });

        foreach (MisfireHistoryEntry entry in page.Items)
        {
            // Overlap: the trigger's Skip policy dropped the firing. Missed: a misfire.
            Console.WriteLine($"{entry.TriggerName} {entry.ScheduledFireTimeUtc:u} {entry.Reason}");
        }

        #endregion
    }

    #region sample_overlap_policy_cancellable_job

    public sealed class ReportJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            foreach (int page in Enumerable.Range(1, 40))
            {
                // CancelPrevious signals this token when the next firing starts; a job that ignores it
                // runs on beside the new one.
                cancellationToken.ThrowIfCancellationRequested();
                await RenderPage(page, cancellationToken);
            }
        }

        private static Task RenderPage(int page, CancellationToken cancellationToken) => Task.Delay(100, cancellationToken);
    }

    #endregion
}
