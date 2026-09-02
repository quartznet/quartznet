# Quartz.AspNetCore

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) adds what a Quartz.NET scheduler
wants from an ASP.NET Core application: an
[HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html) that exposes
scheduler management endpoints.

Hosting and the scheduler's health check are both in the core
[Quartz](https://www.nuget.org/packages/Quartz) package — `AddQuartz`, `AddQuartzHostedService` and
the health check need no extra reference. This package brings ASP.NET Core along with it, so take
it for the HTTP API rather than for the check. Quartz 3's `AddQuartzServer`, which registered the hosted
service and a health check together, is gone.

## Installation

```shell
dotnet add package Quartz.AspNetCore
```

## Usage

<!-- snippet: sample_readme_aspnetcore -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz();
builder.Services.AddQuartzHttpApi();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
builder.Services.AddHealthChecks().AddQuartz();

WebApplication app = builder.Build();

app.MapQuartzHttpApi().RequireAuthorization();
app.MapHealthChecks("/healthz");
```
<!-- endSnippet -->

The API manages jobs and triggers, so authorize it: `MapQuartzHttpApi` returns the endpoint convention
builder to say so on, and a mapping that says nothing refuses to start. It adds no authentication of its
own, every route mutates, and a job scheduled through it names its type as a string the request supplies
— which, with `Quartz.Jobs` on the host's probing path, reaches `NativeJob` and its process. Say
`AllowAnonymous()` where you mean it. `AddQuartz` and the health check beside it come from the core
package; serving the health report at `/healthz` is what needs ASP.NET Core.

## Documentation

- [ASP.NET Core integration](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/aspnet-core-integration.html)
- [HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html)
- [Quartz.NET documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/)
