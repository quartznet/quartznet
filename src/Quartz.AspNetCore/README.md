# Quartz.AspNetCore

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) adds the two things a Quartz.NET
scheduler wants from an ASP.NET Core application: an
[HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html) that exposes
scheduler management endpoints, and a
[health check](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks) that reports
unhealthy when the scheduler is not running or cannot reach its store.

Hosting itself is in the core [Quartz](https://www.nuget.org/packages/Quartz) package — `AddQuartz` and
`AddQuartzHostedService` need no extra reference. Quartz 3's `AddQuartzServer`, which registered the
hosted service and a health check together, is gone.

## Installation

```shell
dotnet add package Quartz.AspNetCore
```

## Usage

<!-- snippet: sample_readme_aspnetcore -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz(q => q.AddQuartzHttpApi());
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
builder.Services.AddHealthChecks().AddQuartz();

WebApplication app = builder.Build();

app.MapQuartzHttpApi().RequireAuthorization();
app.MapHealthChecks("/healthz");
```
<!-- endSnippet -->

The API manages jobs and triggers, so authorize it: `MapQuartzHttpApi` returns the endpoint convention
builder to say so on. The health check takes tags, so it can be filtered into separate liveness and
readiness probes, and a named scheduler gets a check of its own.

## Documentation

- [ASP.NET Core integration](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/aspnet-core-integration.html)
- [HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html)
- [Quartz.NET documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/)
