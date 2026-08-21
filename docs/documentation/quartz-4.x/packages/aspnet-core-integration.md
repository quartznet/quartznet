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
Install-Package Quartz.AspNetCore
```

## Using

You can host the scheduler by invoking `AddQuartzHostedService` on `IServiceCollection`.
This adds a hosted Quartz server into the ASP.NET Core process that is started and stopped based on the application's lifetime.

::: tip
`AddQuartzHostedService` lives in the core `Quartz` package. Quartz 3's `AddQuartzServer`, which registered the
hosted service and a health check together, is gone — call `AddQuartzHealthChecks` for the health check.
:::

::: tip
See [Quartz documentation](microsoft-di-integration) to learn more about configuring Quartz scheduler, jobs and triggers.
:::

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddQuartz(q =>
    {
        // base Quartz scheduler, job and trigger configuration
    });

    // ASP.NET Core hosting
    services.AddQuartzHostedService(options =>
    {
        // when shutting down we want jobs to complete gracefully
        options.WaitForJobsToComplete = true;
    });
}
```

## A practical example of the setup

In the code below you can see a real application of the Quartz package within ASP.NET Core MVC.

To better illustrate the use of the Quartz library, imagine you have a `Program.cs` file that is always created when you choose the MVC architecture, and then imagine a `Jobs` folder where you have all the tasks you want Quartz to perform in the background when you run your web application.

After that, it's pretty straightforward.

In the `Jobs` folder, you create a class that will perform the tasks you specify.
The class should extend the `IJob` interface and implement the `Execute` method.

**Example SendEmailJob.cs configuration**

```csharp
public class SendEmailJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Code that sends a periodic email to the user (for example)
        // Note: This method must always return a value 
        // This is especially important for trigger listers watching job execution 
        return default;
    }
}        
```

After that, you just need to build Quartz trigger in `Program.cs`, which guarantees that the job will run according to the preset interval.

**Example Program.cs configuration**

```csharp
builder.Services.AddQuartz(q =>
{
    // Just use the name of your job that you created in the Jobs folder.
    var jobKey = new JobKey("SendEmailJob");
    q.AddJob<SendEmailJob>(opts => opts.WithIdentity(jobKey));
    
    q.AddTrigger<IJob>(opts => opts
        .ForJob(jobKey)
        .WithIdentity("SendEmailJob-trigger")
         //This Cron interval can be described as "run every minute" (when second is zero)
        .WithCronSchedule("0 * * ? * *")
    );
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```

For more information on cron triggers and their format, you can use the tutorial directly from Quartz - [Cron Triggers](../tutorial/crontriggers.md).

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

```csharp
builder.Services.AddHealthChecks().AddQuartz(options =>
{
    options.Name = "quartz-scheduler";   // the default, or quartz-scheduler-<name> for a named scheduler
    options.Tags.AddRange(["ready", "live"]);
    options.FailureStatus = HealthStatus.Unhealthy;
});
```

The callback is one source of `QuartzHealthCheckOptions` among several: the settings go through the
options pipeline, so `services.Configure<QuartzHealthCheckOptions>(...)` and a bound configuration
section mean the same thing, whichever order they are written in.

A named scheduler has a check of its own, reporting on *its* scheduler. Name it on the health checks
builder, or ask for one from inside `AddQuartz`:

```csharp
builder.Services.AddHealthChecks().AddQuartz("reporting", options => options.Tags.Add("ready"));

// or, where the scheduler is configured
builder.Services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());
```

Its options are that scheduler's, so they are configured under its name:

```csharp
builder.Services.Configure<QuartzHealthCheckOptions>("reporting", options => options.Tags.Add("ready"));
```

```csharp
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
```
