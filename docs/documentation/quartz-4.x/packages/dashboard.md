---
title: Dashboard
---

[Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) is a Blazor dashboard for Quartz.NET. It runs
inside your ASP.NET Core application and renders the schedulers registered in that application.

## Features

Fourteen pages, listed under [The pages](#the-pages).

- **Reads the schedulers in its own container**, through the container's `IQuartzApiClient`: usually the
  schedulers this process runs, with nothing to configure. A scheduler in *another* process is registered with
  `AddQuartzHttpClient`; see
  [Fronting a scheduler in another process over HTTP](#fronting-a-scheduler-in-another-process-over-http).
- **Or shows every scheduler in a database**, with nothing asked of the processes running them. `AttachStore`
  discovers them and shows each as a window on the shared store; use it for a cluster behind a load balancer.
  See [Store-attached targets](#store-attached-targets).
- **Every scheduler the container knows**, including a registration nothing has built yet (shown as such). The
  header's picker switches between them and every page follows.
- **Cluster-aware.** With a persistent job store, the executing view, fire counts and node listing cover the
  whole cluster, and the pages say which scope they show.
- **Execution history**, installed automatically and bounded by age and count. It is in-memory and per-process
  unless you [give it a store](#execution-history-and-misfires); for a scheduler in another process it is read
  from that process.
- **A live event stream** from Quartz itself: in process for a local scheduler, over the HTTP API's event route
  for [a remote one](#fronting-a-scheduler-in-another-process-over-http). The pages do not connect back to the
  dashboard's hub; the hub is still served and fed for your own clients.
- **Three composable authorization levels**: who reaches the dashboard, which schedulers they see, and whether
  anyone may change anything. See [Production hardening](#production-hardening).
- **A time zone picker and a theme toggle** in the header. Absolute times render in the picked zone; ages such
  as a last check-in or last fire are also shown relative.

## Installation

The package brings `Quartz.AspNetCore` with it:

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

// the dashboard's stylesheet and scripts are static web assets, so something
// has to serve them
app.MapStaticAssets();

app.MapQuartzHttpApi().RequireAuthorization();
app.MapQuartzDashboard().RequireAuthorization();
```
<!-- endSnippet -->

The dashboard UI is at `/quartz` by default.

- The [HTTP API](http-api.md) is optional: the pages read local schedulers directly.
- The order of the four `Add…` calls does not matter; nothing is built until the service provider is.

Three pipeline lines are required by the host:

- **`app.MapStaticAssets()`** (or the older `app.UseStaticFiles()`) serves the dashboard's stylesheet and
  scripts, static web assets under `_content/Quartz.Dashboard/`. Without it they answer 404 and the pages render
  unstyled and inert. A project with no `.razor` files also needs
  [`<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>`](#api-only-projects-no-razor-files), or
  `/_framework/blazor.web.js` is missing and nothing responds to a click.
- **`app.UseAuthentication()`** needs a registered scheme, such as `AddAuthentication(…).AddCookie()` or an
  OpenID Connect handler. Quartz authorizes but does not authenticate. With no scheme registered,
  `UseAuthentication()` fails at startup, resolving `IAuthenticationSchemeProvider`.
- **`.RequireAuthorization()`** satisfies the startup check below.

::: danger A mapping that says nothing about authorization does not start
The dashboard has no authentication of its own. Its pages start, stand by, shut down, pause, resume, delete and
trigger, and `ReadOnly` is `false` by default. `AddJob`, as on the HTTP API, takes a job's type as a **string from
the request**, resolved later with `Type.GetType` against the host's probing path. With
[`Quartz.Jobs`](quartz-jobs.md) on that path the string can reach `NativeJob`, which starts a process named in job
data: an unauthenticated dashboard is remote code execution. Its execution history also shows every job
exception's message.

So `app.MapQuartzDashboard()` with no authorization statement fails at startup, before the server binds its
listener. The message names the three statements:

- `app.MapQuartzDashboard().RequireAuthorization()` authorizes its pages and live-events hub;
- `QuartzDashboardOptions.AuthorizationPolicy` authorizes those plus the Blazor circuit and static assets; see
  [Policy and role-based authorization](#policy-and-role-based-authorization);
- `app.MapQuartzDashboard().AllowAnonymous()` serves it to anyone, deliberately.

A non-null `AuthorizationOptions.FallbackPolicy` also satisfies the check. An application that calls
`AddQuartzDashboard()` and never maps it is not checked.
:::

::: tip What `MapQuartzDashboard` returns
An `IEndpointConventionBuilder` covering the pages **and** the live-events hub, so `RequireAuthorization()` on it
covers the whole dashboard. The `RazorComponentsEndpointConventionBuilder` of the pages alone would miss the hub,
and in the integrated overload would also reach the host application's own pages.
:::

## Options

`AddQuartzDashboard(options => …)` takes seven settings. None points the dashboard at a scheduler: **it renders
the schedulers registered in its own application**, through the container's `IQuartzApiClient`.

| Option | Default | What it does |
|---|---|---|
| `DashboardPath` | `/quartz` | Base path of the UI; see [Hosting under a custom path](#hosting-under-a-custom-path) |
| `AuthorizationPolicy` | none | Policy for pages, hub, circuit and assets; see [Policy and role-based authorization](#policy-and-role-based-authorization) |
| `SchedulerAuthorizationPolicy` | none | Policy each *scheduler* is held to; see [One scheduler at a time](#one-scheduler-at-a-time) |
| `ReadOnly` | `false` | Hides every mutating action (pause, resume, trigger-now, reschedule, unschedule, delete, …) |
| `IsJobTypeAllowed` | none | Predicate over a job type *name*; refused raises `UnauthorizedAccessException`; see [Narrowing which job types may be named](#narrowing-which-job-types-may-be-named) |
| `HistoryRetention` | 24 hours | How long the history store keeps executions and misfires; see [Execution history and misfires](#execution-history-and-misfires) |
| `HistoryMaxEntriesPerScheduler` | `2000` | Executions and misfires kept per scheduler each, oldest dropped first |

Both history bounds must be positive or startup fails; a zero window would forget every execution immediately
and look like a missing history plugin.

::: tip Pointing a dashboard at another process
Register that scheduler with `AddQuartzHttpClient`; the dashboard renders it beside the local ones. See
[Fronting a scheduler in another process over HTTP](#fronting-a-scheduler-in-another-process-over-http).
`AddQuartzDashboard` registers its client with `TryAdd`, so you can register your own `IQuartzApiClient` to make
the pages read something else.
:::

### Writing your own `IQuartzApiClient`

An implementation must follow the interface's two promises to render like the shipped one:

- **A missing thing is a `KeyNotFoundException`.** Every member taking a `schedulerName` throws it for an
  unknown scheduler; `GetScheduler`, `GetJobDetail`, `GetTrigger` and `GetCalendar` also throw it when the item
  is gone. The dashboard's error boundary shows the not-found page. Returning `null!` faults the page instead.
- **An unreportable value is a value, not a refusal.** `GetExecutionLimits` returns
  `ExecutionLimitsDto.CannotReport` when the source cannot say; the overview draws that differently from "no
  limits".

**Members added during 4.x arrive as default interface members**, so your implementation keeps compiling. The
default body reports the datum as unavailable, like `CannotReport`; override it when your source can answer.

## The pages

All fourteen are under `{DashboardPath}` (`/quartz` by default) and render the scheduler selected in the header's
picker, so switching schedulers keeps you on the same page.

| Page | Route |
|---|---|
| Overview | `/quartz` |
| Jobs, and one job | `/quartz/jobs`, `/quartz/jobs/{Group}/{Name}` |
| Triggers, and one trigger | `/quartz/triggers`, `/quartz/triggers/{Group}/{Name}` |
| Calendars, and one calendar | `/quartz/calendars`, `/quartz/calendars/{CalendarName}` |
| Currently Executing | `/quartz/executing` |
| Schedulers | `/quartz/schedulers` |
| Cluster | `/quartz/cluster` |
| Execution History, and one execution | `/quartz/history`, `/quartz/history/{EntryId}` |
| Live Logs | `/quartz/live` |
| Action Log | `/quartz/actions` |

Detail pages are covered with their listing below. What the numbers mean for a cluster in trouble is in
[Operating a Cluster](../operations.md).

### Overview

`/quartz` shows the scheduler's status and, outside read-only mode, start, stand-by, pause all, resume all and
shutdown. Beside the totals (jobs, triggers, firings in flight, triggers in error, nodes) are four breakdowns:

- **Trigger-state histogram**: counts of `Normal`, `Paused`, `Blocked`, `Error` and `Complete` triggers. Each
  links to the Triggers page filtered to that state, as `/quartz/triggers?state=Paused` does for any state. The
  counts are counting queries, so cost does not grow with trigger count.
- **Paused groups**: how many trigger groups and job groups are paused, in one tile. It counts empty paused
  groups too, which no listing shows and which are a common silent cause of
  [Nothing is firing](../operations.md#nothing-is-firing).
- **Misfires** within the history retention window, which the label names (`Misfires (last 24 h)` at the default
  `HistoryRetention`). A source with no misfire feed shows a dash, not a zero. Links to `/quartz/history#misfires`;
  see [Execution history and misfires](#execution-history-and-misfires).
- **Execution groups**: one row per [execution group](../tutorial/execution-groups.md) with its limit, the
  limit's scope, firings in flight and remaining headroom; see [Execution groups](#execution-groups).

The **Nodes** tile shows the node count and, when any exist, the number not `Alive`, turning red then. It links
to the Cluster page. A non-clustered scheduler shows `1`.

The page ends with recent [Action Log](#action-log) entries.

### Jobs, Triggers and Calendars

`/quartz/jobs`, `/quartz/triggers` and `/quartz/calendars` are searchable, paged listings; each row links to a
detail page.

| Listing | Detail page shows | Actions outside read-only mode |
|---|---|---|
| **Jobs**: job details and keys | the `JobDataMap` and the triggers pointing at the job | trigger-now with overrides, pause, resume, delete |
| **Triggers**: state, next and previous fire times, execution group | the trigger's `JobDataMap`, its [retry policy](../how-tos/retrying-failed-jobs.md), retries made for the current occurrence | pause, resume, unschedule, *reset error state*, and a cron reschedule editor |
| **Calendars**: names | one calendar | create, replace or delete a cron calendar |

- `?state=` opens the trigger listing filtered, as the overview's histogram links do.
- *Reset error state* clears an `ERROR` trigger once its cause is fixed.
- The cron reschedule editor previews the next five fires.

### Continuations

A [continuation](../how-tos/job-continuations.md) is a trigger waiting for another trigger's firing; it
deliberately does not fire on its own.

- The trigger listing has an **Awaiting only** filter beside *Error only* and *Executing only*.
- Waiting has its own colour in the state column, and each waiting row names the trigger it waits for, with the
  releasing outcomes on hover.
- The detail page shows **Continues after**, linking to the parent, and **When**.
- A removed parent is named without a link and marked *no longer scheduled*. The trigger is then in `ERROR`;
  *reset error state* runs it, giving it the fire time a release would have.
- A released or reset trigger waits for nothing and shows neither field.

### Currently Executing

`/quartz/executing` has one row per firing: job, trigger, node, execution group, fire time, run time and
progress. It is the fire-instance listing, so **with a persistent job store it covers the whole cluster**;
`Node` is the machine that owns the firing.

- **Progress** is a bar, the percentage and the job's message, from
  [`ReportProgress`](../how-tos/progress-and-execution-logs.md#report-progress). A firing that has not
  reported shows a dash. It is read from the store, so a store-attached window shows it too, and it can
  trail the job by up to a second.

- Interrupting interrupts *that one firing*, not every firing of the job. This matters for a job without
  `[DisallowConcurrentExecution]`, which can have several in flight.
- Each interrupt is recorded in the [Action Log](#action-log) with the fire instance and node, whether or not the
  firing was still running.
- A row that never goes away: a firing whose node died stays until another node's check-in sweep takes it over.
  See [Fired triggers: backlog or leak](../operations.md#fired-triggers-backlog-or-leak).

### Schedulers

`/quartz/schedulers` has one row per scheduler the container knows, built or not. It reads
`ISchedulerRegistry`, so a scheduler registered with `AddQuartz("acme", …)` and not yet resolved is listed with
its origin and **not created** status. Listing does not build it.

A built scheduler's row shows, from its `SchedulerMetadata`: instance id, whether the job store is persistent and
clustered, store and thread pool types, pool size, start time (in the header's time zone), jobs executed, and the
Quartz version. The node count comes from the cluster-node query, asked only of a persistent, clustered store.

- Following a row selects that scheduler and opens its Overview, like the header's picker.
- A registration nothing has built is not a link. The picker shows it greyed out, so a tenant that failed to
  start is visible.
- If such a registration becomes the active scheduler (it is the only one, or the running one was shut down),
  the Overview says it has not been created.

### Cluster

`/quartz/cluster` has one row per node of the selected scheduler's cluster, refreshed every five seconds, with
the last refresh time in the header so a stalled page is visible.

- Columns: instance id, state, last check-in, check-in interval, and firings held `Acquired` and `Executing`.
- The answering node is marked *(this node)*.
- The state (`Alive`, `Overdue` or `Failed`) is `IScheduler.QueryClusterNodes()`'s verdict, from the same
  predicate the store's recovery sweep uses. Meanings, and why a `Failed` node is listed briefly and then
  vanishes: [Check-in, node states and failover](../operations.md#check-in-node-states-and-failover) and
  [Reading the cluster](../operations.md#reading-the-cluster).
- **A non-clustered store says so** instead of showing an empty table: the only node is this one.
- Check-in intervals are each node's own configuration. Verdicts use the answering node's clock, so with skewed
  clocks two nodes can disagree about a third.

### Execution History

`/quartz/history`: one row per execution with the node that ran it, a node filter, four stat cards whose titles
name their scope, and a misfires section. Details under
[Execution history and misfires](#execution-history-and-misfires).

Each row's fire time links to `/quartz/history/{EntryId}`, the execution's own page: job, trigger, node, fire
time, duration, status, attempt, what it threw, and the lines it logged when the scheduler
[captures them](../how-tos/progress-and-execution-logs.md#keep-a-job-s-log-lines). It only reads, so read-only
mode leaves it unchanged.

### Live Logs

`/quartz/live` shows the scheduler's events as they happen, from Quartz's event stream.
`AddQuartzDashboard()` calls `AddQuartzSchedulerEvents()`, which installs one publisher into every scheduler in
the container; the page subscribes for the scheduler on screen.

- Every event names the node that raised it, and the page says which node its own process is.
- **A remote scheduler has a feed too**, read through the API's [event route](http-api.md#the-event-stream). A
  target whose API predates the route says so instead of looking idle.
- Each row shows the job, trigger, firing, run time and outcome.
- Fourteen kinds arrive; thirteen are shown (the heartbeat is consumed). Filter by type from the header. The
  interrupted-firing and trigger-in-error kinds are new in 4.1.
- It is a live view, not a log: it starts when the page opens, keeps the newest hundred events, and does not
  survive a reload; see [Current limitations](#current-limitations).
- The page opens no connection of its own, so it works behind a proxy that forwards only the dashboard's path.
  The SignalR hub is still served and fed for your own clients; a proxy must forward `{DashboardPath}/hub` only
  for those.

### Action Log

`/quartz/actions` lists what was done *from this dashboard*, newest first: time, scheduler, action, target, where
it landed, success, and any message. It answers "who paused this" when "who" is the dashboard.

- The store is in-memory and process-wide, holding the last 250 actions across schedulers. The page shows the
  most recent 100 of those that target the selected scheduler.
- It records only this process's dashboard. An HTTP API action is logged where the API is mapped, as event
  `9007`. Other nodes' and other dashboards' actions are not here, and nothing survives a restart.

**Where each action landed:**

- The scheduler's [origin](#fronting-a-scheduler-in-another-process-over-http) is a tag on the row. `Remote`
  adds *in another process*: that scheduler performed the action.
- A **node-local** action names the node it reached: interrupting a firing, start, stand-by, shutdown.
- A cluster-wide action (pausing a trigger, deleting a job) names no node: it wrote the store, which binds every
  node.
- A scheduler this browser session never listed shows no origin.

**Every entry is also logged** at `Information` through the application's `ILogger`: event `9100` on success,
`9101` on failure, each naming the visitor, action, target and scheduler, ending with `(origin …, node …)`. This
copy survives a restart. Connections opening and closing are logged at `Debug` (`9102`, `9103`). See
[Log Events](../log-events.md).

The visitor is `ClaimsPrincipal.Identity.Name`, or `(anonymous)` when nothing authenticated, which is every entry
for a dashboard mapped with `AllowAnonymous()`.

## Execution groups

The Overview's panel joins the scheduler's limits (`IScheduler.GetExecutionLimits`) to its firings in flight, so
configured ceilings are visible in the UI.

- **Both firing states count.** A reservation holds a slot like a running execution, as the acquisition filter
  counts it.
- **Counts are cluster-wide with a persistent store**, because the firing listing is; with an in-memory store
  they are this node's. The panel names the scope, and flags a node-scoped limit compared against a cluster-wide
  count (each node enforces its own copy of such a limit).
- **`other groups` is a rule, not a bucket.** It gives each unlimited group its own allowance, so nothing is in
  flight against it. A group it governs says so. It never covers the ungrouped bucket, matching the scheduler.
- **A derived group is labelled.** With
  [`UseTriggerGroupWhenUnset`](../tutorial/execution-groups.md#letting-the-trigger-group-stand-in), a trigger
  with no execution group is limited as if in a group named after its trigger group; the panel resolves and
  marks it the same way.
- **A scheduler that cannot report limits says so**, for an `IQuartzApiClient` or `IScheduler` of your own that
  does not implement them.

## The schedulers the dashboard covers

`AddQuartzDashboard()` calls `AddQuartzExecutionHistory()` and `AddQuartzSchedulerEvents()`, which install
Quartz's execution recorder and event publisher into **every** scheduler in the container, through
`ConfigureAllQuartzSchedulers`, in any call order.

- A scheduler registered with `AddQuartz("acme", …)` gets Live Logs and History like the default one.
- Each scheduler gets its own instance of each, initialized with its name; both feeds are attributed to the
  producing scheduler.
- `DashboardHistoryPlugin` and `DashboardLiveEventsPlugin` are still public and work when named in a
  `quartz.plugin.*.type` key, but are not registered here. Registering one beside the pair above records every
  execution, or pushes every event, twice.
- The covered schedulers are those on [the Schedulers page](#schedulers): every registration, built or not.

## Fronting a scheduler in another process over HTTP

Register a scheduler from another process with `AddQuartzHttpClient` (see [HTTP Client](http-client.md)) and it
appears beside the local ones:

<!-- snippet: sample_dashboard_remote_http -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The credential and the timeout are the HttpClient's: the dashboard adds nothing of its own.
// A short timeout matters — every page reading this scheduler waits on it.
builder.Services.AddHttpClient("quartz", client =>
{
    client.BaseAddress = new Uri("https://scheduler.internal/quartz-api/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("X-Api-Key", "…");
});

// The scheduler's name has to be the one the target goes by: it is in every route.
builder.Services.AddQuartzHttpClient("QuartzScheduler", "quartz");

builder.Services.AddQuartzDashboard();
```
<!-- endSnippet -->

The target must map the HTTP API (`app.MapQuartzHttpApi()`), and the name given here must be its scheduler's
name, which is in every route.

| Page | Over HTTP |
|---|---|
| Overview, Jobs, Triggers, Calendars, Currently Executing, Cluster | Read through the API's routes |
| Every action (pause, resume, trigger now, reschedule, delete) | Performed by the target's scheduler, in its process |
| Execution History | The **target's own** history |
| Live Logs | The **target's own** events; a target too old for the route says so |
| Schedulers, and the header's picker | Listed as `Remote` |

Before pointing one at production:

- **One target is one process.** Behind a load balancer, node-local operations land on whichever node the
  balancer picks, and two page renders may reach two nodes.
  - Node-local: **interrupting a firing** (must reach the running node), **start, stand-by and shutdown** (act on
    the answering node), **node-scoped execution limits**, the node's figures in the scheduler details (instance
    id, running since, jobs executed, jobs executing here), and **live events** (the answering node's; each names
    its node).
  - Not node-local: with a persistent store, jobs, triggers and [firings in flight](#currently-executing) are the
    whole cluster's whichever node answers.
  - Point the client at a node's own address when it matters. Fronting several processes as one fleet is
    [#3387](https://github.com/quartznet/quartznet/issues/3387).
- **The credential is the `HttpClient`'s.** `QuartzDashboardOptions.AuthorizationPolicy` decides who opens the
  dashboard; the target sees only what the named client was configured with (a header, a handler, a
  certificate). Nothing is forwarded from the signed-in user.
- **Give the client a short timeout.** Pages reading that scheduler wait for it, and `HttpClient`'s default is
  100 seconds. The scheduler *listing* has its own deadline and shows a non-answering scheduler as `Unknown`, but
  job and trigger pages wait for the client.
- **The dashboard's `ReadOnly` and the API's are separate.** `QuartzDashboardOptions.ReadOnly` only hides this
  dashboard's controls. [`QuartzHttpApiOptions.ReadOnly`](http-api.md#serving-reads-only) on the target refuses
  every mutating route, including from a dashboard still showing buttons, which then reports the refusal.

A target whose HTTP API is older than 4.1 has no history routes. The History page says "this scheduler runs in
another process and its Quartz HTTP API does not serve execution history", and the Overview's misfire tile shows
a dash, not zero.

## Store-attached targets

Point the dashboard at **the database**, and every scheduler in it gets pages. Nothing is asked of the processes
doing the work (no port, plugin or change), and one attachment covers every scheduler and node in that database.

<!-- snippet: sample_dashboard_attach_store -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddQuartz();

builder.Services.AddQuartzDashboard(options => options.AttachStore("prod", store =>
{
    // The cluster's own store configuration, not an approximation of it: the window reads
    // the blobs the nodes wrote, so the dialect, the table prefix, the serializer and any
    // custom trigger serializers all have to be the ones they were written with.
    store.UseSqlServer(connectionString);

    // The nodes keep their history in the database, so the window can read it. Leave this
    // out and the window's History page has nothing to show: an in-memory history is the
    // process's own, and no node's process is this one.
    store.UseExecutionHistory();
}));
```
<!-- endSnippet -->

**Use this for a cluster behind a load balancer.** An HTTP target reaches an arbitrary node per render; a
store-attached target talks to no node.

### What a window is

A *window* is a scheduler this process builds over the same store, with
[`ZeroSizeThreadPool`](../how-tos/external-leader.md), and never starts (its store refuses to). It is the
documented admin-process pattern (a process that writes a schedule for others to run) plus discovery and
presentation.

Discovery queries distinct `SCHED_NAME` values in `QRTZ_SCHEDULER_STATE`, `QRTZ_TRIGGERS` and
`QRTZ_JOB_DETAILS`. A name in any of them is a scheduler: a non-clustered scheduler writes no check-in row, and
one with only durable, unscheduled jobs has no triggers. Discovery runs at start-up and then every
`AttachStoreOptions.RediscoveryInterval` (default one minute):

<!-- snippet: sample_dashboard_attach_store_rediscovery -->
```csharp
services.AddQuartzDashboard(options => options.AttachStore(
    "prod",
    store => store.UsePostgres(connectionString),

    // Every five minutes rather than every minute, for a database whose set of schedulers
    // changes rarely. null asks once, at start-up, and never again.
    attach => attach.RediscoveryInterval = TimeSpan.FromMinutes(5)));
```
<!-- endSnippet -->

Rediscovery only adds. A scheduler whose rows were deleted keeps its window and shows an empty schedule, rather
than a page vanishing under an operator.

### Identity is `target/name`

A window is shown as `prod/reporting`: the attach name, then the scheduler's `SCHED_NAME`. Schedulers of *this*
process keep their bare names.

- The target is a label, not part of the key: everything that takes a scheduler name takes the bare one.
- A name colliding with a scheduler this process already has is **refused, naming both**, logged as `4027`; the
  rest of the database is shown. That refusal is permanent.
- A window that fails to build for any other reason is logged as `4029` and retried next round, without
  stopping the others.
- Two stores attached under one target name are refused.

### Status comes from the cluster, never from the window

A window is never started, so its own `SchedulerStatus` is always `Created`. The Schedulers page, the picker and
the Overview derive status from `QRTZ_SCHEDULER_STATE` instead:

| Check-in rows under that `SCHED_NAME` | Reported |
|---|---|
| At least one node within its check-in interval plus the threshold | `Running`, with the node count |
| Rows, but every one convicted | `Shutdown`: every node that checked in has stopped |
| No rows | `Unknown` |

**A non-clustered scheduler writes no check-in row**, so a window cannot tell it from one never started, and
reports `Unknown` rather than "stopped". For liveness of a non-clustered scheduler, reach it over
[HTTP](#fronting-a-scheduler-in-another-process-over-http).

The window writes no check-in row and is never listed as a node; the Cluster page says whose rows it shows.

### What a window can and cannot do

**The store is the contract.**

| Available | Not available |
|---|---|
| Jobs, triggers, calendars, groups, paused groups: read, added, edited and deleted | `Start`, `Standby`, `Shutdown` |
| Pause, resume, reschedule, unschedule, trigger now | Interrupting a running job or a firing |
| Currently Executing, from `QRTZ_FIRED_TRIGGERS` | Live Logs and the live event stream |
| Cluster, from the nodes' check-ins | The node's own figures: instance id, running since, jobs executed |
| Execution History and the misfire tile, when the cluster keeps history in the database | |

- The left column writes the shared tables, and whichever node picks up the work honours it, as in any cluster.
- The right column belongs to **one process**; a database cannot instruct a node. The pages do not offer these,
  the client refuses them, and the Overview says so.
- The [HTTP API](http-api.md) of a process holding a window refuses `start`, `standby` and `shutdown` for it with
  a `400`. The window's store refuses a start from anywhere: starting would run the cluster's start-up here, whose
  recovery deletes the fired-trigger rows of firings the nodes are running.
- To reach one node for those, use an [HTTP target](#fronting-a-scheduler-in-another-process-over-http) or the
  agent target of [#3773](https://github.com/quartznet/quartznet/issues/3773).

### Three things to get right

- **Use the cluster's store recipe**: the same dialect,
  [table prefix](../configuration/reference.md#persistent-job-store), serializer and
  `UseTriggerPersistenceDelegate` registrations. A mismatched serializer fails on the first trigger it cannot
  rebuild.
- **`UseExecutionHistory()` on both sides.** The nodes must keep history in the database
  ([`UsePersistentStore(store => store.UseExecutionHistory())`](../tutorial/job-stores.md#execution-history-in-the-database)),
  and the attached store must say so too. Otherwise the History page says there is no history here. The window
  never writes or sweeps history; retention stays with the nodes.
- **The dashboard holds the database credentials**, as powerful as any node's. Whoever can open the dashboard can
  read and write the cluster's schedule. Use [`ReadOnly`](#read-only-mode) and
  [the authorization policies](#production-hardening), and do not attach a store across a trust boundary; that
  is what the agent target is for.

A window is not reported to the [health check](hosted-services-integration.md#health-checks):
`AddHealthChecks().AddQuartz("reporting")` in a dashboard process reports it unhealthy, with a message to check
the nodes instead.

## Hosting under a custom path

When the dashboard hosts its own Blazor root, set a custom base path at the map site:

<!-- snippet: sample_dashboard_map_path -->
```csharp
app.MapQuartzDashboard("/my-api/quartz").RequireAuthorization();
```
<!-- endSnippet -->

or at registration, when the path comes from configuration:

<!-- snippet: sample_dashboard_options_path -->
```csharp
services.AddQuartzDashboard(options =>
{
    options.DashboardPath = "/my-api/quartz";
});
```
<!-- endSnippet -->

- If both are given, **the pattern passed to `MapQuartzDashboard` wins**. The parameterless overload uses
  `DashboardPath`.
- A map-site pattern follows the option's rule: a plain URL path starting with `/`, with no `{`, `}`, `?`, `#`,
  `.` or `..` segments and no empty ones.

With a custom path, the whole dashboard is under it: pages, navigation links, the SignalR hub, the interactive
circuit (`{DashboardPath}/_blazor`), the framework script (`{DashboardPath}/_framework/blazor.web.js`) and the
static assets (`{DashboardPath}/_content/Quartz.Dashboard/*`). The shell emits a `<base href>` rooted at the
dashboard.

- **Behind a proxy that forwards a path prefix without setting a path base**, set `DashboardPath` to the external
  path (e.g. `/my-api/quartz` when the proxy forwards `/my-api/*` verbatim), and forward WebSocket connections
  for `{DashboardPath}/_blazor`, which every page needs.
- `{DashboardPath}/hub` is the live-events hub. From 4.1 **the dashboard's own pages do not use it**, so the
  application need not reach its own public address for Live Logs. Forward it only for your own clients.
- **With `UsePathBase()`** (or a proxy that sets the path base), `DashboardPath` is relative to the path base,
  and the default `/quartz` works under the prefix. With minimal hosting (`WebApplication`), call
  `app.UseRouting()` explicitly **after** `app.UsePathBase(...)`, or routing matches the unstripped path and every
  dashboard route returns 404:

<!-- snippet: sample_dashboard_path_base -->
```csharp
app.UsePathBase("/my-api");
app.UseRouting();
```
<!-- endSnippet -->

::: warning Upgrading existing custom-path deployments
The Blazor circuit used to stay at the site root. With a custom `DashboardPath` it now connects at
`{DashboardPath}/_blazor`; update reverse-proxy rules scoped to `/_blazor`, such as a WebSocket-upgrade location.
:::

::: warning
A custom path is **not** supported with `MapQuartzDashboard(blazor)` (integrating into an existing Blazor
application). Page routes are fixed at `/quartz` there, and a custom path fails startup with a descriptive
exception. There is no `MapQuartzDashboard(blazor, pattern)` overload.
:::

## Execution history and misfires

`AddQuartzDashboard()` calls `AddQuartzExecutionHistory()`, so the **History** page at `/quartz/history` fills
without further setup. Each row: job, trigger, node, fire time, duration, success, and the error if any.

- **Stat cards** over the page in view: success rate, failures, average duration, P95 duration.
- **Filters**: job, trigger, node, outcome.
- **The node filter** narrows to one machine, and the stat card titles then say so, so one node's success rate
  is not read as the fleet's.
- **The outcome filter** handles retries. A job with a retry policy of three writes four failed rows for one bad
  night. A failure followed by another attempt shows *Failed (retrying)*; **Failed after retries** shows only
  occurrences that gave up.
- **Run again**, on those failed rows, fires the job through `IScheduler.TriggerJob`. It is logged in the
  [action log](#action-log) and hidden in [read-only](#read-only-mode) mode. It uses the job's and trigger's
  stored data maps, not the merged map of the failed run, which history does not record. A row that will be
  retried has no button.

Below, **misfires** lists firings the scheduler missed, which never appear as executions: trigger, its job, the
node that noticed, the missed firing, and when it was noticed.

The history belongs to Quartz: `AddQuartzExecutionHistory()`'s recorder writes it, the container's
`IExecutionHistoryStore` holds it, and the [HTTP API](http-api.md#execution-history) serves the same rows. The
shipped store is per-process and in-memory, bounded by age (`HistoryRetention`, 24 hours) and count
(`HistoryMaxEntriesPerScheduler`, 2000 per feed per scheduler). With the dashboard registered, the bounds come
from those two settings:

<!-- snippet: sample_dashboard_history_bounds -->
```csharp
services.AddQuartzDashboard(options =>
{
    options.HistoryRetention = TimeSpan.FromHours(6);
    options.HistoryMaxEntriesPerScheduler = 500;
});
```
<!-- endSnippet -->

The age bound stops a quiet scheduler showing arbitrarily old executions. It is measured on the scheduler's
`TimeProvider` and applies on read as well as write, so a scheduler that stops writing still forgets.

To keep history across restarts, register your own store before `AddQuartzDashboard` (the shipped registration
is a `TryAdd`). Either seam works:

| Seam | Behaviour |
|---|---|
| `Quartz.Extensibility.IExecutionHistoryStore` | Preferred. Written by the recorder, read by the pages *and* served by the HTTP API |
| `IDashboardHistoryStore` | The 4.0 seam, unchanged; adapted onto the other in both directions. Same five members, other names |

- Both feeds carry `SchedulerInstanceId`, so one store shared by a cluster stays readable.
- Both have `CountMisfires(name, since)`, which a database-backed store can answer with one `COUNT(*)`.
- `AddQuartzDashboard` no longer registers `DashboardHistoryPlugin`; the recorder does its work. The type still
  works, but registering it beside the recorder records every execution twice.

### Enabling the history plugins

To also write history to the application's log, add the Quartz history plugins:

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

With `AuthorizationPolicy` set, the policy covers the pages, the SignalR hub, the Blazor circuit (`/_blazor`) and
the static asset endpoint, including under a fail-closed `FallbackPolicy`.

Without a policy, the dashboard adds no authorization. Startup refuses the mapping unless there is a
`RequireAuthorization()` or `AllowAnonymous()` on what `MapQuartzDashboard` returns, or a fail-closed
`FallbackPolicy`:

- The static asset endpoint (`_content/Quartz.Dashboard/*`) and the Blazor circuit (`/_blazor`) explicitly allow
  anonymous access, so they keep working under a fail-closed `FallbackPolicy`. They are public package content,
  not what the startup check is about.
- The **pages** and the **SignalR hub** have no authorization metadata of their own; the host's policies govern
  them. Under a fail-closed `FallbackPolicy`, an unauthenticated request to `/quartz` redirects to login and
  authenticated users get the dashboard. To expose it to unauthenticated users, do not apply a fail-closed
  `FallbackPolicy` to the dashboard paths, or set an `AuthorizationPolicy` they satisfy; and write
  `AllowAnonymous()` at the map site to record the intent.

::: warning Fail-closed `FallbackPolicy` with `MapStaticAssets()`
The host's `app.MapStaticAssets()` (the .NET 9/10 default) and the framework script `_framework/blazor.web.js`
are **host- and framework-owned endpoints** Quartz cannot annotate. A fail-closed `FallbackPolicy` blocks them for
unauthenticated users whatever the dashboard configuration. To serve them before login (a styled login page, say),
use `app.MapStaticAssets().AllowAnonymous();`; static web assets are public. The classic `app.UseStaticFiles()`
middleware runs before authorization and ignores the `FallbackPolicy`. See
[API-only projects](#api-only-projects-no-razor-files) for `RequiresAspNetWebAssets`.

With a custom `DashboardPath` this does not apply to the dashboard: its framework script and static assets are
then served under the dashboard path by dashboard-owned endpoints carrying the dashboard's authorization metadata.
:::

### One scheduler at a time

`AuthorizationPolicy` decides who reaches the dashboard; by default they then see every scheduler. Set
`SchedulerAuthorizationPolicy` to authorize each scheduler separately, as
`IAuthorizationService.AuthorizeAsync(user, new SchedulerResource(name), policy)`:

- the picker and the **Schedulers** page offer only schedulers the visitor passes for, and the picker refuses a
  value not in its rendered list;
- a page on a refused scheduler renders a *not authorized* frame and reads nothing: the page is never created, so
  no `IQuartzApiClient` call is made;
- **every call through the dashboard's own `IQuartzApiClient` is authorized again**, like `ReadOnly`, so a page
  with the wrong scheduler name is refused;
- the live-events hub refuses to join a refused scheduler's group.

The three settings compose: `AuthorizationPolicy` decides who is in, `SchedulerAuthorizationPolicy` which
schedulers they see, `ReadOnly` what anyone may change. `QuartzHttpApiOptions.SchedulerAuthorizationPolicy` uses
the same resource, so one `AuthorizationHandler<TRequirement, SchedulerResource>` serves the dashboard and the
HTTP API. Worked example: [Multi-tenancy](../multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler).

::: warning Standalone hosting is where this applies today
The *not authorized* frame is drawn by the dashboard's layout, which is rendered only when the dashboard owns its
Blazor root (the `MapQuartzDashboard()` overloads without a components builder). In both hosting modes, every
scheduler listing, the hub subscription and every call through the dashboard's `IQuartzApiClient` are filtered,
so the dashboard never directs a visitor to a refused scheduler. But under an application's own layout
([Integrating with an existing Blazor Server app](#integrating-with-an-existing-blazor-server-app)) the frame is
not drawn: an application that routes visitors to schedulers by its own means must not route them to refused
ones.
:::

Unset, the behaviour is as in earlier releases: whoever passes `AuthorizationPolicy` sees every scheduler.

### Read-only mode

`ReadOnly = true` hides every mutating control: pause, resume, trigger-now, trigger-with-overrides, reschedule,
reset-from-error-state, unschedule, interrupt, delete, calendar create and replace, and the scheduler's start,
stand-by, pause-all, resume-all and shutdown. Every listing, detail page and live view remains.

- It is one setting per process, not per scheduler or per operation. For different powers, run two dashboards: a
  read-only one for observers and a write-enabled one behind an operators-only policy.
  `SchedulerAuthorizationPolicy` limits *which schedulers* each shows, not what may be done to them.
- It does not affect the HTTP API, which is mapped and authorized separately. A read-only dashboard over an API
  anyone may post to is read-only in appearance only.
  [`QuartzHttpApiOptions.ReadOnly`](http-api.md#serving-reads-only) refuses every mutating API route, and binds a
  dashboard fronting the API from another process.

### Narrowing which job types may be named

`IsJobTypeAllowed` refuses one kind of write. Adding or scheduling a job through the dashboard's
`IQuartzApiClient` names the job type as a string, so a visitor who may write can name any `IJob`, including
`NativeJob` (which starts the executable its job data names) when `Quartz.Jobs` is on the probing path:

<!-- snippet: sample_dashboard_job_type_allow_list -->
```csharp
services.AddQuartzDashboard(options =>
{
    // The predicate sees the job type name as it was written, so a namespace prefix covers
    // every spelling of the same type. The HTTP API takes the same predicate under
    // QuartzHttpApiOptions.IsJobTypeAllowed; the two surfaces are configured separately.
    options.IsJobTypeAllowed = jobType => jobType.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
});
```
<!-- endSnippet -->

- A refused name raises `UnauthorizedAccessException`, as a refused scheduler does.
- It is enforced in the client, which every call goes through, like `ReadOnly`.
- The predicate gets the *name* as written; nothing resolves it, since the dashboard stores job type names
  unresolved, like the HTTP API. Prefer a namespace prefix to exact names: `Acme.Jobs.Nightly, Acme.Jobs` and the
  same name with `Version`, `Culture` and `PublicKeyToken` are the same type.
- It does not cover the HTTP API. Set
  `QuartzHttpApiOptions.IsJobTypeAllowed` [for that surface](http-api.md#narrowing-which-job-types-may-be-named)
  too when serving both.

### Browser security headers are the host's

Quartz sets no CORS policy and no browser security headers. ASP.NET Core sends no `Access-Control-Allow-Origin`
unless asked, so the API is same-origin by default. But **the dashboard is framable**, and its mutating controls
are one confirm dialog away: a page framing an authenticated operator's dashboard can drive a two-click sequence.
Send the header your other admin surfaces send:

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    await next();
});
```

`app.UseAntiforgery()` in the setup above is also the application's call; `MapRazorComponents` requires it.

### API key or custom authorization checks

For machine-to-machine access, bind your API auth scheme (an API key handler, say) to a policy used by
`MapQuartzHttpApi()`. For dashboard-only custom checks, use ASP.NET Core policy/handler-based authorization, so
the UI, hub and API are enforced consistently.

### Deployment guidance for multi-scheduler and clustered setups

- **Clustered ADO.NET job stores:** dashboard actions are scheduler operations that can affect the cluster;
  restrict write access to trusted operator roles.
- **Many local schedulers in one host:** the picker lists every registered scheduler; use clear,
  environment-specific names. For tenants, `SchedulerAuthorizationPolicy` keeps them apart; see
  [Multi-tenancy](../multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler).
- **Reverse proxy and Blazor Server:** enable WebSocket/SignalR forwarding, and sticky sessions where your hosting
  stack requires them. The circuit connects to `/_blazor` (or `{DashboardPath}/_blazor` with a custom
  `DashboardPath`).
- **Split operator experiences:** a read-only dashboard (`ReadOnly = true`) for observers and a separate
  write-enabled one for operators.
- **Operational retention:** the built-in history store is per-process and in-memory, bounded by
  `HistoryRetention` and `HistoryMaxEntriesPerScheduler`, so each cluster node keeps its own. Rows name their
  node, so a shared `IDashboardHistoryStore` gives one page over the whole fleet. Use external retention or
  reporting for long-term analytics.

### What authorization does not narrow

Both limits are by design, stay in 4.x, and match the [HTTP API](http-api.md#production-hardening)'s.

- **A visitor who passes is trusted with everything the dashboard shows.** The only narrowings are `ReadOnly`
  (process-wide) and `SchedulerAuthorizationPolicy` (which schedulers). Within a scheduler a writer may trigger,
  pause, reschedule, unschedule, interrupt, delete and shut down, and any visitor may read every job's data map
  and every exception message in the history. There are no per-operation permissions. Authorize the dashboard as
  you would a shell on the machine.
- **There is no rate limiting.** ASP.NET Core's rate limiter middleware applies as to any endpoint; Quartz
  configures none.

## Integrating with an existing Blazor Server app

If the host already uses Blazor Server (`MapRazorComponents<App>().AddInteractiveServerRenderMode()`), use the
`MapQuartzDashboard` overload that takes its `RazorComponentsEndpointConventionBuilder`. This avoids a second
`/_blazor` SignalR endpoint, which would conflict.

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
app.MapQuartzDashboard(blazor).RequireAuthorization();
```
<!-- endSnippet -->

The dashboard's pages, layout, CSS and JavaScript interop are registered into the host's Blazor setup through
`AddAdditionalAssemblies`. No `<link>` or `<script>` tags are needed in `App.razor`.

::: warning
Do **not** call the parameterless `MapQuartzDashboard()` beside your own `MapRazorComponents`: that registers two
`/_blazor` endpoints and the dashboard's interactive pages fail.
:::

### What integrated hosting changes

Everything else on this page applies to both modes.

| | Standalone (`MapQuartzDashboard()`) | Integrated (`MapQuartzDashboard(blazor)`) |
|---|---|---|
| `DashboardPath` | Honoured; the dashboard is self-contained under it | **Rejected**: routes fixed at `/quartz`; a custom path fails startup |
| Blazor circuit and framework assets | Dashboard-owned endpoints with the dashboard's authorization metadata | The host's: a fail-closed `FallbackPolicy` governs them; `AllowAnonymous` on static assets is your call |
| The *not authorized* frame | Drawn | **Not drawn**; see [One scheduler at a time](#one-scheduler-at-a-time) |

In both modes, every scheduler listing and hub subscription is filtered by `SchedulerAuthorizationPolicy`, so the
dashboard never offers a refused scheduler. The gap is only an application routing a visitor to one by its own
means.

## API-only projects (no .razor files)

A host project with no `.razor` files of its own (a pure API project, say) must add:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

This makes the SDK include the Blazor framework scripts (`_framework/blazor.web.js`, `blazor.server.js`) in the
app's static web assets. Without it, `/_framework/blazor.web.js` returns HTTP 404: as of .NET 10 these files are
static web assets, no longer embedded in the ASP.NET Core assemblies.

### The third symptom: an empty `200`

A missing `MapStaticAssets()` or `RequiresAspNetWebAssets` shows as **404**. A third failure looks like success:
**every static asset answers `200` with `Content-Length: 0`.**

```text
/_framework/blazor.web.js       200   0 bytes
/_framework/blazor.server.js    200   0 bytes
/YourApp.styles.css             200   0 bytes
```

The pages return `200` and prerender, but never become interactive: the circuit has no script. The only clue is a
startup warning:

```text
warn: Microsoft.AspNetCore.Hosting.Diagnostics[15]
      The WebRootPath was not found: ...\wwwroot. Static files may be unavailable.
```

Cause (ASP.NET Core, not Quartz): the static web assets manifest is resolved for an **unpublished build only in
the `Development` environment**. The same build with no `ASPNETCORE_ENVIRONMENT` (so Production) has the
endpoints, resolving to nothing. Either fix works:

- run in `Development` (`dotnet run` with the project's launch profile normally does);
- `dotnet publish` and run the published output, as a deployment does.

In practice it is a local trap: an unpublished build started with `dotnet run --no-launch-profile`, or from a
shell without `ASPNETCORE_ENVIRONMENT`.

## Current limitations

- **One HTTP target is one process**: one registration is one address; see
  [Fronting a scheduler in another process over HTTP](#fronting-a-scheduler-in-another-process-over-http). For
  a cluster behind a load balancer, use [a store-attached target](#store-attached-targets). Fronting a fleet as
  one, with per-target credentials, is [#3387](https://github.com/quartznet/quartznet/issues/3387).
- **A store-attached target cannot do anything node-local**, and its liveness is inferred; see
  [What a window can and cannot do](#what-a-window-can-and-cannot-do).
- **Neither Live Logs nor the Action Log is the record.** Live Logs keeps a hundred events from page open; the
  Action Log keeps 250 from this process's dashboard. Neither survives a restart or is lossless; use
  [metrics](opentelemetry-integration.md) for anything you need to go back to. The `ILogger` copy of Action Log
  entries does survive.
- **History is in-memory and per-process by default**, so a dashboard attached to a cluster shows an empty
  History page. On a persistent store,
  [`UsePersistentStore(store => store.UseExecutionHistory())`](../tutorial/job-stores.md#execution-history-in-the-database)
  makes every node write to `QRTZ_EXECUTION_HISTORY` and `QRTZ_MISFIRE_HISTORY`. A scheduler on the in-memory job
  store has no database for history; see [Execution history and misfires](#execution-history-and-misfires).
- **Read-only is one setting per process**, not per scheduler or per operation: "acme may look, globex may act"
  and "this tenant may pause but not delete" cannot be expressed. Which *schedulers* a visitor sees can; see
  [One scheduler at a time](#one-scheduler-at-a-time).
- **The misfire tile and Cluster page show what the store can answer**: a dash for no misfire feed, one node for a
  store with no check-in state. Neither is a count of zero.
- **Typed editors are narrow**: cron calendars, cron trigger reschedules and job-data overrides. Building an
  arbitrary trigger from the UI is not offered.
- **It is a scheduler console, not a workflow tool.** Job dependencies, DAGs and business-process visualisation
  are outside what Quartz models.
