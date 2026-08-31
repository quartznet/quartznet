using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/one-off-job.md.
/// </summary>
public sealed class OneOffJobSamples
{
    public static void ADurableJobWithNoTrigger(IHostApplicationBuilder builder)
    {
        #region sample_one_off_job_durable_registration

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<AnExampleJob>(j => j
                .WithIdentity("name", "group")
                .StoreDurably());
        });

        #endregion
    }

    #region sample_one_off_job_trigger_now

    public async ValueTask RunNow(IScheduler scheduler, CancellationToken cancellationToken)
    {
        await scheduler.TriggerJob(new JobKey("name", "group"), cancellationToken: cancellationToken);
    }

    #endregion

    public static async ValueTask AddingTheJobAtRunTime(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_one_off_job_add_job

        IJobDetail job = JobBuilder.Create<AnExampleJob>()
            .WithIdentity("name", "group")
            .StoreDurably()
            .Build();

        await scheduler.AddJob(job, new AddJobOptions { Replace = true }, cancellationToken);

        #endregion
    }

    public static void MisfireInstructionForAOneShotTrigger()
    {
        #region sample_one_off_job_misfire_instruction

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("name", "group")
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();

        #endregion
    }
}

public sealed class OneOffJobWithData
{
    #region sample_one_off_job_trigger_now_with_data

    public async ValueTask RunNow(IScheduler scheduler, string customer, CancellationToken cancellationToken)
    {
        JobDataMap data = new() { { "CustomerId", customer } };
        await scheduler.TriggerJob(new JobKey("name", "group"), data, cancellationToken);
    }

    #endregion
}

public sealed class OneOffJobScheduledOnce
{
    #region sample_one_off_job_schedule_once

    public async ValueTask ScheduleOnce(IScheduler scheduler, CancellationToken cancellationToken)
    {
        IJobDetail job = JobBuilder.Create<AnExampleJob>()
            .WithIdentity("name", "group")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("name", "group")
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken);
    }

    #endregion
}

#region sample_one_off_job_typed_one_liner

public sealed record SendInvoice(string CustomerId, decimal Amount);

public sealed class SendInvoiceJob : IJob<SendInvoice>
{
    public ValueTask Execute(IJobExecutionContext context, SendInvoice input, CancellationToken cancellationToken = default)
    {
        // input.CustomerId, input.Amount
        return default;
    }
}

public sealed class Invoicing
{
    public async ValueTask Remind(IScheduler scheduler, ILogger logger, SendInvoice invoice, CancellationToken cancellationToken)
    {
        ScheduledOneOffJob firing = await scheduler.ScheduleJob<SendInvoiceJob, SendInvoice>(
            invoice,
            TimeSpan.FromDays(7),
            // Named, so it can be replaced or cancelled; grouped by the thing it is about, so the
            // whole conversation can be cancelled at once. Replacing(name) is the preset for the
            // pair, because a firing with no name of its own has nothing to replace.
            OneOffJobOptions.Replacing($"invoice-{invoice.CustomerId}") with { Group = invoice.CustomerId },
            cancellationToken);

        // What was arranged: the trigger's key, and when the store says it will first fire.
        logger.LogInformation("Reminder {Trigger} scheduled for {At}", firing.TriggerKey, firing.FirstFireTimeUtc);

        // ... and to call it off:
        await scheduler.UnscheduleJob(firing.TriggerKey, cancellationToken);
    }
}

#endregion

public sealed class RecoverableInvoicing
{
    #region sample_one_off_job_request_recovery

    public async ValueTask Remind(IScheduler scheduler, SendInvoice invoice, CancellationToken cancellationToken)
    {
        await scheduler.ScheduleJob<SendInvoiceJob, SendInvoice>(
            invoice,
            TimeSpan.FromDays(7),
            new OneOffJobOptions { RequestRecovery = true },
            cancellationToken);
    }

    #endregion
}

public sealed class InvoicingOnASchedule
{
    #region sample_one_off_job_scheduled_job_key

    public async ValueTask Nightly(IScheduler scheduler, CancellationToken cancellationToken)
    {
        // A schedule of its own, pointed at the job the one-liner keeps rather than at a second job
        // built here: same job, same payload shape, one more trigger.
        ITrigger nightly = TriggerBuilder.Create<SendInvoiceJob>(scheduler.TimeProvider)
            .WithIdentity("nightly", "invoicing")
            .ForJob(SchedulerConstants.ScheduledJobKey<SendInvoiceJob>())
            .WithCronSchedule("0 0 2 * * ?")
            .UsingInput(new SendInvoice("all", 0m))
            .Build();

        await scheduler.ScheduleJob(nightly, cancellationToken: cancellationToken);
    }

    #endregion
}

#region sample_one_off_job_try_get_input

public sealed class SendInvoiceCompatJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // A firing scheduled by 4.x carries the whole payload under one key. One scheduled before the
        // upgrade carries the flat keys the 3.x job wrote, and there is nothing to read there — which
        // is an answer here, where an IJob<SendInvoice> would have failed the firing instead.
        if (!context.TryGetInput(out SendInvoice? invoice) || invoice is null)
        {
            invoice = new SendInvoice(
                context.MergedJobDataMap.GetString("CustomerId")!,
                context.MergedJobDataMap.Get<decimal>("Amount"));
        }

        return Send(invoice, cancellationToken);
    }

    private static ValueTask Send(SendInvoice invoice, CancellationToken cancellationToken) => default;
}

#endregion

public sealed class InvoicingCancellation
{
    #region sample_one_off_job_cancel_by_group

    public async ValueTask<int> CustomerWentAway(IScheduler scheduler, string customerId, CancellationToken cancellationToken)
    {
        // Every firing scheduled under this customer's group goes in one call: the group the one-liner
        // put them in is the handle for calling all of them off, and nothing has to list the keys first.
        List<TriggerKey> calledOff = await scheduler.UnscheduleJobs(
            GroupMatcher<TriggerKey>.GroupEquals(customerId),
            cancellationToken);

        // The answer names what went, so "there was nothing left to cancel" is a count, not a guess.
        return calledOff.Count;
    }

    #endregion
}
