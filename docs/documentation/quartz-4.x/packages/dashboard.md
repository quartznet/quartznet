---
title: Dashboard
---

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor-based dashboard for Quartz.NET that runs inside your ASP.NET Core app and uses Quartz HTTP API endpoints.

::: warning
Quartz Dashboard is currently a work in progress.
The dashboard API surface may change between releases.
:::

## Installation

The dashboard is served over the [HTTP API](http-api.md), which ships in `Quartz.AspNetCore`. The dashboard
package brings it along, so one reference is enough:

```shell
dotnet add package Quartz.Dashboard
```

## Basic setup

Configure Quartz, enable the HTTP API, and add the dashboard services.

<!-- snippet: sample_dashboard_registration -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz(q =>
{
    q.AddQuartzHttpApi(options =>
    {
        options.ApiPath = "/quartz-api";
    });
});

builder.Services.AddQuartzDashboard();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

Map endpoints:

<!-- snippet: sample_dashboard_pipeline -->
```csharp
WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapQuartzHttpApi().RequireAuthorization();
app.MapQuartzDashboard();
```
<!-- endSnippet -->

By default, dashboard UI is available at `/quartz`.

## The schedulers the dashboard covers

`AddQuartzDashboard()` installs the dashboard's own two plugins — the live event feed and the execution
history the History page reads — into **every** scheduler in the container, and the order of the calls
does not matter. A scheduler registered with `AddQuartz("acme", …)` therefore has a populated Live Logs
view and History page just like the default one; each scheduler gets its own instance of each plugin,
initialized with its own name, and history entries are attributed to the scheduler that produced them.

It does this with `ConfigureAllQuartzSchedulers`, so nothing extra is written at the call site.

::: warning Fixed in 4.0.0-alpha.2
The dashboard's plugins used to be registered without a service key, which meant only a scheduler
registered by `AddQuartz()` — the unnamed, default one — ever ran them. A named scheduler appeared in the
scheduler selector and its jobs and triggers rendered, but its Live Logs view and its History page were
silently always empty. There was nothing to configure to get them; this is a fix rather than a new option.
:::

## Hosting under a custom path

When the dashboard hosts its own Blazor root, it can be served from a custom base path. Name it where the endpoints are mapped, the way the rest of ASP.NET Core reads (`MapHealthChecks("/health")`):

<!-- snippet: sample_dashboard_map_path -->
```csharp
app.MapQuartzDashboard("/my-api/quartz");
```
<!-- endSnippet -->

Or configure it at registration, which is the shape to use when the path comes from configuration:

<!-- snippet: sample_dashboard_options_path -->
```csharp
services.AddQuartzDashboard(options =>
{
    options.DashboardPath = "/my-api/quartz";
});
```
<!-- endSnippet -->

If both are given, **the pattern passed to `MapQuartzDashboard` wins**; the parameterless overload uses whatever `DashboardPath` says. A pattern given at the map site is held to the same rule as the option: a plain URL path starting with `/`, with no `{`, `}`, `?`, `#`, `.` or `..` segments and no empty ones.

With a custom dashboard path the dashboard is fully self-contained under it. The pages, navigation links and SignalR hub as well as the Blazor plumbing — the interactive circuit (`{DashboardPath}/_blazor`), the framework script (`{DashboardPath}/_framework/blazor.web.js`) and the dashboard static assets (`{DashboardPath}/_content/Quartz.Dashboard/*`) — are all served under it, and the dashboard shell emits a `<base href>` rooted at the dashboard itself.

This makes the dashboard work behind a reverse proxy that forwards only a path prefix to the application without setting a path base: configure `DashboardPath` with the externally visible path (for example `/my-api/quartz` when the proxy forwards `/my-api/*` verbatim) and make sure the proxy forwards WebSocket connections for `{DashboardPath}/_blazor` **and** `{DashboardPath}/hub` (the live-views hub). Note that the dashboard's server-side circuit connects to the live-events hub through the same externally visible URL the browser uses, so the application must be able to reach its own public address for the Live Logs view to work.

Alternatively, when the whole application is rebased with `UsePathBase()` (or the proxy sets the request path base), the configured `DashboardPath` is interpreted relative to the path base — the default `/quartz` then works as-is under the prefix. With minimal hosting (`WebApplication`), call `app.UseRouting()` explicitly **after** `app.UsePathBase(...)` — otherwise the implicit routing step matches against the un-stripped path and every dashboard route returns 404:

<!-- snippet: sample_dashboard_path_base -->
```csharp
app.UsePathBase("/my-api");
app.UseRouting();
```
<!-- endSnippet -->

::: warning Upgrading existing custom-path deployments
In earlier releases the Blazor circuit stayed at the site root; with a custom `DashboardPath` it now connects at `{DashboardPath}/_blazor`. Reverse-proxy rules scoped to `/_blazor` (for example a WebSocket-upgrade location) must be updated accordingly.
:::

::: warning
A custom dashboard path is **not** supported when integrating into an existing Blazor application with `MapQuartzDashboard(blazor)`; the dashboard page routes are fixed at `/quartz` in that mode and startup fails with a descriptive exception if a custom path is configured. There is no `MapQuartzDashboard(blazor, pattern)` overload for the same reason.
:::

## Enabling history plugin

To populate execution history and make related views useful, add the Quartz history plugins to the
scheduler:

<!-- snippet: sample_dashboard_history_plugins -->
```csharp
builder.AddQuartz(q =>
{
    q.UseJobHistoryLogging();
    q.UseTriggerHistoryLogging();
});
```
<!-- endSnippet -->

## Production hardening

### Policy and role-based authorization

Use an explicit policy for dashboard access, and secure API endpoints separately:

<!-- snippet: sample_dashboard_authorization_policy -->
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("QuartzDashboardOps", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Operations", "SchedulerAdmin");
    });
});

builder.Services.AddQuartzDashboard(options =>
{
    options.AuthorizationPolicy = "QuartzDashboardOps";
});
```
<!-- endSnippet -->

<!-- snippet: sample_dashboard_require_authorization -->
```csharp
app.MapQuartzHttpApi().RequireAuthorization("QuartzDashboardOps");
app.MapQuartzDashboard();
```
<!-- endSnippet -->

When `AuthorizationPolicy` is set, the policy is applied to the dashboard pages, the SignalR hub, the Blazor circuit (`/_blazor`) and the dashboard static asset endpoint, so the whole dashboard is gated consistently — including under a fail-closed `FallbackPolicy`.

Without a policy the dashboard adds no authorization of its own:

- The static asset endpoint (`_content/Quartz.Dashboard/*`) and the Blazor circuit (`/_blazor`) explicitly allow anonymous access so the dashboard's plumbing keeps working under a fail-closed `FallbackPolicy` — these are public package content.
- The dashboard **pages** and the **SignalR hub** carry no authorization metadata of their own, so they remain governed by your host's policies. Under a fail-closed `FallbackPolicy`, an unauthenticated request to `/quartz` is redirected to login (by design) while authenticated users get the full dashboard. To expose the dashboard to unauthenticated users, either don't enforce a fail-closed `FallbackPolicy` over the dashboard paths, or set an `AuthorizationPolicy` your users satisfy.

::: warning Fail-closed `FallbackPolicy` with `MapStaticAssets()`
Assets served by the host's `app.MapStaticAssets()` (the .NET 9/10 default) and the framework script `_framework/blazor.web.js` are served by **host/framework-owned endpoints** that Quartz cannot annotate, so a fail-closed `FallbackPolicy` blocks them for unauthenticated users regardless of the dashboard configuration. If you need them reachable before authentication (for example so the login page is styled), mark your static assets anonymous with `app.MapStaticAssets().AllowAnonymous();` — static web assets are public content. The classic `app.UseStaticFiles()` middleware runs before authorization and is not subject to the `FallbackPolicy`. See [API-only projects](#api-only-projects-no-razor-files) for the related `RequiresAspNetWebAssets` setting.

With a custom `DashboardPath` this caveat does not apply to the dashboard itself: the framework script and the dashboard static assets are then served under the dashboard path by dashboard-owned endpoints that carry the dashboard's authorization metadata.
:::

### API key or custom authorization checks

If you need machine-to-machine access, use your API auth scheme (for example, an API key handler) and bind that to a policy used by `MapQuartzHttpApi()`.
For dashboard-only custom checks, prefer ASP.NET Core policy/handler-based authorization so the dashboard UI, hub, and API are enforced consistently.

### Deployment guidance for multi-scheduler and clustered setups

- **Clustered ADO.NET job stores:** actions in dashboard are scheduler operations and can affect cluster behavior; restrict write access to trusted operator roles.
- **Many local schedulers in one host:** dashboard scheduler selector supports multiple registered schedulers; use clear scheduler names and environment-specific grouping.
- **Reverse proxy and Blazor Server:** enable WebSocket/SignalR forwarding and sticky sessions where required by your hosting stack. The Blazor circuit connects to `/_blazor` (or `{DashboardPath}/_blazor` when a custom `DashboardPath` is configured).
- **Split operator experiences:** expose a read-only dashboard instance (`ReadOnly = true`) for observers, and a separate write-enabled dashboard for operators.
- **Operational retention:** dashboard history is plugin-fed operational history; configure plugin + external retention/reporting if you need long-term analytics.

## Features

- Scheduler overview and summary cards
- Jobs and triggers listing with search and pagination
- Job details and trigger details pages
- Currently executing jobs view — cluster-wide with a persistent job store, showing which node owns each
  execution, and interrupting the one execution a row names rather than every execution of its job
- Live event/log stream for scheduler activity, fed by plugins `AddQuartzDashboard` installs on every
  scheduler in the container — so a named scheduler streams its own events, each plugin instance
  initialized with the name of the scheduler it belongs to
- Pause, resume, trigger-now, and unschedule/delete actions (when not in read-only mode)
- Trigger detail cron reschedule and job detail trigger-with-overrides actions
- Calendar create/replace (cron calendar), details, and delete actions
- Multi-scheduler selection, over the schedulers the container has *built* — the dashboard lists
  `ISchedulerRepository`, so a registered scheduler nothing has created yet does not appear
- Read-only mode support via dashboard options

## Integrating with an existing Blazor Server app

If your host application already uses Blazor Server (i.e., it calls `MapRazorComponents<App>().AddInteractiveServerRenderMode()`), you must use the `MapQuartzDashboard` overload that accepts the existing `RazorComponentsEndpointConventionBuilder`. This avoids registering a second `/_blazor` SignalR endpoint, which would cause routing conflicts.

<!-- snippet: sample_dashboard_host_app_registration -->
```csharp
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddQuartzDashboard();
```
<!-- endSnippet -->

<!-- snippet: sample_dashboard_host_app_pipeline -->
```csharp
app.UseAntiforgery();

RazorComponentsEndpointConventionBuilder blazor = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapQuartzHttpApi().RequireAuthorization();
app.MapQuartzDashboard(blazor);
```
<!-- endSnippet -->

The dashboard pages, layout, CSS, and JavaScript interop are automatically registered into the host's Blazor setup via `AddAdditionalAssemblies`. No additional `<link>` or `<script>` tags are needed in your `App.razor`.

::: warning
Do **not** call the parameterless `MapQuartzDashboard()` alongside your own `MapRazorComponents` — this registers two `/_blazor` endpoints and causes the dashboard's interactive pages to fail.
:::

## API-only projects (no .razor files)

If your host project has no `.razor` files of its own (for example a pure API project hosting Quartz), you must add the following to your project file:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

This property tells the .NET SDK to include the Blazor framework scripts (`_framework/blazor.web.js`, `blazor.server.js`) in the app's static web assets. Without it, requests to `/_framework/blazor.web.js` return HTTP 404: as of .NET 10 these files are no longer embedded in the ASP.NET Core assemblies — they are served as static web assets instead.

## Current limitations

- Live views are near-real-time polling/streaming and are not guaranteed to be lossless event storage
- No built-in persistence UI for historical analytics; plugin-backed history is operational/log oriented
- Advanced management remains intentionally scoped; rich typed editors are currently focused on cron calendars/triggers and operational overrides
- UX is optimized for Quartz APIs and scheduler operations, not full workflow/business process visualization
