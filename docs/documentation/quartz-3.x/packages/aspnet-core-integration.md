---

title: ASP.NET Core Integration
---

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore)
integrates Quartz with [ASP.NET Core hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

::: tip
If you only need the generic host, [generic host integration](hosted-services-integration) may be enough.
:::

## Installation

```shell
Install-Package Quartz.AspNetCore
```

## Using

The `AddQuartzServer` extension method on `IServiceCollection` adds a hosted Quartz server to the ASP.NET Core process, started and stopped with the application's lifetime.

::: tip
The [Quartz.Extensions.DependencyInjection documentation](microsoft-di-integration) covers configuring the scheduler, jobs and triggers.
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
    services.AddQuartzServer(options =>
    {
        // when shutting down we want jobs to complete gracefully
        options.WaitForJobsToComplete = true;
    });
}
```

## Health checks

On target frameworks with health check support, `AddQuartzServer` also registers an
[ASP.NET Core health check](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
named `quartz-scheduler`. It reports unhealthy when the scheduler is not running or cannot reach its store.

Tags let you filter the check, for example into separate liveness and readiness probes:

```csharp
services.AddQuartzServer(
    options => options.WaitForJobsToComplete = true,
    healthCheckTags: ["ready", "live"]);
```

```csharp
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
```

## A practical example of the setup

An ASP.NET Core MVC application with the MVC template's `Program.cs` and a `Jobs` folder holding the background tasks for Quartz.

In the `Jobs` folder, create a class that implements the `IJob` interface and its `Execute` method.

**Example SendEmailJob.cs configuration**

```csharp
public class SendEmailJob : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        // Code that sends a periodic email to the user (for example)
        // Note: This method must always return a value 
        // This is especially important for trigger listeners watching job execution 
        return Task.CompletedTask;
    }
}        
```

Then add the job and its trigger in `Program.cs`, so the job runs on the trigger's schedule.

**Example Program.cs configuration**

```csharp
builder.Services.AddQuartz(q =>
{
    // Just use the name of your job that you created in the Jobs folder.
    var jobKey = new JobKey("SendEmailJob");
    q.AddJob<SendEmailJob>(opts => opts.WithIdentity(jobKey));
    
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("SendEmailJob-trigger")
         //This Cron interval can be described as "run every minute" (when second is zero)
        .WithCronSchedule("0 * * ? * *")
    );
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```

[Cron Triggers](../tutorial/crontriggers.md) covers cron triggers and their format.
