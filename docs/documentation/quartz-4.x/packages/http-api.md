---
title: HTTP API
---

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) serves scheduler management endpoints from
an ASP.NET Core application. This page covers the server and the wire format; to call the endpoints from .NET,
see [HTTP Client](http-client.md), whose `HttpScheduler` implements `IScheduler` over this contract.

## Installation

`Quartz.AspNetCore` depends on `Quartz`, so one reference is enough:

```shell
dotnet add package Quartz.AspNetCore
```

## Basic setup

Configure Quartz and enable the HTTP API:

<!-- snippet: sample_httpapi_registration -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz(q => { });
builder.Services.AddQuartzHttpApi();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

- One set of endpoints serves every scheduler in the container; each request names its scheduler. So the API
  is added to the container, and there is no `IQuartzBuilder` form.
- The order of the three calls does not matter: nothing is built until the service provider is.

Map endpoints:

<!-- snippet: sample_httpapi_pipeline -->
```csharp
WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapQuartzHttpApi("/quartz-api").RequireAuthorization();
```
<!-- endSnippet -->

Quartz authorizes but does not authenticate. `UseAuthentication` needs a scheme the application registered, such
as `AddAuthentication(…).AddJwtBearer()` or an API-key handler. Calling `UseAuthentication()` with no scheme
registered fails at startup, resolving `IAuthenticationSchemeProvider`.

::: danger A mapping that says nothing about authorization does not start
The API has no authentication or authorization of its own, and its endpoints mutate schedulers, `shutdown` and
`clear` included. A scheduled job's type is a **string from the request**, resolved later with `Type.GetType`
against the host's probing path. With [`Quartz.Jobs`](quartz-jobs.md#nativejob) there (`Quartz.Plugins` depends
on it), the string can name `NativeJob`, which starts a process: an unauthenticated endpoint is remote code
execution.

So `app.MapQuartzHttpApi()` with no authorization statement fails at startup, in
`IHostedLifecycleService.StartingAsync` (before the server binds its listener). The message names the three
statements:

- `app.MapQuartzHttpApi().RequireAuthorization()` authorizes the whole API;
- `QuartzHttpApiOptions.SchedulerAuthorizationPolicy` authorizes each scheduler; see
  [Authorizing per scheduler](#authorizing-per-scheduler);
- `app.MapQuartzHttpApi().AllowAnonymous()` serves it to anyone, deliberately.

Also accepted: a non-null `AuthorizationOptions.FallbackPolicy`, or `RequireAuthorization()` on a `MapGroup` the
API is mapped into (group metadata flows into the endpoints). An application that calls `AddQuartzHttpApi()` and
maps nothing is not checked.

**`AllowAnonymous()` on a group counts, and it wins.** With `app.MapGroup("/ops").AllowAnonymous()`, the API
mapped into it starts and serves the whole mutating API to anyone, *even if* the mapping also says
`RequireAuthorization()`. ASP.NET Core's authorization middleware gives `IAllowAnonymous` precedence over any
`IAuthorizeData`, in any order. The guard cannot tell a deliberate `AllowAnonymous()` from an inherited one.

**Some mappings are checked a moment late.** At `StartingAsync` the guard sees only endpoints reachable from the
route builder `Map*` was called on, and a `RouteGroupBuilder`'s endpoints do not yet carry the group's
conventions. A `MapGroup(...).MapQuartzHttpApi()`, and anything mapped from `Startup.Configure`/`UseEndpoints`,
is checked in `StartedAsync` instead. The host still stops, but an unauthorized API of that shape answers
requests between the web host starting and the guard throwing. Map it on the application directly to avoid that
window.
:::

### Where the API is served

The default is `/quartz-api`. Change it at the map site or at registration:

<!-- snippet: sample_httpapi_path -->
```csharp
// at the map site, beside the application's other routes
app.MapQuartzHttpApi("/ops/api").RequireAuthorization();

// or at registration
builder.Services.AddQuartzHttpApi(options => options.ApiPath = "/ops/api");
```
<!-- endSnippet -->

- If both are given, **the pattern passed to `MapQuartzHttpApi` wins**. The parameterless overload uses
  `ApiPath`.
- A map-site pattern must start with `/`, the same rule as `ApiPath`.

## Every endpoint

Sixty-five routes in four groups.

- `{ApiPath}` is `/quartz-api` unless changed. `{name}` is the scheduler; every route but the first has one.
- Every route with `{name}` is subject to [`SchedulerAuthorizationPolicy`](#authorizing-per-scheduler) when set.
- An unknown scheduler is `404` on every route.
- **Answers** uses the [response-shape conventions](#response-shape-conventions): *empty* is `200` with no body;
  `{ applied }` is the one-flag form; `{ groups }` / `{ jobs }` / `{ triggers }` are the group-matcher and
  key-set forms; *paged* is the [paged envelope](#listing-endpoints-are-paged).

### Schedulers — 17

| Method | Path | Answers |
|---|---|---|
| `GET` | `{ApiPath}/schedulers` | Every scheduler the container knows, built or only registered ([below](#the-scheduler-listing-carries-registrations)); names no scheduler, so it filters itself |
| `GET` | `{ApiPath}/schedulers/{name}` | The scheduler and its `SchedulerMetadata` |
| `GET` | `{ApiPath}/schedulers/{name}/context` | `{ context }`, [every value as text](#the-scheduler-context-travels-as-text) |
| `POST` | `{ApiPath}/schedulers/{name}/start` | empty; `?delay=00:00:30` delays; `400` for a negative delay or a [store-attached window](dashboard.md#what-a-window-can-and-cannot-do) |
| `POST` | `{ApiPath}/schedulers/{name}/standby` | empty; `400` for a store-attached window |
| `POST` | `{ApiPath}/schedulers/{name}/shutdown` | empty; `?waitForJobsToComplete=true` waits for running jobs; `400` for a store-attached window |
| `POST` | `{ApiPath}/schedulers/{name}/clear` | empty; deletes every job, trigger and calendar |
| `POST` | `{ApiPath}/schedulers/{name}/pause-all` | empty |
| `POST` | `{ApiPath}/schedulers/{name}/resume-all` | empty |
| `GET` | `{ApiPath}/schedulers/{name}/nodes` | The cluster's nodes ([below](#cluster-nodes)) |
| `GET` | `{ApiPath}/schedulers/{name}/events` | Live events as `text/event-stream` ([below](#the-event-stream)) |
| `GET` | `{ApiPath}/schedulers/{name}/history/executions` | A page of what the scheduler ran ([below](#execution-history)) |
| `GET` | `{ApiPath}/schedulers/{name}/history/misfires` | A page of missed firings |
| `GET` | `{ApiPath}/schedulers/{name}/history/misfires/count` | `{ count }` since `?since=` |
| `GET` | `{ApiPath}/schedulers/{name}/execution-limits` | `{ limits, useTriggerGroupWhenUnset }`; `limits` is `null` when nothing is limited |
| `POST` | `{ApiPath}/schedulers/{name}/execution-limits` | empty; replaces the whole set |
| `DELETE` | `{ApiPath}/schedulers/{name}/execution-limits` | empty; same as posting an empty set |

### Jobs — 21

Every path below is prefixed `{ApiPath}/schedulers/{name}`.

| Method | Path | Answers |
|---|---|---|
| `GET` | `…/jobs` | paged job headers |
| `POST` | `…/jobs/fetch` | Whole job details for a page of keys, at most 1000 |
| `GET` | `…/jobs/{jobGroup}/{jobName}` | The job detail |
| `GET` | `…/jobs/{jobGroup}/{jobName}/exists` | `{ exists }` |
| `GET` | `…/jobs/{jobGroup}/{jobName}/triggers` | Every trigger pointing at that job |
| `GET` | `…/jobs/fire-instances` | paged fire instances ([below](#fire-instances)) |
| `POST` | `…/jobs/{jobGroup}/{jobName}/pause` | `{ applied }` |
| `POST` | `…/jobs/pause` | `{ groups }`; group matcher in the query string |
| `POST` | `…/jobs/keys/pause` | `{ jobs }`; key set in the body |
| `POST` | `…/jobs/{jobGroup}/{jobName}/resume` | `{ applied }` |
| `POST` | `…/jobs/resume` | `{ groups }` |
| `POST` | `…/jobs/keys/resume` | `{ jobs }` |
| `POST` | `…/jobs/{jobGroup}/{jobName}/trigger` | empty; fires the job now; body may carry a `JobDataMap` for this firing |
| `POST` | `…/jobs/{jobGroup}/{jobName}/interrupt` | `{ applied }`; every execution of the job |
| `POST` | `…/jobs/interrupt/{fireInstanceId}` | `{ applied }`; the one firing |
| `DELETE` | `…/jobs/{jobGroup}/{jobName}` | `{ applied }` |
| `POST` | `…/jobs/delete` | `{ jobs }` |
| `POST` | `…/jobs/delete-by-group` | `{ jobs }`; group matcher in the query string |
| `POST` | `…/jobs` | empty; adds the job; `replace` and `storeNonDurableWhileAwaitingScheduling` are body fields |
| `GET` | `…/jobs/groups` | paged job groups; the four `name*` filters, `paused` |
| `GET` | `…/jobs/groups/{jobGroup}/paused` | `{ paused }` |

### Triggers — 22

| Method | Path | Answers |
|---|---|---|
| `GET` | `…/triggers` | paged trigger headers |
| `POST` | `…/triggers/fetch` | Whole triggers for a page of keys, at most 1000 |
| `GET` | `…/triggers/{triggerGroup}/{triggerName}` | The trigger |
| `GET` | `…/triggers/{triggerGroup}/{triggerName}/exists` | `{ exists }` |
| `GET` | `…/triggers/{triggerGroup}/{triggerName}/state` | `{ state }`, the `TriggerState` name |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/reset-from-error-state` | `{ applied }` |
| `POST` | `…/triggers/keys/reset-from-error-state` | `{ triggers }` |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/pause` | `{ applied }` |
| `POST` | `…/triggers/pause` | `{ groups }` |
| `POST` | `…/triggers/keys/pause` | `{ triggers }` |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/resume` | `{ applied }` |
| `POST` | `…/triggers/resume` | `{ groups }` |
| `POST` | `…/triggers/keys/resume` | `{ triggers }` |
| `GET` | `…/triggers/groups` | paged trigger groups; the four `name*` filters, `paused` |
| `GET` | `…/triggers/groups/{triggerGroup}/paused` | `{ paused }` |
| `POST` | `…/triggers/schedule` | `{ firstFireTimeUtc }`; one job and its trigger |
| `POST` | `…/triggers/schedule-multiple` | empty; several jobs and their triggers |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/unschedule` | `{ applied }` |
| `POST` | `…/triggers/unschedule` | `{ triggers }` |
| `POST` | `…/triggers/unschedule-by-group` | `{ triggers }`; group matcher in the query string |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/reschedule` | `{ firstFireTimeUtc }`; **`null`** when the trigger did not exist |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/update-details` | `{ applied }`; edits the trigger [as a patch](#editing-a-trigger-in-place) |

### Calendars — 5

| Method | Path | Answers |
|---|---|---|
| `GET` | `…/calendars` | paged calendar names |
| `GET` | `…/calendars/{calendarName}` | The calendar |
| `GET` | `…/calendars/{calendarName}/exists` | `{ exists }` |
| `POST` | `…/calendars` | empty; adds or replaces; `replace` and `updateTriggers` are body fields |
| `DELETE` | `…/calendars/{calendarName}` | `{ applied }` |

::: tip Why `schedule-multiple` is not `schedule`
`POST …/triggers/schedule` answers with one first fire time. Several jobs have several, so the plural form is a
separate route with an empty body.
:::

## The scheduler listing carries registrations

`GET {ApiPath}/schedulers` lists every scheduler the container has *registered* or *built*, ordered by name. It is
not paged.

```json
[
  {
    "name": "acme",
    "schedulerInstanceId": null,
    "status": null,
    "origin": "Container"
  },
  {
    "name": "core",
    "schedulerInstanceId": "web-01",
    "status": "Running",
    "origin": "Container"
  }
]
```

- `status` and `schedulerInstanceId` are both `null` for a registration nothing has built; listing does not
  build it. This is the only way to tell "this tenant has not started" from "no such tenant": the scheduler's
  own routes answer `404` for both.
- `origin` is `Container` for a scheduler registered by `AddQuartz()` or `AddQuartz(name, …)`, and `Runtime` for
  one in the repository without a registration (bound by hand, or remote from `AddQuartzHttpClient`).

## Enums travel as names

Every enum on the wire (`status`, `state`, `repeatIntervalUnit`, a `daysOfWeek` entry, …) is spelled as its C#
member name:

```json
{ "status": "Running" }
{ "state": "Paused" }
```

- The names are the contract and are stable across versions.
- Numeric forms are still *accepted* on input, so an older client's `?state=1` works.
- Query-string filters take a name too: `?state=Paused`.

## A job type is a name, and its two attribute flags may be absent

`jobType` is an assembly-qualified type name, treated as text. The server resolves neither incoming nor outgoing
names; the type is loaded only when the job fires and when a store derives the job's attributes.

`concurrentExecutionDisallowed` and `persistJobDataAfterExecution` are therefore **nullable**:

- **stated** (`true` / `false`): your value, overriding the type's attributes;
- **omitted or `null`**: whatever `[DisallowConcurrentExecution]` and `[PersistJobDataAfterExecution]` on the
  type say, decided by the side that resolves the type.

```json
{
  "job": {
    "name": "nightly", "group": "reports",
    "jobType": "Acme.Jobs.NightlyReport, Acme.Jobs",
    "durable": true,
    "jobDataMap": {}
  },
  "replace": true
}
```

That request keeps the job's `[DisallowConcurrentExecution]`. Before 4.0 rc.1 the fields were plain booleans,
so an omitted field stored `false` and a job declared unsafe to run concurrently was stored as safe.

A job whose type the answering process cannot resolve reports both flags as `null`, instead of a `500`, for
example on a node without the job's assembly.

## Durations travel as `TimeSpan`

Every duration, in bodies and query strings, is a `TimeSpan` in its invariant form, for example
`"repeatIntervalTimeSpan": "120.02:30:59.9990000"`:

```text
POST {ApiPath}/schedulers/{name}/start?delay=00:00:30
```

## The scheduler context travels as text

`GET {ApiPath}/schedulers/{name}/context` returns **every value as a string**: a non-string value as its invariant
text, a null as null.

```json
{
  "context": {
    "activeFrom": "2025-06-01T12:30:00.0000000+00:00",
    "nothing": null,
    "retries": "4352",
    "tenant": "acme"
  }
}
```

- Instants (`DateTimeOffset`, `DateTime`) use the round-trip `"O"` format, as elsewhere on the wire.
- Entries are ordered by key, ordinally.
- A client gets strings whatever the types were in the scheduler's process. A value whose type a caller must act
  on belongs in its own endpoint.

::: warning The scheduler context is not a secret store
**Every** entry is returned, falling back to `Convert.ToString`. For a record or struct with a compiler-generated
`ToString`, that is every field. One authorized `GET` dumps the whole map, including any connection strings or
API keys, and [Jobs](quartz-jobs.md#directoryscanjob) puts shared instances there. Any authorized caller can read
the context, as with a job's data map. Keep secrets in `IConfiguration`, a key vault or the container, and put a
*name* in the context if a job needs to find one.
:::

## Response-shape conventions

A `200` has a body only when the operation has something to report that the caller could not work out: a
computed value, or whether it applied.

| Operation | Answers |
|---|---|
| A read that found its target | `200` with the object |
| A read whose target does not exist | `404` with RFC 7807 problem details |
| A mutation that always acts | `200` with an **empty body** |
| A mutation that may be a no-op | `200` with `{ "applied": … }` |
| …the same, for a group matcher or key set | `200` with what it applied to: `{ "groups": [ … ] }`, `{ "jobs": [ … ] }`, `{ "triggers": [ … ] }` |
| A mutation that computed something | `200` with that value, e.g. `{ "firstFireTimeUtc": … }` |
| Any operation on an unknown scheduler | `404` |

- Always-acting mutations: `AddJob`, `TriggerJob`, `PauseAll`, `ScheduleJobs`, `AddCalendar`, `Start`, `Standby`,
  `Shutdown`, `Clear`, the execution-limit writes.
- `{ "firstFireTimeUtc": … }` comes from schedule and reschedule.
- `applied` is the only flag: the entity existed and the operation changed it. An operation that cannot answer
  that about one entity is a key-set form and answers with keys.
- A key-set delete or unschedule **still acts on the keys it found** on a partial hit, so it answers with those
  keys; a boolean could not tell "some missing" from "nothing happened".

### Errors are one shape per kind

Every error is RFC 7807 problem details. A **client-actionable** error (every `400` and `404`) carries `type`,
`title`, `status`, `detail` and `Quartz-ExceptionType`, the exception the server raised:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "The scheduler has been shut down",
  "Quartz-ExceptionType": "SchedulerException"
}
```

A client maps Quartz exception names (`SchedulerException`, `JobPersistenceException`,
`ObjectAlreadyExistsException`, …) back to typed exceptions and treats other values as opaque; `HttpScheduler`
does this. `Quartz-ExceptionStackTrace` is added only when `IncludeStackTraceInProblemDetails` is on.

A **`500` carries neither `Quartz-ExceptionType` nor the exception's message**, because a driver's message can
name the server, database, login or constraint. `detail` is a fixed sentence; the real message is logged:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500,
  "detail": "The scheduler failed to handle the request. The failure is recorded in the server's log."
}
```

`IncludeStackTraceInProblemDetails` puts the message back beside the stack trace.

::: warning
The `500` `detail` is a constant: match a client on the status code only.
:::

One `400` has **no** body: a query parameter the framework cannot bind (`?skip=not-a-number`,
`?includeTotalCount=maybe`) is rejected before the endpoint. Anything the endpoint rejects (`?skip=-1`,
`?take=lots`, `?state=not-a-state`, a job with no name, unparseable JSON) answers with problem details.

## Listing endpoints are paged

Every listing (jobs, triggers, calendars, the two group listings) takes `skip`, `take` and `includeTotalCount` and
returns a paged envelope:

```json
{
  "items": [ /* ... */ ],
  "hasMore": true,
  "totalCount": 4213
}
```

| Parameter | Default | Notes |
|---|---|---|
| `take` | 250 (`PagedQuery.DefaultTake`) | **`?take=all`** (`PagedQuery.All` in code) asks for as many as the server allows |
| `includeTotalCount` | `false` | `totalCount` is `null` unless requested; it costs a second query |

- `hasMore` is exact.
- A count without rows: `?take=0&includeTotalCount=true`, answered with the count query alone.

`QuartzHttpApiOptions.MaxPageSize` bounds one request, default **1000**, the same as the
[bulk key fetch](#a-whole-set-of-keys-in-one-call).

- A **number** above the cap is a `400` naming the cap and the setting that raises it. That includes
  `?take=2147483647`, the number behind the old sentinel.
- **`all`** is *capped*, not refused. A listing under the cap answers as if there were no cap; `hasMore` says when
  it was cut. This keeps the 3.x-compatible listings (`GetJobKeys` and its neighbours) working through
  [`HttpScheduler`](http-client.md), which throws on a truncated answer instead of returning a short list.
- Set `MaxPageSize` to `0` for an export or migration that must take everything in one call.

::: tip `take` is a string in the OpenAPI document
It accepts a number or `all`, so the six listing operations describe `take` as
`"A page size, or \"all\" for everything up to MaxPageSize"`. A generated client takes a string: pass the number
as text.
:::

| Endpoint | Returns | Filters (besides paging) |
|---|---|---|
| `GET {ApiPath}/schedulers/{name}/jobs` | Job headers¹ | `groupEquals`, `groupContains`, `groupStartsWith`, `groupEndsWith`, and the four `name*` filters |
| `GET {ApiPath}/schedulers/{name}/jobs/groups` | Job groups: `name`, `paused` | `nameEquals`, `nameContains`, `nameStartsWith`, `nameEndsWith`, `paused` |
| `GET {ApiPath}/schedulers/{name}/triggers` | Trigger headers² | the four `group*` and four `name*` filters, `jobName` + `jobGroup` (both or neither), `calendarName`, `state` |
| `GET {ApiPath}/schedulers/{name}/triggers/groups` | Trigger groups: `name`, `paused` | `nameEquals`, `nameContains`, `nameStartsWith`, `nameEndsWith`, `paused` |
| `GET {ApiPath}/schedulers/{name}/calendars` | Calendar names | `nameEquals`, `nameContains`, `nameStartsWith`, `nameEndsWith` |
| `GET {ApiPath}/schedulers/{name}/jobs/fire-instances` | Fire instances³ | the four `group*` and four `name*` filters (on the *trigger*), `jobName` + `jobGroup` (both or neither), `schedulerInstanceId`, `state` |

¹ Key, description, `jobType` (the assembly-qualified name the detail body carries), durable,
concurrent-execution-disallowed, persist-job-data, requests-recovery.
² Key, job key, description, trigger type, state, start/end/next/previous fire times, calendar name, priority,
execution group, retry policy and attempt, and [what the trigger is waiting for](#continuations).
³ `fireInstanceId`, trigger key, job key (`null` while only reserved), `schedulerInstanceId`, `state`,
`fireTimeUtc`, `scheduledFireTimeUtc`, `executionGroup`, and what the job
[last reported](../how-tos/progress-and-execution-logs.md#report-progress): `progress` (0–100) and
`progressMessage`, both `null` until it reports.

- Results are ordered by group, then name, on every page. Fire instances add the fire instance id, since one
  trigger can have several firings at once.
- At most one `name*` filter per request; more is a `400`.
- Filter text is literal, not a wildcard: a calendar named `50%` matches `?nameStartsWith=50%25`.

### Fire instances

`GET {ApiPath}/schedulers/{name}/jobs/fire-instances` lists firings, not job-execution contexts. It is
store-backed, so with a persistent store it covers the whole cluster.

| `state` | Lists |
|---|---|
| none (default) | running firings (`Executing`) |
| `Any` | everything |
| `Acquired` | reservations |
| anything else | `400` |

Caveats for a UI:

- A firing an `ITriggerListener` vetoes completes when the veto is applied. It can be listed only between the
  store recording the firing and the veto decision.
- Elapsed time is your clock minus `fireTimeUtc`, written by the firing node's clock. With skewed clocks it can be
  negative; clamp at zero.
- `scheduledFireTimeUtc` is the schedule the owning node recorded; after a misfire, the *rescheduled* time. Its gap
  to `fireTimeUtc` is not misfire lateness.

`POST {ApiPath}/schedulers/{name}/jobs/interrupt/{fireInstanceId}` interrupts one firing;
`POST …/jobs/{group}/{name}/interrupt` interrupts every execution of the job. Both are node-local on the server:
interrupt a firing owned by another node through that node.

To get whole objects for a page of headers, post the keys back:

- `POST {ApiPath}/schedulers/{name}/jobs/fetch`: body is an array of `{ "name": …, "group": … }`; returns job
  details;
- `POST {ApiPath}/schedulers/{name}/triggers/fetch`: the same, returning triggers.

Missing keys are absent from the response. At most 1000 keys per call. These are the only non-`GET` routes that
are reads, so `ReadOnly` serves them; see [Serving reads only](#serving-reads-only).

::: warning Changed in 4.x
Listings used to return bare arrays of keys, or `{ "names": [ … ] }` for group and calendar listings. All now
return the paged envelope. `GET {ApiPath}/schedulers/{name}/triggers/groups/paused` was removed; use
`GET {ApiPath}/schedulers/{name}/triggers/groups?paused=true`.
:::

## Cluster nodes

`GET {ApiPath}/schedulers/{name}/nodes` returns the scheduler's nodes: the node that handled the request first,
then the rest by instance id. It is not paged.

```json
[
  {
    "instanceId": "web-01",
    "lastCheckInUtc": "2026-08-26T09:14:57+00:00",
    "checkInInterval": "00:00:15",
    "state": "Alive",
    "isCurrentNode": true
  },
  {
    "instanceId": "web-02",
    "lastCheckInUtc": "2026-08-26T09:09:12+00:00",
    "checkInInterval": "00:00:15",
    "state": "Failed",
    "isCurrentNode": false
  }
]
```

| `state` | Meaning |
|---|---|
| `Alive` | Checking in |
| `Overdue` | Missed a check-in; nothing more |
| `Failed` | The store's recovery sweep will take over its in-flight work; then it is no longer listed |

- `checkInInterval` is a `TimeSpan`, the interval *that* node was configured with.
- Both times are `null` when the store keeps no check-in history (in-memory store, or persistent without
  clustering). The answer is then one node: `isCurrentNode: true`, `Alive`, no times. `null` is not zero; do not
  fall back to `DateTimeOffset.MinValue`.
- The verdicts are the answering node's, on its own clock; with skewed clocks two nodes can disagree.
- Join to `GET {ApiPath}/schedulers/{name}/jobs/fire-instances` on `schedulerInstanceId` to see what each node
  runs.

## The event stream

`GET {ApiPath}/schedulers/{name}/events` streams the scheduler's events as
[server-sent events](https://developer.mozilla.org/docs/Web/API/Server-sent_events), one frame per event, until
the caller disconnects. Use it for a live view from another process: a dashboard, a terminal, a browser
`EventSource`.

```text
event: JobExecuted
data: {"kind":"JobExecuted","schedulerName":"QuartzScheduler","schedulerInstanceId":"web-01","occurredAtUtc":"2026-09-12T10:30:00+00:00","jobKey":{"name":"nightly","group":"reports"},"triggerKey":{"name":"at-midnight","group":"reports"},"fireInstanceId":"fire-1","fireTimeUtc":"2026-09-12T10:29:55+00:00","runTime":"00:00:01.5000000","vetoed":false,"exceptionMessage":"the job threw","status":null,"message":null,"cause":null}
id: 7

```

- `event:` is the event's **kind**, so a reader can subscribe by kind without parsing:
  `EventSource.addEventListener("JobExecuted", …)`.
- `data:` is one line of the API's usual JSON.
- `id:` counts this stream's frames. It is **not** a cursor: nothing is replayed.

Fourteen kinds:

| Kind | What happened | Carries, beyond the common four |
|---|---|---|
| `JobExecuting` | a job began running | `jobKey`, `triggerKey`, `fireTimeUtc`, `fireInstanceId` |
| `JobExecuted` | a job finished, threw, or was vetoed before it ran | the same, plus `runTime`, `vetoed`, `exceptionMessage` |
| `TriggerFired` | a trigger fired | `triggerKey`, `jobKey`, `fireTimeUtc`, `fireInstanceId` |
| `TriggerCompleted` | its firing completed | the same |
| `TriggerMisfired` | a firing was missed and the misfire instruction applied | `triggerKey`, `jobKey`; no `fireTimeUtc` |
| `TriggerPaused`, `TriggerResumed` | a trigger was paused or resumed | `triggerKey` |
| `JobPaused`, `JobResumed` | a job, and every trigger that fires it, was paused or resumed | `jobKey` |
| `JobInterrupted` | one firing of a job was interrupted | `jobKey`, `fireInstanceId` |
| `TriggerInError` | a trigger entered the error state; it will not fire until reset | `triggerKey` |
| `SchedulerStateChanged` | one node's scheduler entered a new lifecycle state | `status` |
| `SchedulerError` | the scheduler handled an error itself | `message`, `cause`, and the `triggerKey` / `jobKey` where known |
| `Heartbeat` | nothing; the connection is still open | nothing; see below |

- Every event carries `kind`, the raising node's `schedulerName` and `schedulerInstanceId`, and `occurredAtUtc`.
  In a cluster each node raises its own events, so the instance id tells a local event from a peer's.
- Every member is always present; facets a kind does not carry are `null`.

**Heartbeats are not events.** One is sent when the stream opens (which sends the response headers), then one
whenever [`EventStreamHeartbeatInterval`](#configuration-options) (fifteen seconds) passes with nothing to send.
They keep intermediaries from closing an idle connection and let the reader tell a quiet scheduler from a dead
socket. A reader should drop them; `Quartz.HttpClient` does.

**Authorized once, when the stream opens.** The route names `{schedulerName}`, so
[`SchedulerAuthorizationPolicy`](#authorizing-per-scheduler) runs before the handler. A refused caller gets `403`
with problem details and no frames; an unknown scheduler gets `404`. The open stream is not re-authorized: close
it and reopen to re-check rights.

**No replay, and no `Last-Event-ID`.** A subscription gets only later events. After a reconnect, read the gap
from the [execution history](#execution-history).

::: warning A reverse proxy must not buffer this
A buffering proxy delivers frames in bursts, and an idle timeout shorter than the heartbeat keeps cutting the
connection. For nginx, set `proxy_buffering off;` and a `proxy_read_timeout` above the heartbeat interval for this
route. The response already sends `Cache-Control: no-cache,no-store` and `Content-Encoding: identity`.
:::

From .NET, `AddQuartzHttpClient` registers a reader for this route that reconnects by itself; see
[Events](http-client.md#events).

## Execution history

A job store holds what is *scheduled*. What *happened* (what ran, for how long, whether it threw, what was
missed) is in the container's `IExecutionHistoryStore`, served by three routes:

| Path | Query | Answers |
|---|---|---|
| `GET {ApiPath}/schedulers/{name}/history/executions` | `skip`, `take`, `includeTotalCount`, `schedulerInstanceId`, `jobContains`, `triggerContains` | A page of executions, newest first |
| `GET {ApiPath}/schedulers/{name}/history/misfires` | the same, minus `jobContains` | A page of misfires, newest first |
| `GET {ApiPath}/schedulers/{name}/history/misfires/count` | `since`: a `DateTimeOffset`, required | `{ "count": 3 }` |

```json
{
  "items": [
    {
      "schedulerInstanceId": "web-01",
      "jobGroup": "reports",
      "jobName": "nightly",
      "triggerGroup": "reports",
      "triggerName": "at-midnight",
      "firedAtUtc": "2026-08-26T00:00:00+00:00",
      "duration": "00:00:01.5000000",
      "succeeded": false,
      "exceptionMessage": "the job threw"
    }
  ],
  "hasMore": false,
  "totalCount": 1
}
```

- Rows carry the **node's** id, not the scheduler name (the route has it). `schedulerInstanceId` narrows to one
  node.
- `jobContains` and `triggerContains` match a key's group, name, or `group.name`, case-insensitively.
- Paging uses the usual envelope, bounded by [`MaxPageSize`](#listing-endpoints-are-paged).

**`AddQuartzHttpApi()` records the history.** It calls `AddQuartzExecutionHistory()`, which installs one recorder
into every scheduler in the container and keeps history in an in-memory store bounded by age and count, as the
dashboard's always has. To record nothing:

```csharp
services.AddQuartzExecutionHistory(options => options.MaxEntriesPerScheduler = 0);
```

**A scheduler on a persistent store can keep history in the database.**
[`UsePersistentStore(store => store.UseExecutionHistory())`](../tutorial/job-stores.md#execution-history-in-the-database)
writes both feeds to `QRTZ_EXECUTION_HISTORY` and `QRTZ_MISFIRE_HISTORY`. They survive a restart, and a whole
cluster writes one history, so the routes answer for every node.

Which history a route reads follows the [dashboard's rule](dashboard.md#execution-history-and-misfires):

- a named scheduler that called `UseExecutionHistory()`: its own database;
- any other scheduler: the container's shared store;
- a [store-attached window](dashboard.md#store-attached-targets): the database it is a window onto. If that
  store keeps no history, the routes answer `400` with the dashboard's explanation, not an empty page.

To keep history elsewhere, register your own `IExecutionHistoryStore` before `AddQuartzHttpApi()`. The shipped
registration is a `TryAdd`, and `UseExecutionHistory()` replaces only the in-memory default. A dashboard in the
same process reads the same store.

## Pause and resume report what they did

Every single-key mutation that can be a no-op answers `{ "applied": … }`:

- `POST …/jobs/{group}/{name}/pause`, `…/resume`
- `POST …/triggers/{group}/{name}/pause`, `…/resume`
- `POST …/triggers/{group}/{name}/reset-from-error-state`
- `POST …/triggers/{group}/{name}/unschedule`
- `POST …/jobs/{group}/{name}/interrupt`, `POST …/jobs/interrupt/{fireInstanceId}`
- `DELETE …/jobs/{group}/{name}`
- `DELETE …/calendars/{name}`

```json
{ "applied": true }
```

`applied` is `false` when the key does not exist or nothing changed (pausing a paused trigger, resuming one that
was not paused, resetting one not in the error state). `POST …/jobs/delete` and `POST …/triggers/unschedule` take
a key set and answer with keys, [like every key-set form](#a-whole-set-of-keys-in-one-call).

The group-matcher forms, `POST …/jobs/pause`, `…/jobs/resume`, `…/triggers/pause` and `…/triggers/resume`, are
the wire form of `PauseJobGroups`, `ResumeJobGroups`, `PauseTriggerGroups` and `ResumeTriggerGroups`. They return
the groups they recorded:

```json
{ "groups": [ "reporting", "imports" ] }
```

The `…/keys/pause` and `…/keys/resume` routes beside them return the keys they moved.

### A whole set of keys in one call

The key-set forms take many keys in one request (one round trip and one scheduling signal) and answer with the
keys they applied to:

| Endpoint | Body | Answers |
|---|---|---|
| `POST …/jobs/keys/pause`, `…/jobs/keys/resume` | `{ "jobs": [ { "name": …, "group": … } ] }` | `{ "jobs": [ … ] }` |
| `POST …/triggers/keys/pause`, `…/triggers/keys/resume` | `{ "triggers": [ { "name": …, "group": … } ] }` | `{ "triggers": [ … ] }` |
| `POST …/triggers/keys/reset-from-error-state` | `{ "triggers": [ … ] }` | `{ "triggers": [ … ] }` |
| `POST …/jobs/delete` | `{ "jobs": [ { "name": …, "group": … } ] }` | `{ "jobs": [ … ] }` |
| `POST …/triggers/unschedule` | `{ "triggers": [ { "name": …, "group": … } ] }` | `{ "triggers": [ … ] }` |

- A key the operation did not apply to (names nothing, already paused, not in the error state) is **absent**
  from the answer, never an error.
- The answer keeps the request's order. `answer.length === request.length` means every key applied; otherwise
  the list shows which did.
- Pause, resume and reset are under `keys/` because the collection-level `pause` and `resume` are the
  group-matcher forms. `delete` and `unschedule` keep the plain path; their group forms are `delete-by-group` and
  `unschedule-by-group`.
- The server applies the whole set in one pass (one lock and one transaction on the ADO store) and signals the
  scheduling change once.
- Listener events stay per key: one `TriggerPaused` / `JobPaused` / `TriggerResumed` / `JobResumed` /
  `JobDeleted` / `JobUnscheduled` per applied key, none for the rest. There is no key-set listener event:
  `TriggersPaused(null)` means *every group*, and a monitoring listener would read it as a total outage.

### A whole group in one call

Delete by group to cancel everything sharing a trigger group (a saga, a tenant, a conversation) without listing
keys first, which would leave a window for another node to add one.

| Endpoint | Selects by | Answers |
|---|---|---|
| `POST …/jobs/delete-by-group` | `?groupEquals=`, `?groupStartsWith=`, `?groupEndsWith=`, `?groupContains=` | `{ "jobs": [ … ] }` |
| `POST …/triggers/unschedule-by-group` | the same four | `{ "triggers": [ … ] }` |

- The parameters are those of `…/jobs/pause` and `…/triggers/pause`. None given means *every group*.
- The answer is the removed keys, not group names.
- A non-durable job left with no triggers is removed with them but not named: an unschedule answers with
  triggers.
- The group is resolved inside the lock that empties it, and the scheduling change is signalled once.
- Listener events are per key: one `JobDeleted` or `JobUnscheduled` per removed key, none for an empty group.

## Editing a trigger in place

`POST …/triggers/{triggerGroup}/{triggerName}/update-details` changes a trigger's metadata and settings without
rescheduling it. Fire times, fire count and state are unchanged; only the fields in the body change. It is the
wire form of [`IScheduler.UpdateTriggerDetails`](../how-tos/rescheduling-jobs.md). It answers
`{ "applied": … }`, `false` (not `404`) when no trigger has the key, as for
[every single-trigger mutation](#response-shape-conventions).

**The body is a patch: an omitted member is left alone; a member present as `null` is cleared.** Sending every
member as `null` clears them all.

```json
{
  "description": "nightly export",
  "priority": 8,
  "calendarName": null
}
```

This sets the description and priority, removes the calendar, and leaves job data, misfire instruction, node
pin, execution group and retry policy unchanged.

| Member | Type | Meaning |
|---|---|---|
| `description` | string or `null` | The trigger's description |
| `priority` | number | The trigger's priority |
| `jobDataMap` | object or `null` | Replaces the trigger's job data; `null` empties it |
| `calendarName` | string or `null` | Calendar to observe (must exist); `null` removes it |
| `misfireInstruction` | number | Misfire instruction code, as in a trigger body |
| `misfireInstructionFamily` | string or `null` | The schedule family of the code; see below |
| `preferredNode` | string or `null` | Node pin: `null` clears, `"*"` requests an automatic pin, else a scheduler instance id |
| `preferredNodeAuto` | bool | Sent with `preferredNode`: whether the pin was assigned automatically |
| `executionGroup` | string or `null` | The [execution group](../tutorial/execution-groups.md) whose thread limit applies; `null` leaves every group |
| `retryPolicy` | string or `null` | The [retry policy](../how-tos/retrying-failed-jobs.md) in stored form, e.g. `"fixed;3;00:00:30"`; `null` stops retrying |

**Name the misfire instruction's family.** `misfireInstructionFamily` is `Simple`, `Cron`, `CalendarInterval`,
`DailyTimeInterval` or `Recurrence`. The same number means a different policy per family (`2` is *do nothing*
for cron, *reschedule now with existing repeat count* for simple), so the scheduler refuses a code aimed at
another family. Omit the family to skip that check; that is the only way to set a code on a custom trigger type,
which belongs to none of the five.

::: warning A calendar name or a misfire instruction changes firing
Unlike the other fields, these change when the trigger fires. Neither recomputes fire times; the new value
applies at the next scheduling evaluation.
:::

A continuation cannot be edited here: what a trigger waits for is set when it is scheduled, and changing it is a
reschedule.

## Continuations

A [continuation](../how-tos/job-continuations.md) is a trigger that waits for another trigger's firing. It
travels wherever a trigger does: in a trigger body, in the listing header, and through `POST …/schedule-job`.

In a trigger body (what `GET …/triggers/{group}/{name}` returns and `schedule-job` accepts), three members sit
beside `retryPolicy`:

```json
{
  "continuesAfterTriggerName": "import",
  "continuesAfterTriggerGroup": "nightly",
  "continuationCondition": 6
}
```

`continuationCondition` is the stored integer there: `1` on success, `2` on failure, `4` on cancellation, `8` on
veto, `15` however it ends. All three are `null` on a trigger that waits for nothing.

A **listing header** uses the same members, with the condition as names, like
[every enum](#enums-travel-as-names) in a body the API composes:

```json
{
  "name": "reconcile", "group": "nightly",
  "state": "Awaiting",
  "continuesAfterTriggerName": "import",
  "continuesAfterTriggerGroup": "nightly",
  "continuationCondition": "OnFailure, OnCancellation"
}
```

`?state=Awaiting` lists waiting triggers; `GET …/triggers/{group}/{name}/state` answers `{ "state": "Awaiting" }`
for one.

::: warning `Awaiting` is a state a 4.1 client does not know
A **4.1** client reading a trigger listing or state from a **4.2** host meets `"Awaiting"` and throws, as a 4.0
client did with `SchedulerOrigin.Remote` from a 4.1 host. It only happens once continuations are scheduled, so
follow the [rolling-upgrade order](../how-tos/job-continuations.md#upgrading-a-running-cluster): migrate, roll
every node and client, then schedule continuations.
:::

## Configuration options

`QuartzHttpApiOptions`:

| Option | Default | What it does |
|---|---|---|
| `ApiPath` | `/quartz-api` | Base path of every endpoint; see [Where the API is served](#where-the-api-is-served) |
| `IncludeStackTraceInProblemDetails` | `false` | Adds `Quartz-ExceptionStackTrace` to errors, and a `500`'s real message to `detail` |
| `MaxPageSize` | `1000` | Most items one paged request returns; `0` is unbounded; see [Listing endpoints are paged](#listing-endpoints-are-paged) |
| `ReadOnly` | `false` | Refuses every mutating route with `403`; see [Serving reads only](#serving-reads-only) |
| `SchedulerAuthorizationPolicy` | none | Policy for every route naming a scheduler, evaluated against it; see [Authorizing per scheduler](#authorizing-per-scheduler) |
| `IsJobTypeAllowed` | none | Predicate over a request's job type *name*; refused is `403`; see [Narrowing which job types may be named](#narrowing-which-job-types-may-be-named) |
| `EventStreamHeartbeatInterval` | `00:00:15` | Idle time before the [event stream](#the-event-stream) sends a `Heartbeat` |

- Set `EventStreamHeartbeatInterval` below the idle read timeout of any proxy in front of the API: nginx's
  `proxy_read_timeout` defaults to 60 seconds, Azure's front doors to 90.
- There is one set of options per process, not per scheduler. Calling `services.AddQuartzHttpApi(configure)`
  twice configures the same options; the last callback wins for settings both touch.

### Serving reads only

`ReadOnly = true` refuses every route that changes something:

```csharp
services.AddQuartzHttpApi(options => options.ReadOnly = true);
```

- A refusal is `403` with problem details reading *The Quartz HTTP API is configured as read-only.* It is decided
  **before** the handler: no body is read and no scheduler looked up, so refusals reveal nothing about which
  schedulers exist. It carries no `Quartz-ExceptionType`.
- Use it where the API only feeds a dashboard, a monitoring tool or a report. Route-level authorization would have
  to name thirty-odd routes.
- **Mutation is decided per route, not per verb.** The bulk fetches, `POST {ApiPath}/schedulers/{name}/jobs/fetch`
  and `POST …/triggers/fetch`, are reads and are served. Every other non-`GET` is refused, including pause,
  resume, interrupt and reset-from-error-state.
- It binds only this API. The scheduler keeps firing, and a dashboard mapped beside it has its own
  [`QuartzDashboardOptions.ReadOnly`](dashboard.md#read-only-mode). A dashboard *fronting* this API from another
  process is bound by this setting and reports its refusal.

### Authorizing per scheduler

`RequireAuthorization(...)` on the mapping covers the whole API, which fits when every caller may reach every
scheduler. Otherwise, set `SchedulerAuthorizationPolicy`. Each request is then evaluated as
`IAuthorizationService.AuthorizeAsync(user, new SchedulerResource(name), policy)` against the route's
`{schedulerName}`.

- A refused caller gets `403` with problem details, decided **before** the scheduler lookup, so a `404` only
  answers names the caller may ask about.
- `GET {ApiPath}/schedulers` is filtered to the schedulers the caller may act on.
- The application writes one `AuthorizationHandler<TRequirement, SchedulerResource>`.
  `QuartzDashboardOptions.SchedulerAuthorizationPolicy` uses the same resource, so one handler serves both. Worked
  example: [Multi-tenancy](../multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler).
- Setting it without authorization services in the container fails at startup.
- It authorizes and never authenticates: an anonymous caller gets whatever the policy says (a `403` if refused).
  Keep `RequireAuthorization()` on the mapped group to challenge with a `401` first.

### Narrowing which job types may be named

Add-job, schedule and schedule-multiple take a job type name in the body. An authorized caller can name any
`IJob`, including `NativeJob` (which starts the executable its job data names) when `Quartz.Jobs` is on the
probing path. `IsJobTypeAllowed` restricts the names:

<!-- snippet: sample_httpapi_job_type_allow_list -->
```csharp
builder.Services.AddQuartzHttpApi(options =>
{
    // The predicate sees the job type name exactly as the request spelled it. A namespace
    // prefix therefore covers every spelling of the same type - with or without the
    // assembly's version, culture and public key token.
    options.IsJobTypeAllowed = jobType => jobType.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
});
```
<!-- endSnippet -->

- A refused name is `403` with problem details whose `detail` names only the requested type.
- The refusal is per *request*: `schedule-multiple` stores none of its batch if one job is refused.
- Each refusal is logged as event `9005` at `Warning`.
- **The predicate sees a name, not a `Type`.** Nothing resolves it first, which would be the assembly probe this
  API avoids ([A job type is a name](#a-job-type-is-a-name-and-its-two-attribute-flags-may-be-absent)).
- **One type has several spellings**: `Acme.Jobs.Nightly, Acme.Jobs`, or with `Version`, `Culture` and
  `PublicKeyToken`. A namespace prefix covers them all; a set of exact strings is easy to get wrong.
- **A malformed name is still a `400`**, not a `403`.
- It is one predicate per process and does not receive the scheduler name, so it cannot allow a type for one
  scheduler only. The [dashboard](dashboard.md#narrowing-which-job-types-may-be-named) has its own
  `QuartzDashboardOptions.IsJobTypeAllowed`, configured separately.

## Calling it from .NET

`Quartz.HttpClient` is the client half of this contract. Its `HttpScheduler` implements `IScheduler` over these
endpoints:

```shell
dotnet add package Quartz.HttpClient
```

<!-- snippet: sample_httpapi_client -->
```csharp
IScheduler scheduler = new HttpScheduler("MyScheduler", httpClient);
await scheduler.TriggerJob(new JobKey("nightly-report"));
```
<!-- endSnippet -->

With a container, register it and inject `IScheduler`, naming the `IHttpClientFactory` client that carries the base
address and authentication.

**The base address is the site root plus `ApiPath`, ending with `/`.** A base address of the site root alone
answers `404` on every call; one without the trailing slash is refused by the `HttpScheduler` constructor.

<!-- snippet: sample_httpapi_client_registration -->
```csharp
// The base address is the site root *plus the API path*, and it must end with "/"
builder.Services.AddHttpClient("quartz", client => client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/"));
builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");
```
<!-- endSnippet -->

Any HTTP client can speak this wire format. [HTTP Client](http-client.md) covers registration, authentication,
serializer matching and what does not travel.

## The wire contract is source-generated

The API's bodies are a closed set, described by a source-generated `JsonSerializerContext` shared by the server
and `Quartz.HttpClient`: schedulers, job details, trigger pages, problem details and every request body. A new
body must be added there.

Three things stay open:

- `ITrigger` and `ICalendar` are read and written by converters that consult the scheduler's serializer
  registry, so custom trigger and calendar types can travel;
- the values in a `JobDataMap` are whatever the application stored.

The generated contract is asked first and reflection second, so reflection covers only the payload, never the
contract.

## Production hardening

- Require authentication and authorization on `MapQuartzHttpApi()`. Startup refuses a mapping that states
  nothing, but also accepts `AllowAnonymous()`: do not use it just to silence the error.
- Do not expose this API or the [dashboard](dashboard.md) to a network you would not give a shell. A job's type
  is a string from the request, and `Quartz.Jobs` puts `NativeJob` within reach. Set `IsJobTypeAllowed` wherever
  the deployment's job types are known in advance; see
  [Narrowing which job types may be named](#narrowing-which-job-types-may-be-named).
- Keep `IncludeStackTraceInProblemDetails` off in production: it returns the stack trace *and* a `500`'s real
  message.
- Restrict mutating operations (schedule, delete, pause/resume, shutdown) to trusted operator roles. Quartz has
  no per-operation permissions: **a caller who passes authorization is trusted with the whole API**, including
  every job's data map. `SchedulerAuthorizationPolicy` limits *which schedulers*, `IsJobTypeAllowed` *which job
  types*; anything finer belongs in the policy or a gateway.
- **Every successful mutation is logged** at `Information` as event `9007`:
  `"Api user {User} performed {Operation} on scheduler {SchedulerName}: {Route}"`.
  - `{User}` is `HttpContext.User.Identity.Name`, or `(anonymous)`; authenticate the API for it to mean anything.
  - `{Operation}` is the endpoint's name; `{Route}` is the request path, which spells the target key.
  - One line per request, after the handler, for `2xx` answers only. A refusal is `9005`; a failure is `9003` or
    `9004`.
  - It is this process's log. API actions are not in the [dashboard](dashboard.md#action-log)'s Action Log, and
    dashboard actions are not here. All events: [Log Events](../log-events.md).
- Set `ReadOnly` where nothing should write through the API, such as a process that only feeds a dashboard,
  monitoring or reports; see [Serving reads only](#serving-reads-only).
- Leave `MaxPageSize` set, so one request cannot load an unbounded result.
- In a cluster, API calls are scheduler control operations with cluster-wide effect.
- There is **no rate limiting**. ASP.NET Core's rate limiter middleware applies as to any endpoint; Quartz
  configures none.
