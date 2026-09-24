---
title: Dashboard
---

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor-based dashboard for Quartz.NET. It runs inside your ASP.NET Core app and accesses the scheduler in-process.

::: warning
Quartz Dashboard is currently a work in progress.
The dashboard API surface may change between releases.
Supported target frameworks are .NET 8 and newer.
:::

::: tip
Quartz 3.16 or later required.
:::


## Installation

```shell
Install-Package Quartz.Dashboard
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

Map endpoints:

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

The dashboard UI is at `/quartz` by default.

## Hosting under a custom path

::: tip
The self-contained custom path below requires a Quartz.Dashboard release **newer than 3.18.2** (#3134). On 3.18.2 and earlier only the dashboard pages, links and hub use the custom path; the Blazor framework script, the circuit and the static assets stay at the application root, so a prefix-forwarding reverse proxy needs `UsePathBase` (or equivalent) instead.
:::

When the dashboard hosts its own Blazor root (the parameterless `MapQuartzDashboard()` overload), set a custom base path:

```csharp
services.AddQuartzDashboard(options =>
{
    options.DashboardPath = "/my-api/quartz";
});
```

Everything is then served under `DashboardPath`: the pages, navigation links and SignalR hub, the interactive circuit (`{DashboardPath}/_blazor`), the framework script (`{DashboardPath}/_framework/blazor.web.js`) and the dashboard static assets (`{DashboardPath}/_content/Quartz.Dashboard/*`). The dashboard shell emits a `<base href>` rooted at the dashboard.

This works behind a reverse proxy that forwards only a path prefix, without setting a path base:

- Set `DashboardPath` to the externally visible path, for example `/my-api/quartz` when the proxy forwards `/my-api/*` verbatim.
- Forward WebSocket connections for `{DashboardPath}/_blazor` **and** `{DashboardPath}/hub` (the live-views hub).
- The server-side circuit connects to the live-events hub through the same external URL the browser uses, so the application must reach its own public address for the Live Logs view to work.

If instead the whole application is rebased with `UsePathBase()` (or the proxy sets the request path base), `DashboardPath` is relative to the path base, and the default `/quartz` works as-is under the prefix. With minimal hosting (`WebApplication`), call `app.UseRouting()` explicitly **after** `app.UsePathBase(...)`; otherwise the implicit routing step matches the un-stripped path and every dashboard route returns 404:

```csharp
app.UsePathBase("/my-api");
app.UseRouting();
```

::: warning Upgrading existing custom-path deployments
In earlier releases the Blazor circuit stayed at the site root; with a custom `DashboardPath` it now connects at `{DashboardPath}/_blazor`. Update reverse-proxy rules scoped to `/_blazor`, such as a WebSocket-upgrade location.
:::

::: warning
A custom `DashboardPath` is **not** supported with `MapQuartzDashboard(blazor)` in an existing Blazor application. The page routes are fixed at `/quartz` in that mode, and startup fails with a descriptive exception if a custom path is configured.
:::

## Enabling history plugin

To populate execution history and the views that use it, enable the Quartz history plugins in `QuartzOptions`:

```csharp
services.Configure<QuartzOptions>(options =>
{
    options["quartz.plugin.jobHistory.type"] = "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins";
    options["quartz.plugin.triggerHistory.type"] = "Quartz.Plugin.History.LoggingTriggerHistoryPlugin, Quartz.Plugins";
});
```

## Production hardening

### Policy and role-based authorization

Use an explicit policy for dashboard access:

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
    endpoints.MapQuartzDashboard();
});
```

With `AuthorizationPolicy` set, the policy applies to the dashboard pages, the SignalR hub, the Blazor circuit (`/_blazor`) and the dashboard static asset endpoint. The whole dashboard is gated consistently, including under a fail-closed `FallbackPolicy`.

Without a policy, the dashboard adds no authorization of its own:

- The static asset endpoint (`_content/Quartz.Dashboard/*`) and the Blazor circuit (`/_blazor`) allow anonymous access, so they keep working under a fail-closed `FallbackPolicy`. They are public package content.
- The dashboard **pages** and the **SignalR hub** have no authorization metadata, so your host's policies govern them. Under a fail-closed `FallbackPolicy`, an unauthenticated request to `/quartz` is redirected to login, and authenticated users get the full dashboard. To open the dashboard to unauthenticated users, do not enforce a fail-closed `FallbackPolicy` over the dashboard paths, or set an `AuthorizationPolicy` they satisfy.

::: warning Fail-closed `FallbackPolicy` with `MapStaticAssets()`
Assets served by the host's `app.MapStaticAssets()` (the .NET 9/10 default) and the framework script `_framework/blazor.web.js` come from **host/framework-owned endpoints** that Quartz cannot annotate. A fail-closed `FallbackPolicy` blocks them for unauthenticated users whatever the dashboard configuration. To make them reachable before authentication (for example to style the login page), use `app.MapStaticAssets().AllowAnonymous();`; static web assets are public content. The classic `app.UseStaticFiles()` middleware runs before authorization and is not subject to the `FallbackPolicy`. See [API-only projects](#api-only-projects-no-razor-files) for the related `RequiresAspNetWebAssets` setting.

With a custom `DashboardPath` this does not apply to the dashboard itself: the framework script and static assets are then served under the dashboard path by dashboard-owned endpoints that carry the dashboard's authorization metadata.
:::

### API key or custom authorization checks

Prefer ASP.NET Core policy/handler-based authorization, so the dashboard UI and hub are enforced consistently.

### Deployment guidance for multi-scheduler and clustered setups

- **Clustered ADO.NET job stores:** dashboard actions are scheduler operations and can affect the cluster. Restrict write access to trusted operator roles.
- **Many local schedulers in one host:** the scheduler selector supports multiple registered schedulers. Use clear scheduler names and environment-specific grouping.
- **Reverse proxy and Blazor Server:** enable WebSocket/SignalR forwarding, and sticky sessions where your hosting stack requires them. The Blazor circuit connects to `/_blazor` (or `{DashboardPath}/_blazor` with a custom `DashboardPath`).
- **Split operator experiences:** run a read-only dashboard (`ReadOnly = true`) for observers and a separate write-enabled one for operators.
- **Operational retention:** dashboard history comes from the plugins and is operational. For long-term analytics, configure the plugins plus external retention or reporting.

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

- Live views are near-real-time polling/streaming, not lossless event storage.
- No persistence UI for historical analytics; plugin-backed history is operational and log-oriented.
- Management features are limited on purpose: typed editors cover cron calendars/triggers and operational overrides.
- The UX targets Quartz APIs and scheduler operations, not workflow or business process visualization.

## Integrating with an existing Blazor Server app

If your host already uses Blazor Server (it calls `MapRazorComponents<App>().AddInteractiveServerRenderMode()`), use the `MapQuartzDashboard` overload that takes the existing `RazorComponentsEndpointConventionBuilder`. It avoids registering a second `/_blazor` SignalR endpoint, which would cause routing conflicts.

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

Add the dashboard assembly to the `AdditionalAssemblies` of the `<Router>` in your `Routes.razor`, so the host's interactive router resolves the dashboard pages:

```razor
<Router AppAssembly="typeof(App).Assembly"
        AdditionalAssemblies="new[] { typeof(Quartz.Dashboard.Components.QuartzDashboardApp).Assembly }">
    ...
</Router>
```

Without it, the dashboard renders server-side on the first request, but once the circuit starts the router cannot match the `/quartz` routes: the dashboard flashes briefly and is replaced by the application's not-found page.

The dashboard pages, layout, CSS and JavaScript interop are registered into the host's Blazor endpoint routing via `AddAdditionalAssemblies`. `App.razor` needs no extra `<link>` or `<script>` tags.

::: warning
Do **not** call the parameterless `MapQuartzDashboard()` alongside your own `MapRazorComponents`. It registers two `/_blazor` endpoints, and the dashboard's interactive pages fail.
:::

::: tip
The dashboard pages declare no render mode, so the host must use global interactive server rendering (for example `<Routes @rendermode="InteractiveServer" />` in `App.razor`). With per-page/component interactivity the pages render as static SSR and their actions do not work.
:::

## API-only projects (no .razor files)

If your host project has no `.razor` files of its own (e.g., a pure API project hosting Quartz) and runs on **.NET 10 or later**, add this to the project file:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

It makes the .NET SDK include the Blazor framework scripts (`_framework/blazor.web.js`, `blazor.server.js`) in the app's static web assets. Without it, `/_framework/blazor.web.js` returns HTTP 404: since .NET 10 these files are served as static web assets, no longer embedded in the ASP.NET Core assemblies.

Also enable static files in the request pipeline:

```csharp
app.UseRouting();
app.Antiforgery();
app.UseStaticFiles();
```

On .NET 8 and .NET 9, the framework scripts are served via endpoint routing and need no extra configuration.
