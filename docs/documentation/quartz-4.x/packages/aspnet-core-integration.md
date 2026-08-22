---

title: ASP.NET Core Integration
---

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore)
provides integration with [ASP.NET Core hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

::: tip
If you only need the generic host, [generic host integration](hosted-services-integration) might suffice.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
dotnet add package Quartz.AspNetCore
```

## Using

You can host the scheduler by invoking `AddQuartzHostedService` on the web application builder.
This adds a hosted Quartz server into the ASP.NET Core process that is started and stopped based on the application's lifetime.

::: tip
`AddQuartzHostedService` lives in the core `Quartz` package. Quartz 3's `AddQuartzServer`, which registered the
hosted service and a health check together, is gone — call `AddQuartzHealthChecks` for the health check.
:::

::: tip
See [Quartz documentation](microsoft-di-integration) to learn more about configuring Quartz scheduler, jobs and triggers.
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

In the code below you can see a real application of the Quartz package within ASP.NET Core MVC.

To better illustrate the use of the Quartz library, imagine you have a `Program.cs` file that is always created when you choose the MVC architecture, and then imagine a `Jobs` folder where you have all the tasks you want Quartz to perform in the background when you run your web application.

After that, it's pretty straightforward.

In the `Jobs` folder, you create a class that will perform the tasks you specify.
The class should extend the `IJob` interface and implement the `Execute` method.

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

A job whose work is asynchronous is written `async ValueTask` as usual. One that only forwards a call, like
this one, can return it directly and skip the state machine; one with nothing to await at all returns
`default`, which is a completed `ValueTask` that allocates nothing. What a job must not do is block: the
scheduler is holding a worker slot for it.

After that, you just need to build Quartz trigger in `Program.cs`, which guarantees that the job will run according to the preset interval.

**Example Program.cs configuration**

<!-- snippet: sample_aspnetcore_schedule_job -->
```csharp
builder.AddQuartz(q =>
{
    // Just use the name of your job that you created in the Jobs folder.
    JobKey jobKey = new("SendEmailJob");
    q.AddJob<SendEmailJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger<SendEmailJob>(opts => opts
        .ForJob(jobKey)
        .WithIdentity("SendEmailJob-trigger")
        // This Cron interval can be described as "run every minute" (when second is zero)
        .WithCronSchedule("0 * * ? * *"));
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

For more on cron triggers see the [CronTriggers lesson](../tutorial/crontriggers.md), and for the expression
syntax itself the [Cron Expression Reference](../cron-expressions.md).

## Health checks

Quartz registers an
[ASP.NET Core health check](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
that reports unhealthy when the scheduler is not running or cannot reach its store. Add it alongside
your application's other checks:

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString)
    .AddQuartz();
```

`services.AddQuartzHealthChecks()` is the same thing for an application that has no other checks to
compose with.

The registration can be customized via the optional configuration callback, for example to attach
tags so the check can be filtered into separate liveness and readiness probes:

<!-- snippet: sample_aspnetcore_health_check_options -->
```csharp
builder.Services.AddHealthChecks().AddQuartz(options =>
{
    options.Name = "quartz-scheduler";   // the default, or quartz-scheduler-<name> for a named scheduler
    options.Tags.AddRange(["ready", "live"]);
    options.FailureStatus = HealthStatus.Unhealthy;
});
```
<!-- endSnippet -->

The callback is one source of `QuartzHealthCheckOptions` among several: the settings go through the
options pipeline, so `services.Configure<QuartzHealthCheckOptions>(...)` and a bound configuration
section mean the same thing, whichever order they are written in.

A named scheduler has a check of its own, reporting on *its* scheduler. Name it on the health checks
builder, or ask for one from inside `AddQuartz`:

<!-- snippet: sample_aspnetcore_named_health_check -->
```csharp
builder.Services.AddHealthChecks().AddQuartz("reporting", options => options.Tags.Add("ready"));

// or, where the scheduler is configured
builder.Services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());
```
<!-- endSnippet -->

Its options are that scheduler's, so they are configured under its name:

<!-- snippet: sample_aspnetcore_named_health_check_options -->
```csharp
builder.Services.Configure<QuartzHealthCheckOptions>("reporting", options => options.Tags.Add("ready"));
```
<!-- endSnippet -->

<!-- snippet: sample_aspnetcore_map_health_checks -->
```csharp
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
```
<!-- endSnippet -->
