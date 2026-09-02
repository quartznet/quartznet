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
app.MapQuartzHttpApi().RequireAuthorization();
app.MapQuartzDashboard().RequireAuthorization();
```
<!-- endSnippet -->

The UI is then at `/quartz`. `MapQuartzDashboard("/ops/quartz")` serves it somewhere else — pages,
assets and the Blazor circuit all move with it — and `AddQuartzDashboard(options => …)` takes an
authorization policy, which is what a deployment reachable by anyone but you needs.

The `RequireAuthorization()` on both map calls is not decoration. The dashboard and the API add no
authentication of their own, both are fully mutating, and a job scheduled through either names its type
as a string the request supplies — which, with `Quartz.Jobs` on the host's probing path, reaches
`NativeJob` and its process. A mapping that says nothing about authorization refuses to start; say
`AllowAnonymous()` where you mean it.

Execution-history views stay empty until the scheduler records history: add
`q.UseJobHistoryLogging()` and `q.UseTriggerHistoryLogging()` from
[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins).

## Trimming

This package is deliberately **not** trimmable, and does not declare `IsTrimmable`. Blazor Server is a
reflective framework: a component's `[Parameter]` properties are set by name from the render tree, the
router finds page components by type, and grids, event callbacks and the SignalR hub proxy all bind
members that nothing statically references. That is the framework's model rather than anything this
package could resolve, and the Blazor packages themselves are not marked trimmable either.

An application that publishes trimmed or native AOT therefore does so without the dashboard. `Quartz`,
`Quartz.AspNetCore` and `Quartz.HttpClient` are trimmable, so a trimmed service can still be driven
remotely over the HTTP API.

Hosting the dashboard itself in another process is not something this package does today: the client it
registers reads the schedulers in its own container. A supported remote dashboard is designed in
[#3387](https://github.com/quartznet/quartznet/issues/3387).

## Documentation

The full guide covers custom paths and reverse proxies, authorization, integrating into an existing
Blazor application, and production hardening:

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard.html>
