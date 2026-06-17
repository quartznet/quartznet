# Quartz.AspNetCore

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) provides [ASP.NET Core hosted service](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services) integration for Quartz.NET, running a scheduler that starts and stops with the application lifetime.

> **Tip:** If you only need the generic host, [Quartz.Extensions.Hosting](https://www.nuget.org/packages/Quartz.Extensions.Hosting) may be enough.

## Installation

```shell
dotnet add package Quartz.AspNetCore
```

## Usage

Register the scheduler with `AddQuartzServer`:

```csharp
services.AddQuartz(q =>
{
    // scheduler, job and trigger configuration
});

services.AddQuartzServer(options =>
{
    // wait for jobs to complete gracefully on shutdown
    options.WaitForJobsToComplete = true;
});
```

See the [Microsoft DI integration docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html) for configuring jobs and triggers.

## Health checks

On target frameworks with health check support, `AddQuartzServer` also registers an [ASP.NET Core health check](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks) named `quartz-scheduler` that reports unhealthy when the scheduler is not running or cannot reach its store. Attach tags to filter it into separate liveness/readiness probes:

```csharp
services.AddQuartzServer(
    options => options.WaitForJobsToComplete = true,
    healthCheckTags: ["ready", "live"]);
```

## Documentation

📖 Full documentation: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/aspnet-core-integration.html>
