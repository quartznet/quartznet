using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.HowTos;

#region sample_multiple_triggers_job

public sealed class CustomerProcessJob : IJob
{
    public static readonly JobKey Key = new("customer-process", "batch");

    private readonly ILogger<CustomerProcessJob> logger;

    public CustomerProcessJob(ILogger<CustomerProcessJob> logger)
    {
        this.logger = logger;
    }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobDataMap data = context.MergedJobDataMap;

        string? customerId = data.GetString("CustomerId");
        int batchSize = data.GetInt("batch-size");

        logger.LogInformation("CustomerId={CustomerId} batch-size={BatchSize}", customerId, batchSize);
        return default;
    }
}

#endregion

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/multiple-triggers.md.
/// </summary>
public static class MultipleTriggersSamples
{
    public static void AtConfigurationTime(IHostApplicationBuilder builder)
    {
        #region sample_multiple_triggers_configuration

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<CustomerProcessJob>(j => j
                .WithIdentity(CustomerProcessJob.Key)
                .StoreDurably()
                .UsingJobData("batch-size", 50));

            q.AddTrigger<CustomerProcessJob>(t => t
                .ForJob(CustomerProcessJob.Key)
                .WithIdentity("customer-1-hourly")
                .UsingJobData("CustomerId", "1")
                .WithCronSchedule("0 0 * ? * *"));

            q.AddTrigger<CustomerProcessJob>(t => t
                .ForJob(CustomerProcessJob.Key)
                .WithIdentity("customer-2-nightly")
                .UsingJobData("CustomerId", "2")
                .UsingJobData("batch-size", 500)   // this trigger overrides the job's value
                .WithCronSchedule("0 0 2 ? * *"));
        });

        #endregion
    }

    public static async ValueTask AdHocFiring(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_multiple_triggers_ad_hoc

        JobDataMap data = new() { { "CustomerId", "3" }, { "batch-size", 10 } };
        await scheduler.TriggerJob(CustomerProcessJob.Key, data, cancellationToken);

        #endregion
    }
}

public sealed class CustomerSchedules
{
    #region sample_multiple_triggers_at_run_time

    public async ValueTask ScheduleFor(
        IScheduler scheduler,
        IReadOnlyCollection<string> customers,
        CancellationToken cancellationToken)
    {
        IJobDetail job = JobBuilder.Create<CustomerProcessJob>()
            .WithIdentity(CustomerProcessJob.Key)
            .StoreDurably()
            .UsingJobData("batch-size", 50)
            .Build();

        await scheduler.AddJob(job, new AddJobOptions { Replace = true }, cancellationToken);

        foreach (string customer in customers)
        {
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"customer-{customer}", "batch")
                .ForJob(CustomerProcessJob.Key)
                .UsingJobData("CustomerId", customer)
                .WithCronSchedule("0 0 * ? * *")
                .Build();

            await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken);
        }
    }

    #endregion
}
