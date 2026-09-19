using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.Listeners;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/job-continuations.md, and the continuation
/// sections of one-off-job.md and the triggers tutorial.
/// </summary>
public sealed class ContinuationsSamples
{
    public static void AContinuationDeclaredAtStartup(IHostApplicationBuilder builder)
    {
        #region sample_continuations_registration

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<DataImportJob>(j => j.WithIdentity("import", "nightly"));
            q.AddTrigger<DataImportJob>(t => t
                .WithIdentity("import", "nightly")
                .ForJob("import", "nightly")
                .WithCronSchedule("0 0 2 * * ?"));

            // Reconciliation has no schedule of its own: it runs when the import has run, and only
            // if the import worked. Until then the trigger sits in the store in Awaiting.
            q.AddJob<ReconcileJob>(j => j.WithIdentity("reconcile", "nightly"));
            q.AddTrigger<ReconcileJob>(t => t
                .WithIdentity("reconcile", "nightly")
                .ForJob("reconcile", "nightly")
                .StartAfter(new TriggerKey("import", "nightly")));
        });

        #endregion
    }

    public static async ValueTask AContinuationScheduledAtRunTime(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_continuations_conditions

        // "Tell operations whenever the import does not get there": a failure the retry policy has
        // given up on, or a firing somebody interrupted. A success discards this trigger.
        ITrigger alert = TriggerBuilder.Create<AlertOpsJob>(scheduler.TimeProvider)
            .WithIdentity("alert", "nightly")
            .ForJob("alert", "nightly")
            .StartAfter(
                new TriggerKey("import", "nightly"),
                ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation)
            .Build();

        await scheduler.ScheduleJob(alert, cancellationToken: cancellationToken);

        #endregion
    }

    public static async ValueTask AFloorUnderAReleasedContinuation(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_continuations_floor

        // StartAfter composes with the rest of the builder rather than replacing it. The start time
        // stays a floor, so a continuation released at 03:00 still waits until 09:00; and the
        // schedule is the schedule the released trigger then keeps.
        ITrigger report = TriggerBuilder.Create<ReconcileJob>(scheduler.TimeProvider)
            .WithIdentity("report", "nightly")
            .ForJob("reconcile", "nightly")
            .StartAfter(new TriggerKey("import", "nightly"))
            .StartAt(DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(9))
            .Build();

        await scheduler.ScheduleJob(report, cancellationToken: cancellationToken);

        #endregion
    }

    public static async ValueTask AOneCallContinuation(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_continuations_one_off

        // One firing of the import, six hours from now. The key it answers with is the handle.
        ScheduledOneOffJob import = await scheduler.ScheduleJob<DataImportJob, ImportRequest>(
            new ImportRequest("eu-west"),
            TimeSpan.FromHours(6),
            cancellationToken: cancellationToken);

        // And one firing of the reconciliation after it. There is no time argument, because the
        // time is the import's completion.
        ScheduledOneOffJob reconcile = await scheduler.ScheduleJob<ReconcileJob, ImportRequest>(
            new ImportRequest("eu-west"),
            Continuation.After(import.TriggerKey),
            cancellationToken: cancellationToken);

        #endregion
    }

    public static async ValueTask WhatIsWaiting(IScheduler scheduler, ILogger logger, CancellationToken cancellationToken)
    {
        #region sample_continuations_listing

        PagedResult<TriggerHeader> waiting = await scheduler.QueryTriggers(
            new TriggerQuery { State = TriggerState.Awaiting },
            cancellationToken);

        foreach (TriggerHeader trigger in waiting.Items)
        {
            // A listing says what each one is waiting for and what releases it, without loading a
            // trigger per row.
            logger.LogInformation(
                "{Trigger} is waiting for {Parent} ({Condition})",
                trigger.Key,
                trigger.ContinuesAfter,
                trigger.ContinuationCondition);
        }

        #endregion
    }

    public static void ARecurringConditionalChain(IHostApplicationBuilder builder)
    {
        #region sample_continuations_chaining_listener

        JobChainingJobListener chain = new("nightly-chain");

        // Every time the import fails, run the alert. A listener link fires on every completion,
        // where a continuation settles once - which is the difference between the two.
        chain.AddJobChainLink(
            new JobKey("import", "nightly"),
            new JobKey("alert", "nightly"),
            ContinuationCondition.OnFailure);

        builder.Services.AddQuartz(q => q.AddJobListener(chain));

        #endregion
    }
}

#region sample_continuations_tutorial

public sealed class TutorialContinuation
{
    public async ValueTask Schedule(IScheduler scheduler, CancellationToken cancellationToken)
    {
        // The trigger is ordinary in every way except when it fires: it is stored in
        // TriggerState.Awaiting, nothing acquires it, and the import's completion settles it.
        ITrigger reconcile = TriggerBuilder.Create<ReconcileJob>(scheduler.TimeProvider)
            .WithIdentity("reconcile", "nightly")
            .ForJob("reconcile", "nightly")
            .StartAfter(new TriggerKey("import", "nightly"), ContinuationCondition.OnSuccess)
            .Build();

        await scheduler.ScheduleJob(reconcile, cancellationToken: cancellationToken);
    }
}

#endregion

#region sample_one_off_job_continuation

public sealed class InvoiceRun
{
    public async ValueTask Send(IScheduler scheduler, ImportRequest request, CancellationToken cancellationToken)
    {
        ScheduledOneOffJob import = await scheduler.ScheduleJob<DataImportJob, ImportRequest>(
            request,
            TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);

        // The second firing's time is the first firing's completion, so the overload takes a
        // Continuation where the others take a DateTimeOffset or a TimeSpan.
        await scheduler.ScheduleJob<ReconcileJob, ImportRequest>(
            request,
            Continuation.After(import.TriggerKey, ContinuationCondition.OnSuccess),
            cancellationToken: cancellationToken);
    }
}

#endregion

/// <summary>
/// What the import and the reconciliation are given.
/// </summary>
public sealed record ImportRequest(string Region);

public sealed class DataImportJob : IJob<ImportRequest>
{
    public ValueTask Execute(IJobExecutionContext context, ImportRequest input, CancellationToken cancellationToken = default) => default;
}

public sealed class ReconcileJob : IJob<ImportRequest>
{
    public ValueTask Execute(IJobExecutionContext context, ImportRequest input, CancellationToken cancellationToken = default) => default;
}

public sealed class AlertOpsJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
