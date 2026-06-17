# Quartz.Dashboard

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor-based dashboard for Quartz.NET that runs inside your ASP.NET Core app and accesses the scheduler in-process.

> **Warning:** Quartz Dashboard is a work in progress and its API surface may change between releases. Supported target frameworks are .NET 8 and newer (Quartz 3.16 or later required).

## Installation

```shell
dotnet add package Quartz.Dashboard
```

## Basic setup

Configure Quartz and add the dashboard services:

```csharp
services.AddQuartz(q =>
{
    // configure jobs and triggers
});

services.AddQuartzDashboard();
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

Map the endpoints:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.UseEndpoints(endpoints =>
{
    endpoints.MapQuartzDashboard();
});
```

By default the dashboard UI is available at `/quartz`.

## Documentation

📖 The full guide covers custom hosting paths, history plugins, production hardening and authorization, integrating with an existing Blazor Server app, and API-only project setup:

<https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/dashboard.html>
