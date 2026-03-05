---
title: Dashboard
---

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor-based dashboard for Quartz.NET that runs inside your ASP.NET Core app and uses Quartz HTTP API endpoints.

::: warning
Quartz Dashboard is currently a work in progress.
The dashboard API surface may change between releases.
Supported target frameworks are .NET 8 and newer.
:::

## Installation

Add package references:

```shell
Install-Package Quartz.Dashboard
Install-Package Quartz.AspNetCore
```

## Basic setup

Configure Quartz, enable HTTP API, and add the dashboard services.

```csharp
services.AddQuartz(q =>
{
    q.AddHttpApi(options =>
    {
        options.ApiPath = "/quartz-api";
    });
});

services.AddQuartzDashboard();
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

Map endpoints:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.UseEndpoints(endpoints =>
{
    endpoints.MapQuartzApi().RequireAuthorization();
    endpoints.MapQuartzDashboard();
});
```

By default, dashboard UI is available at `/quartz`.

## Enabling history plugin

To populate execution history and make related views useful, enable Quartz history plugins in `QuartzOptions`:

```csharp
services.Configure<QuartzOptions>(options =>
{
    options["quartz.plugin.jobHistory.type"] = "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins";
    options["quartz.plugin.triggerHistory.type"] = "Quartz.Plugin.History.LoggingTriggerHistoryPlugin, Quartz.Plugins";
});
```

## Production hardening

### Policy and role-based authorization

Use an explicit policy for dashboard access, and secure API endpoints separately:

```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("QuartzDashboardOps", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Operations", "SchedulerAdmin");
    });
});

services.AddQuartzDashboard(options =>
{
    options.AuthorizationPolicy = "QuartzDashboardOps";
});
```

```csharp
app.UseEndpoints(endpoints =>
{
    endpoints.MapQuartzApi().RequireAuthorization("QuartzDashboardOps");
    endpoints.MapQuartzDashboard();
});
```

### API key or custom authorization checks

If you need machine-to-machine access, use your API auth scheme (for example, an API key handler) and bind that to a policy used by `MapQuartzApi()`.
For dashboard-only custom checks, prefer ASP.NET Core policy/handler-based authorization so the dashboard UI, hub, and API are enforced consistently.

### Deployment guidance for multi-scheduler and clustered setups

- **Clustered ADO.NET job stores:** actions in dashboard are scheduler operations and can affect cluster behavior; restrict write access to trusted operator roles.
- **Many local schedulers in one host:** dashboard scheduler selector supports multiple registered schedulers; use clear scheduler names and environment-specific grouping.
- **Reverse proxy and Blazor Server:** enable WebSocket/SignalR forwarding and sticky sessions where required by your hosting stack.
- **Split operator experiences:** expose a read-only dashboard instance (`ReadOnly = true`) for observers, and a separate write-enabled dashboard for operators.
- **Operational retention:** dashboard history is plugin-fed operational history; configure plugin + external retention/reporting if you need long-term analytics.

## Features

- Scheduler overview and summary cards
- Jobs and triggers listing with search and pagination
- Job details and trigger details pages
- Currently executing jobs view
- Live event/log stream for scheduler activity
- Pause, resume, trigger-now, and unschedule/delete actions (when not in read-only mode)
- Trigger detail cron reschedule and job detail trigger-with-overrides actions
- Calendar create/replace (cron calendar), details, and delete actions
- Multi-scheduler selection
- Read-only mode support via dashboard options

## Current limitations

- Live views are near-real-time polling/streaming and are not guaranteed to be lossless event storage
- No built-in persistence UI for historical analytics; plugin-backed history is operational/log oriented
- Advanced management remains intentionally scoped; rich typed editors are currently focused on cron calendars/triggers and operational overrides
- UX is optimized for Quartz APIs and scheduler operations, not full workflow/business process visualization
