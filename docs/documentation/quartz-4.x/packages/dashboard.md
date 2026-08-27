---
title: Dashboard
---

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor-based dashboard for Quartz.NET that runs inside your ASP.NET Core app and renders the schedulers registered in that same application.

::: warning
Quartz Dashboard is currently a work in progress.
The dashboard API surface may change between releases.
:::

## Features

Ten pages, listed under [The pages](#the-pages). What they are built on, which is what decides where
the dashboard fits:

- **It reads the schedulers in its own process**, through the `IQuartzApiClient` in the container. No
  address to configure and no scheduler to point it at; a dashboard over another process is
  [#3387](https://github.com/quartznet/quartznet/issues/3387) and is 4.1.
- **Every scheduler the container knows about**, not just the default one — including a registration
  nothing has built yet, which is shown as such rather than omitted. The header's picker switches
  between them and every page follows it.
- **Cluster-aware wherever the store is.** With a persistent job store the executing view, the fire
  counts and the node listing are the whole cluster's rather than this process's, and the pages say
  which of the two you are looking at.
- **Its own execution history**, installed and populated without anything further being written, and
  bounded by age as well as by count. It is in-memory and per-process unless you
  [give it a store of your own](#execution-history-and-misfires).
- **A live event stream** over SignalR, fed by plugins installed into every scheduler in the container.
- **Authorization at three levels that compose** — who reaches the dashboard, which schedulers they see
  once they are in, and whether anyone may change anything. See
  [Production hardening](#production-hardening).
- **A time zone picker and a theme toggle** in the header. The picker is what the pages render their
  absolute times in, so a cluster spanning regions can be read in one; the times that are more useful
  as an age — a last check-in, a last fire — are shown relative as well.

## Installation

The dashboard package builds on `Quartz.AspNetCore` and brings it along, so one reference is enough:

```shell
dotnet add package Quartz.Dashboard
```

## Basic setup

Configure Quartz, enable the HTTP API, and add the dashboard services.

<!-- snippet: sample_dashboard_registration -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz();

builder.Services.AddQuartzHttpApi(options =>
{
    options.ApiPath = "/quartz-api";
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

The [HTTP API](http-api.md) is enabled above because it is useful alongside the dashboard, not because
the dashboard needs it: the pages read the schedulers in this process directly.

## Options

`AddQuartzDashboard(options => …)` takes six settings, and none of them points the dashboard at a
scheduler — **the dashboard renders the schedulers registered in its own application**, reading them
through the `IQuartzApiClient` in the container rather than over a network.

| Option | Default | What it does |
|---|---|---|
| `DashboardPath` | `/quartz` | The base path the UI is served from — see [Hosting under a custom path](#hosting-under-a-custom-path) |
| `AuthorizationPolicy` | none | The policy applied to the dashboard pages, hub, circuit and assets — see [Policy and role-based authorization](#policy-and-role-based-authorization) |
| `SchedulerAuthorizationPolicy` | none | The policy each *scheduler* is held to, evaluated against that scheduler — see [One scheduler at a time](#one-scheduler-at-a-time) |
| `ReadOnly` | `false` | Hides every mutating action: no pause, resume, trigger-now, reschedule, unschedule or delete |
| `HistoryRetention` | 24 hours | How far back the dashboard's own history store keeps executions and misfires — see [Execution history and misfires](#execution-history-and-misfires) |
| `HistoryMaxEntriesPerScheduler` | `2000` | How many executions and how many misfires it keeps per scheduler, oldest dropped first |

Both history bounds are rejected at startup if they are not positive: a window of zero forgets every
execution the moment it is recorded, which looks exactly like a history plugin that was never installed.

::: tip Pointing a dashboard at another process
There is no option for it. `AddQuartzDashboard` registers its client with `TryAdd`, so an application
can register its own `IQuartzApiClient` and have the pages read whatever it likes; a supported remote
dashboard — with the authentication forwarding, execution limits and history story such a thing needs —
is designed in [#3387](https://github.com/quartznet/quartznet/issues/3387).
:::

## The pages

Ten of them, all served under `{DashboardPath}` — `/quartz` unless you said otherwise. Every one of
them renders the scheduler the header's picker has selected, so switching schedulers keeps you on the
page you were reading.

This section says what each page shows. What the numbers on them *mean* for a cluster in trouble is
[Operating a Cluster](../operations.md), which these pages link into rather than restate.

### Overview

`/quartz` — the page a scheduler is judged from. It opens with the scheduler's status and — outside read-only mode —
the controls that change it: start, stand-by, pause all, resume all and shutdown.

Beside the totals — jobs, triggers, firings in flight, triggers in error, nodes — it carries four
breakdowns, because a total is rarely the thing that explains why work is not being done:

- A **trigger-state histogram**: how many triggers are `Normal`, `Paused`, `Blocked`, `Error` and
  `Complete`. Each count is a link to the Triggers page already narrowed to that state, which is also
  what `/quartz/triggers?state=Paused` does for any state you name. The counts are counting queries, so
  a scheduler with a hundred thousand triggers costs the same as one with ten.
- A **paused-group** tile: how many trigger groups and how many job groups are paused, both kinds in one
  tile because pausing a trigger group and pausing a job group are different acts with the same
  consequence. A group can be paused while it holds nothing, and this counts such a group — which is
  exactly the one an operator cannot find by looking at a listing, and a common and entirely silent
  answer to [Nothing is firing](../operations.md#nothing-is-firing).
- A **misfire** tile: how many firings the scheduler missed inside the history store's retention window,
  which the tile names — `Misfires (last 24 h)` at the default `HistoryRetention`, and whatever you
  configured otherwise, so the label cannot promise a day a store set to remember an hour has already
  forgotten. A data source that keeps no misfire feed shows a dash rather than a zero: it has not looked,
  which is not the same as having looked and found none. See
  [Execution history and misfires](#execution-history-and-misfires).
- An **execution-group panel**: one row per [execution group](../tutorial/execution-groups.md), with the
  limit that governs it, the scope that limit is counted in, what the group has in flight, and the
  headroom left. It is described under [Execution groups](#execution-groups) below.

The **Nodes** tile is the one with a verdict in it: it carries the node count, and beside it the number
that are not `Alive` when there are any — a cluster of four is only news when one of them has stopped
checking in. It is a link to the Cluster page, and it is shown only for a store that has nodes to count.
Below the breakdowns the page ends with the most recent entries from the [Action Log](#action-log).

### Jobs, Triggers and Calendars

`/quartz/jobs`, `/quartz/triggers`, `/quartz/calendars` — the three listings, each with search and
pagination, each row a link to a detail page.

- **Jobs** lists the job details and their keys; the detail page shows the job's `JobDataMap`, the
  triggers pointing at it, and — outside read-only mode — trigger-now with overrides, pause, resume and
  delete.
- **Triggers** lists triggers with their state, their next and previous fire times and their execution
  group. `?state=` opens it already narrowed, which is what the overview's histogram counts link to. The
  detail page shows the trigger's own `JobDataMap`, and outside read-only mode offers pause, resume,
  unschedule, *reset error state* — the one that clears an `ERROR` trigger once the reason for it is
  fixed — and, for a cron trigger, an editor that reschedules it with a preview of its next five fires.
- **Calendars** lists the calendars by name; the detail page shows one, and outside read-only mode a
  cron calendar can be created, replaced or deleted.

### Currently Executing

`/quartz/executing` — one row per firing: job, trigger, node, execution group, fire time and run time. It is the fire-instance
listing, so **with a persistent job store it is the whole cluster's**, and the `Node` column is which
machine owns each firing rather than decoration.

Interrupting from here interrupts *the one firing the row names*, not every firing of its job — the
distinction matters for a job without `[DisallowConcurrentExecution]`, which can have several in flight.

A row that will not go away is worth reading
[Fired triggers: backlog or leak](../operations.md#fired-triggers-backlog-or-leak) about: a firing whose
node died leaves its row behind until another node's check-in sweep takes it over.

### Schedulers

`/quartz/schedulers` — the fleet: one row per scheduler the container knows about, whether or not anything has built it. It
reads `ISchedulerRegistry`, so a scheduler registered with `AddQuartz("acme", …)` that nothing has
resolved yet is listed with its origin and a **not created** status rather than being absent — and
listing it does not build it.

Each row that has a scheduler behind it shows what that scheduler is made of, read from its
`SchedulerMetadata`: its instance id, whether its job store is persistent and whether it is clustered,
the store and thread pool it uses, the pool size, when it started (in the time zone the header picker
selects), how many jobs it has executed, and the version of Quartz running it. The node count beside it
comes from the cluster-node query, and only for a store that has nodes — a persistent, clustered one. An
in-memory store is never asked, because the answer would always be "one".

Following a row makes that scheduler the active one and opens its Overview page, which is the same
switch the header's scheduler picker makes. A registration nothing has built is not a link: there is no
scheduler behind it for any page to show. The picker offers it too, greyed out, so that a tenant that
failed to start is visible rather than looking as though it had never been registered. Should such a
registration end up being the active scheduler anyway — it is the only one there is, or the one that was
running has just been shut down — the Overview page says it has not been created rather than reporting a
scheduler it could not find.

### Cluster

`/quartz/cluster` — one row per node of the selected scheduler's cluster, refreshed every five seconds, with the time of
the last refresh in the header so a stalled page is visible as one. The columns are the node's instance
id, its state, its last check-in, its check-in interval, and how many firings it holds `Acquired` and
how many it is `Executing`. The node answering is marked *(this node)*.

The state is `Alive`, `Overdue` or `Failed`, and it is `IScheduler.QueryClusterNodes()`'s verdict —
decided by the same predicate the store's own recovery sweep applies, so the page and the sweep cannot
disagree. What the three mean, why a `Failed` node is listed for a short while and then vanishes, and
what it means when one does not, are in
[Check-in, node states and failover](../operations.md#check-in-node-states-and-failover) and
[Reading the cluster](../operations.md#reading-the-cluster).

**A scheduler whose job store is not clustered says so** rather than showing an empty table: such a
store keeps no check-in state, so the only node is this one. The check-in times are the ones that node
was configured with rather than the reader's, and the verdicts are read off the answering node's clock —
on a cluster with skewed clocks two nodes can disagree about a third.

### Execution History

`/quartz/history` — covered in full under [Execution history and misfires](#execution-history-and-misfires): one row per
execution with the node that ran it, a node filter, four stat cards whose titles say which scope their
figures cover, and a misfires section beneath.

### Live Logs

`/quartz/live` — the scheduler's events as they happen, over the SignalR hub, fed by a plugin `AddQuartzDashboard`
installs into every scheduler in the container. Every event names the node that raised it, and the page
says which node its own process is — which is how a clustered scheduler's stream is readable rather
than an undifferentiated blur.

Events can be narrowed by type from the header, which is what makes a busy scheduler readable.

It is a live view, not a log: it starts when the page opens, holds the newest hundred events and drops
the rest. Nothing here survives a reload, and nothing here is the record — see
[Current limitations](#current-limitations).

### Action Log

`/quartz/actions` — what was done *from this dashboard*: time, scheduler, action, target, whether it succeeded and any
message, newest first. It is the audit trail for the buttons, and it answers "who paused this" for a
value of "who" that is the dashboard rather than a user.

The store behind it is in-memory and process-wide, holding the last 250 actions across every scheduler;
the page takes the most recent 100 of those and shows the ones aimed at the scheduler you have selected,
so switching schedulers re-filters it. That makes it a recent-activity view rather than an audit store —
and it records only what *this process's dashboard* did. An action taken through the HTTP API, from
another node, or by another operator's dashboard is not in it, and nothing in it survives a restart.

## Execution groups

The panel joins the limits the scheduler is running with — `IScheduler.GetExecutionLimits` — to the
firings it has in flight, so that ceilings set in configuration are visible in the UI rather than only
in the file that set them.

- **Both firing states count.** A reservation holds a slot exactly as a running execution does, which is
  what the acquisition filter counts against a limit; a panel that counted only the running ones would
  show headroom the next acquisition cannot use.
- **The counts are cluster-wide when the job store is persistent**, because the firing listing is: a
  firing owned by another node is one of them. With an in-memory store they are this node's, and the
  panel says which of the two you are looking at. Where a node-scoped limit is being compared against a
  cluster-wide count — a clustered store — the panel says that too, since every node enforces its own
  copy of such a limit.
- **`other groups` is a rule, not a bucket.** The catch-all gives each group with no limit of its own an
  allowance of its own rather than one shared between them, so it has nothing in flight against it. A
  group governed by it says where its number came from. It never covers the ungrouped bucket, which is
  the rule the scheduler applies.
- **A derived group is labelled.** With
  [`UseTriggerGroupWhenUnset`](../tutorial/execution-groups.md#letting-the-trigger-group-stand-in), a
  trigger with no execution group is limited as though it belonged to a group named after its trigger
  group; the panel resolves the key the same way and marks the row, so a group nobody typed is not a
  puzzle.
- **A scheduler that cannot report limits says so.** An `IQuartzApiClient` or `IScheduler` of your own
  may not implement them; the panel then says it cannot tell rather than rendering as though nothing
  were limited.

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

Which schedulers those are is [the Schedulers page](#schedulers) — every registration
in the container, built or not.

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

## Execution history and misfires

`AddQuartzDashboard()` installs the history plugin itself, so the **History** page at `/quartz/history`
is populated without anything further being written. Each row names the job, the trigger, the node that
ran it, when it fired, how long it took, whether it succeeded and the error if it did not.

Above the rows are four figures over the page in view — success rate, failures, average duration and
P95 duration — and above those three filters: by job, by trigger, and by node. **The node filter is the
one a cluster needs**: it narrows the listing to one machine, and the stat cards' titles then say so, so
a success rate cannot be read as the fleet's when it is one node's. Without it a clustered scheduler's
history is an undifferentiated stream.

Beneath the executions the page lists **misfires**: firings the scheduler missed. Nothing ran, so they
never appear in the execution history however long a reader stares at it; each row names the trigger,
the job it points at, the node that noticed, the firing that was missed and when it was noticed.

The store `AddQuartzDashboard` registers is per-process and in-memory, bounded both by age
(`HistoryRetention`, 24 hours) and by count (`HistoryMaxEntriesPerScheduler`, 2000 of each feed per
scheduler):

<!-- snippet: sample_dashboard_history_bounds -->
```csharp
services.AddQuartzDashboard(options =>
{
    options.HistoryRetention = TimeSpan.FromHours(6);
    options.HistoryMaxEntriesPerScheduler = 500;
});
```
<!-- endSnippet -->

The age bound is what a count alone cannot supply: a scheduler that has gone quiet keeps whatever it
last recorded, so its page shows executions from an arbitrary distance in the past with nothing to say
how old they are. The window is measured on the scheduler's `TimeProvider`, and it applies when history
is read as well as when it is written — otherwise a scheduler that never writes again never forgets.

To keep history somewhere that survives a restart, register your own `IDashboardHistoryStore` before
calling `AddQuartzDashboard` (its registration is a `TryAdd`). Both feeds carry `SchedulerInstanceId`,
which is what makes one store shared by a whole cluster readable. It carries the `CountMisfires(name,
since)` count-over-a-window a summary needs, so an implementation backed by a database can answer that
with one `COUNT(*)` rather than by loading rows it would throw away.

### Enabling the history plugins

For history written to your application's log as well, add the Quartz history plugins to the scheduler:

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

### One scheduler at a time

`AuthorizationPolicy` decides who reaches the dashboard; it says nothing about *which* of the schedulers
in the process they then see, and by default they see all of them. Name a policy in
`SchedulerAuthorizationPolicy` and each scheduler is authorized on its own, evaluated as
`IAuthorizationService.AuthorizeAsync(user, new SchedulerResource(name), policy)`:

- the scheduler picker and the **Schedulers** page offer only the schedulers the visitor passes for;
- a page opened on one they do not renders a *not authorized* frame, and reads nothing about that
  scheduler — the page is never created, so no `IQuartzApiClient` call is made on their behalf;
- the live-events hub refuses a connection's request to join that scheduler's group.

The three policies compose rather than replace one another: `AuthorizationPolicy` decides who is in,
`SchedulerAuthorizationPolicy` decides which schedulers they see, and `ReadOnly` decides what anyone may
change. `QuartzHttpApiOptions.SchedulerAuthorizationPolicy` takes the same policy against the same
resource, so one `AuthorizationHandler<TRequirement, SchedulerResource>` answers for the dashboard and the
HTTP API together. The worked example is in
[Multi-tenancy](../multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler).

::: warning Standalone hosting is where this applies today
The *not authorized* frame is drawn by the dashboard's own layout, which is in the render tree only when
the dashboard owns its Blazor root — the `MapQuartzDashboard()` overloads that take no components builder.
Every scheduler listing the dashboard writes is filtered in both hosting modes, and so is the hub
subscription, so nothing in the dashboard itself will ever point a visitor at a scheduler they fail the
policy for. But when the components are hosted under an application's own layout
([Integrating with an existing Blazor Server app](#integrating-with-an-existing-blazor-server-app)) the
frame is not rendered, so an application that routes a visitor to a scheduler by its own means is
responsible for not routing them to one they fail for.
:::

Leaving it unset is the behaviour every earlier release had: whoever passes `AuthorizationPolicy` sees
every scheduler in the process.

### Read-only mode

`ReadOnly = true` hides every mutating control the pages carry: pause, resume, trigger-now,
trigger-with-overrides, reschedule, reset-from-error-state, unschedule, interrupt, delete, calendar
create and replace, and the scheduler's own start, stand-by, pause-all, resume-all and shutdown. What
remains is every listing, every detail page and every live view — which is the whole of the dashboard's
diagnostic value.

It is one setting for the whole process, not per scheduler and not per operation. The shape to reach for
when different people need different powers is two dashboards: a read-only one for observers, and a
write-enabled one behind a policy only operators pass. `SchedulerAuthorizationPolicy` narrows *which
schedulers* each of those shows; it does not narrow what may be done to the ones it does show.

Note what read-only is not: it hides the dashboard's controls and does nothing to the HTTP API, which is
mapped and authorized separately. A dashboard in read-only mode over an API that anyone may post to is
read-only in appearance alone.

::: warning Fixed in 4.0.0-alpha.2
The dashboard's controls did not work. Blazor's event handlers and two-way binding are directive
attributes contributed by tag helpers in `Microsoft.AspNetCore.Components.Web`, and a tag helper only
applies where its namespace is in scope; the package carried no `_Imports.razor`, so the Razor
compiler emitted `@onclick`, `@bind`, `@oninput`, `@onchange` and `@onkeydown` as literal HTML
attributes -- silently, with nothing in the build to say so.

Nineteen components carry such an attribute and eighteen of them had every one dead: 69 handlers and
6 `@onclick:stopPropagation` / `:preventDefault` modifiers. The one exception was the layout, which
imported that namespace itself, so the theme and time zone pickers were the only working controls.
The confirmation dialog was among the eighteen, so every action that asks first -- delete a job,
unschedule a trigger, shut a scheduler down -- could not be completed even where the button that
opens the dialog was live.

There was nothing to configure to get these working; this is a fix rather than a new option.
:::

### API key or custom authorization checks

If you need machine-to-machine access, use your API auth scheme (for example, an API key handler) and bind that to a policy used by `MapQuartzHttpApi()`.
For dashboard-only custom checks, prefer ASP.NET Core policy/handler-based authorization so the dashboard UI, hub, and API are enforced consistently.

### Deployment guidance for multi-scheduler and clustered setups

- **Clustered ADO.NET job stores:** actions in dashboard are scheduler operations and can affect cluster behavior; restrict write access to trusted operator roles.
- **Many local schedulers in one host:** dashboard scheduler selector supports multiple registered schedulers; use clear scheduler names and environment-specific grouping. Where those schedulers are different tenants, `SchedulerAuthorizationPolicy` is what keeps them apart — see [Multi-tenancy](../multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler).
- **Reverse proxy and Blazor Server:** enable WebSocket/SignalR forwarding and sticky sessions where required by your hosting stack. The Blazor circuit connects to `/_blazor` (or `{DashboardPath}/_blazor` when a custom `DashboardPath` is configured).
- **Split operator experiences:** expose a read-only dashboard instance (`ReadOnly = true`) for observers, and a separate write-enabled dashboard for operators.
- **Operational retention:** the built-in history store is per-process and in-memory, bounded by `HistoryRetention` and `HistoryMaxEntriesPerScheduler`. Every node of a cluster therefore keeps its own; the rows name the node they came from, so registering a shared `IDashboardHistoryStore` gives one page over the whole fleet. Configure external retention/reporting if you need long-term analytics.

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

### What integrated hosting changes

Three things behave differently when the components are hosted under an application's own Blazor root
rather than the dashboard's. Everything else on this page applies to both modes.

| | Standalone (`MapQuartzDashboard()`) | Integrated (`MapQuartzDashboard(blazor)`) |
|---|---|---|
| `DashboardPath` | Honoured; the whole dashboard is self-contained under it | **Rejected** — the page routes are fixed at `/quartz`, and a custom path fails startup with a descriptive exception. There is no `MapQuartzDashboard(blazor, pattern)` for the same reason |
| The Blazor circuit and framework assets | Dashboard-owned endpoints, which carry the dashboard's own authorization metadata | The host's — so a fail-closed `FallbackPolicy` governs them, and `AllowAnonymous` on your static assets is yours to decide |
| The *not authorized* frame | Drawn, because the dashboard's layout is in the render tree | **Not drawn** — see [One scheduler at a time](#one-scheduler-at-a-time) |

What does *not* change is every listing the dashboard writes and every hub subscription it makes: both
are filtered by `SchedulerAuthorizationPolicy` in either mode, so the dashboard itself will never offer
a visitor a scheduler they fail for. The gap is only an application that routes a visitor to one by
means of its own.

## API-only projects (no .razor files)

If your host project has no `.razor` files of its own (for example a pure API project hosting Quartz), you must add the following to your project file:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

This property tells the .NET SDK to include the Blazor framework scripts (`_framework/blazor.web.js`, `blazor.server.js`) in the app's static web assets. Without it, requests to `/_framework/blazor.web.js` return HTTP 404: as of .NET 10 these files are no longer embedded in the ASP.NET Core assemblies — they are served as static web assets instead.

## Current limitations

- **The dashboard renders its own process.** There is no address to point it at another one; a remote
  dashboard is [#3387](https://github.com/quartznet/quartznet/issues/3387) and is 4.1.
- **Nothing here is the record.** Live Logs is a live view that starts when the page opens and keeps a
  hundred events; the Action Log keeps 250 and only what this process's dashboard did. Neither survives
  a restart, and neither is lossless — use logging and
  [metrics](opentelemetry-integration.md) for anything you need to be able to go back to.
- **The history store is in-memory and per-process**, so history does not survive a restart and one
  node cannot show another's unless you register a shared `IDashboardHistoryStore`. A database-backed
  one ships with 4.1.
- **Read-only is one setting for the whole process**, not per scheduler and not per operation — "acme
  may look, globex may act" and "this tenant may pause but not delete" are not expressible. Which
  *schedulers* a visitor sees is expressible; see [One scheduler at a time](#one-scheduler-at-a-time).
- **The misfire tile and the Cluster page ask the store what it can answer.** A data source with no
  misfire feed shows a dash rather than a zero, and a job store with no check-in state reports the one
  node it is. Neither is a count of zero, and the pages say so rather than implying otherwise.
- **Typed editors are deliberately narrow**: cron calendars, cron trigger reschedules and job-data
  overrides. Building an arbitrary trigger of any type from the UI is not offered.
- **It is a scheduler console, not a workflow tool.** Job dependencies, DAGs and business-process
  visualisation are outside what Quartz itself models, so they are outside this.
