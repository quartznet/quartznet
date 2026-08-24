using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/rescheduling-jobs.md.
/// </summary>
public static class ReschedulingJobsSamples
{
    public static async ValueTask ReplacingATrigger(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_rescheduling_replace_trigger

        ITrigger replacement = TriggerBuilder.Create()
            .WithIdentity("nightly", "reports")
            .ForJob(new JobKey("build-report", "reports"))
            .WithCronSchedule("0 30 2 * * ?")
            .Build();

        DateTimeOffset? firstFire = await scheduler.RescheduleJob(
            new TriggerKey("nightly", "reports"),
            replacement,
            cancellationToken);

        #endregion
    }

    public static async ValueTask WhenTheOldTriggerIsGone(
        IScheduler scheduler,
        TriggerKey key,
        ITrigger replacement,
        CancellationToken cancellationToken)
    {
        #region sample_rescheduling_missing_trigger

        DateTimeOffset? next = await scheduler.RescheduleJob(key, replacement, cancellationToken);
        if (next is null)
        {
            // the old trigger was gone; store the new one on its own terms
            await scheduler.ScheduleJob(replacement, cancellationToken);
        }

        #endregion
    }

    public static async ValueTask UpdatingDetailsInPlace(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_rescheduling_update_details

        bool applied = await scheduler.UpdateTriggerDetails(
            new TriggerKey("nightly", "reports"),
            new TriggerDetailsUpdate()
                .WithPriority(10)
                .WithDescription("moved up ahead of the invoice run"),
            cancellationToken);

        #endregion
    }

    public static async ValueTask MisfireInstructionsAreTyped(IScheduler scheduler, TriggerKey cronKey)
    {
        #region sample_rescheduling_typed_misfire_instruction

        // fine — the key resolves to a cron trigger
        await scheduler.UpdateTriggerDetails(cronKey, new TriggerDetailsUpdate()
            .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing));

        // rejected — the key resolves to a cron trigger, not a simple one
        await scheduler.UpdateTriggerDetails(cronKey, new TriggerDetailsUpdate()
            .WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow));

        #endregion
    }

    public static void GivingUpOnAJob(string id)
    {
        #region sample_rescheduling_unschedule_all_triggers

        throw new JobExecutionException($"account {id} no longer exists")
        {
            UnscheduleAllTriggers = true,
        };

        #endregion
    }

    public static async ValueTask ResettingErroredTriggers(
        IScheduler scheduler,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        #region sample_rescheduling_reset_error_state

        TriggerQuery broken = new() { State = TriggerState.Error, Take = 250 };

        while (true)
        {
            PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(broken, cancellationToken);
            if (page.Items.Count == 0)
            {
                break;
            }

            List<TriggerKey> keys = page.Items.Select(h => h.Key).ToList();
            List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(keys, cancellationToken);
            logger.LogInformation("Reset {Count} triggers", reset.Count);

            if (!page.HasMore)
            {
                break;
            }
        }

        #endregion
    }
}

#region sample_rescheduling_refire

public sealed class ImportJob(IImportService importer) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await importer.Run(cancellationToken);
        }
        catch (TransientImportException ex) when (context.RefireCount < 3)
        {
            throw new JobExecutionException(ex) { RefireImmediately = true };
        }
    }
}

#endregion

public sealed class RetryingImportJob(IImportService importer) : IJob
{
    #region sample_rescheduling_retry_trigger

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await importer.Run(cancellationToken);
        }
        catch (TransientImportException) when (context.RefireCount == 0)
        {
            ITrigger retry = TriggerBuilder.Create()
                .WithIdentity($"{context.Trigger.Key.Name}-retry-{context.FireInstanceId}", "retries")
                .ForJob(context.JobDetail.Key)
                .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
                .Build();

            await context.Scheduler.ScheduleJob(retry, cancellationToken);
        }
    }

    #endregion
}
