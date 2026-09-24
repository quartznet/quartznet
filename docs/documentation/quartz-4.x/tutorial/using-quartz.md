---

title: 'Using Quartz'
---

This lesson wires up a scheduler that runs one job; the lessons that follow explain each piece of it.

## Install the package

```shell
dotnet add package Quartz
```

That is the whole install for a hosted application. Dependency injection and the hosted service are in the
core package; in 3.x they were the separate `Quartz.Extensions.DependencyInjection` and
`Quartz.Extensions.Hosting` packages.

## Write a job

A job is a class that implements `IJob`:

<!-- snippet: sample_using_quartz_job -->
```csharp
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
```
<!-- endSnippet -->

* The container constructs the job for every fire, so it can take any service: a logger, a `DbContext`, a
  typed `HttpClient`.
* `cancellationToken` is the same token as `context.CancellationToken`. Pass it to everything you await,
  so a shutdown or an `Interrupt` call reaches your work.

## Configure the host

<!-- snippet: sample_using_quartz_host -->
```csharp
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
```
<!-- endSnippet -->

| Call | Does |
|---|---|
| `AddQuartz` | registers the scheduler and everything it is made of |
| `AddQuartzHostedService` | starts the scheduler with the host and shuts it down when the host stops |
| `WaitForJobsToComplete` | makes shutdown wait for running jobs instead of cancelling them |

Both methods extend `IHostApplicationBuilder`, so they also work with `WebApplication.CreateBuilder(args)`.
When you only have the collection, use the `IServiceCollection` overloads
(`builder.Services.AddQuartz(…)`).

## Describing jobs and triggers

`q.ScheduleJob<TJob>(…)` registers one job with one trigger and takes the job's identity from the
trigger's. When a job has several triggers, or is registered somewhere other than its schedule, register
them separately:

<!-- snippet: sample_using_quartz_several_triggers -->
```csharp
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
```
<!-- endSnippet -->

The type argument on `AddTrigger<TJob>` is the job the trigger fires. It lets the trigger's data be named
as properties of that job; see
[More About Jobs & JobDetails](more-about-jobs.md#naming-the-property-instead-of-the-key). Use the bare
`AddTrigger` when the trigger only names its job by key.

`"0 0 2 * * ?"` is a cron expression (second, minute, hour, day-of-month, month, day-of-week): every day
at 02:00. See the [Cron Expression Reference](../cron-expressions.md). Cron is one of five schedule
kinds; the others are in [Lesson 2](jobs-and-triggers.md).

Everything registered this way is stored when the scheduler starts. With a persistent job store,
registrations replace stored definitions of the same name by default, so this list describes the
schedule on every start rather than seeding it once.

## Scheduling at run time

Prefer the declarative registrations above for a schedule that is part of the application: the scheduler
makes the store match them on every start.

For a schedule not known at startup, inject `IScheduler` and schedule whenever you like:

<!-- snippet: sample_using_quartz_scheduling_at_run_time -->
```csharp
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

        await scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken);
    }
}
```
<!-- endSnippet -->

With several schedulers, each is registered under a name; inject one with
`[FromKeyedServices("reporting")] IScheduler scheduler`. See
[Multiple schedulers](../packages/multiple-schedulers.md).

## The scheduler's lifecycle

* Triggers do not fire until the scheduler is started. The hosted service starts it.
* `Standby()` stops firing without shutting anything down; `Start()` resumes. Running jobs keep running.
* `Shutdown()` is final. A shut-down scheduler cannot be started again; build a new one.
* The scheduler is `IAsyncDisposable`. Disposing it shuts it down and releases what it owns. Under a host,
  the host does that.

Next: [Lesson 2](jobs-and-triggers.md), a tour of jobs and triggers.
