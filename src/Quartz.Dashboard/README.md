# Quartz.Dashboard

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor dashboard for
Quartz.NET that runs inside your ASP.NET Core application and drives the schedulers registered in that
same application.

**The dashboard is a work in progress and its API surface may change between releases.**

## Installation

It builds on `Quartz.AspNetCore`, which this package brings along, so one reference is enough:

```shell
dotnet add package Quartz.Dashboard
```

## Usage

<!-- snippet: sample_readme_dashboard -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz();
builder.Services.AddQuartzHttpApi();
builder.Services.AddQuartzDashboard();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

WebApplication app = builder.Build();

app.UseAntiforgery();
app.MapQuartzHttpApi();
app.MapQuartzDashboard();
```
<!-- endSnippet -->

The UI is then at `/quartz`. `MapQuartzDashboard("/ops/quartz")` serves it somewhere else — pages,
assets and the Blazor circuit all move with it — and `AddQuartzDashboard(options => …)` takes an
authorization policy, which is what a deployment reachable by anyone but you needs.

Execution-history views stay empty until the scheduler records history: add
`q.UseJobHistoryLogging()` and `q.UseTriggerHistoryLogging()` from
[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins).

## Documentation

The full guide covers custom paths and reverse proxies, authorization, integrating into an existing
Blazor application, and production hardening:

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard.html>
