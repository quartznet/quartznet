---
title: Dashboard
---

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor-based dashboard for Quartz.NET that runs inside your ASP.NET Core app and uses Quartz HTTP API endpoints.

::: warning
Quartz Dashboard is currently a work in progress.
The dashboard API surface may change between releases.
Supported target frameworks are .NET 8 and newer.
:::

::: tip
Quartz 3.16 or later required.
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

## Hosting under a custom path

When the dashboard hosts its own Blazor root (the parameterless `MapQuartzDashboard()` overload), the dashboard can be served from a custom base path:

```csharp
services.AddQuartzDashboard(options =>
{
    options.DashboardPath = "/my-api/quartz";
});
```

All dashboard pages, navigation links and the SignalR hub are then served under the configured path (for example `/my-api/quartz/jobs`).

Dashboard links are generated relative to the application base URI, so the dashboard also works behind a reverse proxy or an application path base (`UsePathBase`) — the configured `DashboardPath` is interpreted relative to the path base.

::: warning
A custom `DashboardPath` is **not** supported when integrating into an existing Blazor application with `MapQuartzDashboard(blazor)`; the dashboard page routes are fixed at `/quartz` in that mode and startup fails with a descriptive exception if a custom path is configured.
:::

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

When `AuthorizationPolicy` is set, the policy is applied to the dashboard pages, the SignalR hub and the dashboard static asset endpoint. Without a policy, the static asset endpoint (`_content/Quartz.Dashboard/*`) explicitly allows anonymous access so that applications enforcing a fail-closed `FallbackPolicy` can still load the dashboard CSS and JavaScript — the files are public package content.

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

app.MapQuartzDashboard(blazor);
```

In addition, the host application's interactive router must be able to resolve the dashboard pages. Add the dashboard assembly to the `AdditionalAssemblies` of the `<Router>` in your `Routes.razor`:

```razor
<Router AppAssembly="typeof(App).Assembly"
        AdditionalAssemblies="new[] { typeof(Quartz.Dashboard.Components.QuartzDashboardApp).Assembly }">
    ...
</Router>
```

Without this, the dashboard renders server-side on the initial request, but the interactive router cannot match the `/quartz` routes once the circuit starts — the dashboard flashes briefly and is then replaced by the application's not-found page.

The dashboard pages, layout, CSS, and JavaScript interop are automatically registered into the host's Blazor endpoint routing via `AddAdditionalAssemblies`. No additional `<link>` or `<script>` tags are needed in your `App.razor`.

::: warning
Do **not** call the parameterless `MapQuartzDashboard()` alongside your own `MapRazorComponents` — this registers two `/_blazor` endpoints and causes the dashboard's interactive pages to fail.
:::

::: tip
The dashboard pages do not declare a render mode of their own, so the host application needs to use global interactive server rendering (for example `<Routes @rendermode="InteractiveServer" />` in `App.razor`). With per-page/component interactivity the dashboard pages render as static SSR and their actions are not functional.
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
