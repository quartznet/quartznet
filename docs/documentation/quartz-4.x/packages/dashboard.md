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
Install-Package Quartz.HttpApi
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

When `AuthorizationPolicy` is set, the policy is applied to the dashboard pages, the SignalR hub, the Blazor circuit (`/_blazor`) and the dashboard static asset endpoint, so the whole dashboard is gated consistently — including under a fail-closed `FallbackPolicy`.

Without a policy the dashboard adds no authorization of its own:

- The static asset endpoint (`_content/Quartz.Dashboard/*`) and the Blazor circuit (`/_blazor`) explicitly allow anonymous access so the dashboard's plumbing keeps working under a fail-closed `FallbackPolicy` — these are public package content.
- The dashboard **pages** and the **SignalR hub** carry no authorization metadata of their own, so they remain governed by your host's policies. Under a fail-closed `FallbackPolicy`, an unauthenticated request to `/quartz` is redirected to login (by design) while authenticated users get the full dashboard. To expose the dashboard to unauthenticated users, either don't enforce a fail-closed `FallbackPolicy` over the dashboard paths, or set an `AuthorizationPolicy` your users satisfy.

::: warning Fail-closed `FallbackPolicy` with `MapStaticAssets()`
Assets served by the host's `app.MapStaticAssets()` (the .NET 9/10 default) and the framework script `_framework/blazor.web.js` are served by **host/framework-owned endpoints** that Quartz cannot annotate, so a fail-closed `FallbackPolicy` blocks them for unauthenticated users regardless of the dashboard configuration. If you need them reachable before authentication (for example so the login page is styled), mark your static assets anonymous with `app.MapStaticAssets().AllowAnonymous();` — static web assets are public content. The classic `app.UseStaticFiles()` middleware runs before authorization and is not subject to the `FallbackPolicy`. See [API-only projects](#api-only-projects-no-razor-files) for the related `RequiresAspNetWebAssets` setting.
:::

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

## Integrating with an existing Blazor Server app

If your host application already uses Blazor Server (i.e., it calls `MapRazorComponents<App>().AddInteractiveServerRenderMode()`), you must use the `MapQuartzDashboard` overload that accepts the existing `RazorComponentsEndpointConventionBuilder`. This avoids registering a second `/_blazor` SignalR endpoint, which would cause routing conflicts.

```csharp
services.AddRazorComponents().AddInteractiveServerComponents();
services.AddQuartzDashboard();
```

```csharp
app.UseRouting();
app.UseAntiforgery();

var blazor = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapQuartzApi().RequireAuthorization();
app.MapQuartzDashboard(blazor);
```

The dashboard pages, layout, CSS, and JavaScript interop are automatically registered into the host's Blazor setup via `AddAdditionalAssemblies`. No additional `<link>` or `<script>` tags are needed in your `App.razor`.

::: warning
Do **not** call the parameterless `MapQuartzDashboard()` alongside your own `MapRazorComponents` — this registers two `/_blazor` endpoints and causes the dashboard's interactive pages to fail.
:::

## API-only projects (no .razor files)

If your host project has no `.razor` files of its own (e.g., a pure API project hosting Quartz), and you are running on **.NET 10 or later**, you must add the following to your project file:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

This property tells the .NET SDK to include the Blazor framework scripts (`_framework/blazor.web.js`, `blazor.server.js`) in the app's static web assets. Without it, requests to `/_framework/blazor.web.js` return HTTP 404 because in .NET 10+, these files are no longer embedded in the ASP.NET Core assemblies — they are served as static web assets instead.

On .NET 8 and .NET 9, the framework scripts are served via endpoint routing and no extra configuration is needed.

## Current limitations

- Live views are near-real-time polling/streaming and are not guaranteed to be lossless event storage
- No built-in persistence UI for historical analytics; plugin-backed history is operational/log oriented
- Advanced management remains intentionally scoped; rich typed editors are currently focused on cron calendars/triggers and operational overrides
- UX is optimized for Quartz APIs and scheduler operations, not full workflow/business process visualization
