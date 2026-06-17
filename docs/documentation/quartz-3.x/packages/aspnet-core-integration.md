[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore)
provides integration with [ASP.NET Core hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

::: tip
If you only need the generic host, [generic host integration](hosted-services-integration) might suffice.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.AspNetCore

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzServer` on `IServiceCollection`.
This will add a hosted quartz server into ASP.NET Core process that will be started and stopped based on applications lifetime.

::: tip
See [Quartz.Extensions.DependencyInjection documentation](microsoft-di-integration) to learn more about configuring Quartz scheduler, jobs and triggers.
:::

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddQuartz(q =>
    {
        // base quartz scheduler, job and trigger configuration
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

On target frameworks with health check support `AddQuartzServer` also registers an
[ASP.NET Core health check](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
named `quartz-scheduler` that reports unhealthy when the scheduler is not running or cannot reach its store.

You can attach tags to this health check so it can be filtered, for example into separate
liveness and readiness probes:

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