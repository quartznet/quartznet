# Quartz.Extensions.DependencyInjection

[Quartz.Extensions.DependencyInjection](https://www.nuget.org/packages/Quartz.Extensions.DependencyInjection) provides [Microsoft Dependency Injection](https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection) integration for Quartz.NET, wrapping the configuration properties with a strongly-typed API.

> **Note:** Quartz 3.1 or later required.

## Installation

```shell
dotnet add package Quartz.Extensions.DependencyInjection
```

## Usage

Configure Quartz with `AddQuartz`. Configuration can come from code and/or the `Quartz` section of `appsettings.json`:

```csharp
services.AddQuartz(q =>
{
    var jobKey = new JobKey("awesome job", "awesome group");

    q.AddJob<ExampleJob>(jobKey, j => j.WithDescription("my awesome job"));

    q.AddTrigger(t => t
        .ForJob(jobKey)
        .WithIdentity("Cron Trigger")
        .WithCronSchedule("0/3 * * * * ?"));
});
```

Read hierarchical JSON configuration with the `IConfiguration` overload:

```csharp
services.AddQuartz(Configuration.GetSection("Quartz"), q =>
{
    // additional code-based configuration
});
```

> **Note:** As of Quartz.NET 3.7 all jobs are created as scoped and MS DI is configured by default — there is no need to call the `UseMicrosoftDependencyInjection*` overloads. By default Quartz resolves the job type from the container, falling back to `ActivatorUtilities` (the job should have a single public constructor).

> **Warning:** With persistent job stores, always declare explicit job and trigger names so existence checks work correctly across application restarts.

Pair this with [Quartz.Extensions.Hosting](https://www.nuget.org/packages/Quartz.Extensions.Hosting) (or [Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore)) to manage the scheduler lifecycle, and see [Multiple Schedulers](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/multiple-schedulers.html) for hosting several schedulers in one app.

## Documentation

📖 Full documentation, including defining jobs and triggers in JSON: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html>
