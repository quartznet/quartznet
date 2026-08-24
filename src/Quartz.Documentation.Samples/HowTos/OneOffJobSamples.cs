using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        await scheduler.ScheduleJob(job, trigger, cancellationToken);
    }

    #endregion
}
