# Quartz.Extensions.Hosting

[Quartz.Extensions.Hosting](https://www.nuget.org/packages/Quartz.Extensions.Hosting) runs Quartz.NET as a [hosted service](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services) on the generic host, starting and stopping the scheduler with the application lifetime.

> **Note:** Quartz 3.2 or later required. For ASP.NET Core apps, see [Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore).

## Installation

```shell
dotnet add package Quartz.Extensions.Hosting
```

## Usage

Register the scheduler and the hosted service:

```csharp
services.AddQuartz(q =>
{
    // scheduler, job and trigger configuration
});

services.AddQuartzHostedService(options =>
{
    // wait for jobs to complete gracefully on shutdown
    options.WaitForJobsToComplete = true;
});
```

See the [Microsoft DI integration docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html) for configuring jobs and triggers.

## Documentation

📖 Full documentation: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/hosted-services-integration.html>
