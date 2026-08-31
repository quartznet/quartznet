---

title: 'Using Quartz'
---

Quartz runs inside your application. You register a scheduler with the application's service container,
describe the jobs and triggers it should start with, and let the host start and stop it. This lesson
wires up a scheduler that runs one job; the lessons that follow explain each piece of it.

## Install the package

```shell
dotnet add package Quartz
```

That is the whole install for a hosted application. Dependency injection and the hosted service are part
of the core package — in 3.x they were the separate `Quartz.Extensions.DependencyInjection` and
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

The job is constructed from the container for every fire, so it can take whatever the rest of your
application takes — a logger, a `DbContext`, a typed `HttpClient`. The `cancellationToken` is the same
token as `context.CancellationToken`; pass it on to everything you await, so a shutdown or an
`Interrupt` call actually reaches your work.

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

`AddQuartz` registers the scheduler and everything it is made of. `AddQuartzHostedService` starts it when
the host starts and shuts it down when the host stops; `WaitForJobsToComplete` makes shutdown wait for
jobs that are still running instead of cancelling them.

Both hang off `IHostApplicationBuilder`, so the same two lines work in a web application built with
`WebApplication.CreateBuilder(args)`. They are also available on `IServiceCollection`
(`builder.Services.AddQuartz(…)`) when the registration lives in a method that only has the collection.

## Describing jobs and triggers

`q.ScheduleJob<TJob>(…)` is the short form for the common case: one job, one trigger, the job's identity
taken from the trigger's. When a job has several triggers, or when the job is registered somewhere other
than where its schedule is, name them separately:

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

The type argument on `AddTrigger<TJob>` is the job the trigger fires. It is what lets the trigger's data
be named as properties of that job — see
[More About Jobs & JobDetails](more-about-jobs.md#naming-the-property-instead-of-the-key). Use the
bare `AddTrigger` when the trigger only names its job by key and you do not need that.

`"0 0 2 * * ?"` is a cron expression: second, minute, hour, day-of-month, month, day-of-week, so that one
is "every day at 02:00". The fields and their special characters are in the
[Cron Expression Reference](../cron-expressions.md), and cron is only one of five schedule kinds — the
others are in [Lesson 2](jobs-and-triggers.md).

Everything registered this way is stored when the scheduler starts. With a persistent job store it is
also what the store already holds that matters: registrations replace stored definitions of the same
name by default, which is what makes this list the description of the schedule rather than a one-time
seed.

## Scheduling at run time

The registrations above are *declarative*: the application describes the schedule it wants, and the
scheduler makes the store match on every start. That is the shape to prefer for a schedule that is part
of the application.

Not every schedule is known at startup, though. `IScheduler` is an ordinary service, so inject it and
schedule whenever you like:

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

An application with several schedulers registers each under a name, and injects one by that name with
`[FromKeyedServices("reporting")] IScheduler scheduler` — see
[Multiple schedulers](../packages/multiple-schedulers.md).

## The scheduler's lifecycle

* Triggers do not fire until the scheduler has been started. The hosted service does that for you.
* `Standby()` stops firing without shutting anything down; `Start()` resumes. Jobs already running keep
  running.
* `Shutdown()` is final. A scheduler that has been shut down cannot be started again — build a new one.
* The scheduler is `IAsyncDisposable`, and disposing it shuts it down and releases what it owns. Under a
  host, the host does that.

In [Lesson 2](jobs-and-triggers.md) we take a quick tour of jobs and triggers, so that the code above
reads as more than an incantation.
