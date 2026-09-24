---

title: ASP.NET Core Integration
---

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) runs the scheduler as an
[ASP.NET Core hosted service](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).
For the generic host alone, [generic host integration](hosted-services-integration) is enough.

## Installation

```shell
dotnet add package Quartz.AspNetCore
```

## Using

`AddQuartzHostedService` on the web application builder starts and stops the scheduler with the application.
Configuring the scheduler, jobs and triggers is covered in [Microsoft DI Integration](microsoft-di-integration).

::: tip
`AddQuartzHostedService` and the health check are in the core `Quartz` package. Quartz 3's `AddQuartzServer`,
which registered both, is gone: call each by its own name.
:::

**Example Program.cs configuration**

<!-- snippet: sample_aspnetcore_registration -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz(q =>
{
    // base Quartz scheduler, job and trigger configuration
});

// ASP.NET Core hosting
builder.AddQuartzHostedService(options =>
{
    // when shutting down we want jobs to complete gracefully
    options.WaitForJobsToComplete = true;
});

WebApplication app = builder.Build();
```
<!-- endSnippet -->

## A practical example of the setup

A job is a class that implements `IJob` and its `Execute` method, for example in a `Jobs` folder of an MVC
project.

**Example SendEmailJob.cs configuration**

<!-- snippet: sample_aspnetcore_job -->
```csharp
public sealed class SendEmailJob : IJob
{
    private readonly IEmailSender sender;

    public SendEmailJob(IEmailSender sender)
    {
        this.sender = sender;
    }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Code that sends a periodic email to the user (for example)
        return sender.SendDigest(cancellationToken);
    }
}
```
<!-- endSnippet -->

- Asynchronous work: write `async ValueTask` as usual.
- A job that only forwards one call can return it directly, as above.
- A job with nothing to await returns `default`, a completed `ValueTask` that allocates nothing.
- Never block: the job holds a worker slot.

Then schedule it in `Program.cs`. `ScheduleJob<TJob>` registers one job with one trigger and names the job
after the trigger, so there is no `JobKey` or `ForJob` to keep in step. For a job shared by several triggers,
use `AddJob` and `AddTrigger`; see
[Microsoft DI Integration](microsoft-di-integration.md#a-worked-configuration).

**Example Program.cs configuration**

<!-- snippet: sample_aspnetcore_schedule_job -->
```csharp
builder.AddQuartz(q =>
{
    // One job and the one trigger that fires it. The job class you wrote in the
    // Jobs folder is the type argument; the job takes its identity from the trigger.
    q.ScheduleJob<SendEmailJob>(trigger => trigger
        .WithIdentity("SendEmailJob-trigger")
        // This Cron interval can be described as "run every minute" (when second is zero)
        .WithCronSchedule("0 * * ? * *"));
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

See the [CronTriggers lesson](../tutorial/crontriggers.md) and the
[Cron Expression Reference](../cron-expressions.md).

## Health checks

The scheduler health check is in the core `Quartz` package: it reads `IScheduler.Status` and probes the job
store, with no ASP.NET Core dependency. Registering, naming and tagging it is covered in
[Hosted Services Integration](hosted-services-integration.md#health-checks). ASP.NET Core adds the endpoint and
the status-code mapping:

<!-- snippet: sample_aspnetcore_map_health_checks -->
```csharp
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
```
<!-- endSnippet -->

`Degraded` maps to **200** by default, like `Healthy`, so a scheduler in standby looks healthy to anything that
reads only the status code. To take a standby node out of rotation, map `Degraded` to 503 in
[`HealthCheckOptions.ResultStatusCodes`](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks).
