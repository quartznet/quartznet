using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.Tutorial;

#region sample_using_quartz_job

public sealed class HelloJob : IJob
{
    private readonly ILogger<HelloJob> logger;

    public HelloJob(ILogger<HelloJob> logger)
    {
        this.logger = logger;
    }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Hello from {JobKey}", context.JobDetail.Key);
        return default;
    }
}

#endregion

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/using-quartz.md.
/// </summary>
public static class UsingQuartzSamples
{
    public static async ValueTask ConfigureTheHost(string[] args)
    {
        #region sample_using_quartz_host

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.AddQuartz(q =>
        {
            // run HelloJob now, and then every 40 seconds
            q.ScheduleJob<HelloJob>(trigger => trigger
                .WithIdentity("helloTrigger")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromSeconds(40))
                    .RepeatForever()));
        });

        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        IHost host = builder.Build();

        // blocks until the host is stopped, and then until the last running job completes
        await host.RunAsync();

        #endregion
    }

    public static void JobWithSeveralTriggers(IHostApplicationBuilder builder)
    {
        #region sample_using_quartz_several_triggers

        builder.AddQuartz(q =>
        {
            JobKey jobKey = new("reportJob");

            q.AddJob<ReportJob>(j => j
                .WithIdentity(jobKey)
                .WithDescription("nightly and on-demand sales report"));

            q.AddTrigger<ReportJob>(t => t
                .ForJob(jobKey)
                .WithIdentity("nightly")
                .WithCronSchedule("0 0 2 * * ?"));

            q.AddTrigger<ReportJob>(t => t
                .ForJob(jobKey)
                .WithIdentity("hourly-on-weekdays")
                .WithCronSchedule("0 0 9-17 ? * MON-FRI"));
        });

        #endregion
    }
}

#region sample_using_quartz_scheduling_at_run_time

public sealed class ReportRequests
{
    private readonly IScheduler scheduler;

    public ReportRequests(IScheduler scheduler)
    {
        this.scheduler = scheduler;
    }

    public async ValueTask QueueFor(string customer, CancellationToken cancellationToken)
    {
        IJobDetail job = JobBuilder.Create<ReportJob>()
            .WithIdentity(customer, "reports")
            .UsingJobData("customer", customer)
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(customer, "reports")
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken);
    }
}

#endregion
