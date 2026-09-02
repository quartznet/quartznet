---
title: HTTP API
---

Quartz HTTP API is provided by [Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) and exposes scheduler management endpoints for ASP.NET Core apps.

This page is the server half and the wire format. For driving one of these endpoints from .NET, see
[HTTP Client](http-client.md), whose `HttpScheduler` implements `IScheduler` over exactly this contract.

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

The API serves every scheduler in the container through one set of endpoints — a request names the
scheduler it is for — so it is added to the container rather than to a scheduler. There is deliberately
no `IQuartzBuilder` form: written inside `AddQuartz(name, …)` it would look like that scheduler's API
while configuring everybody's.

The order of those three calls does not matter. Every `Add…` here registers services and configuration
callbacks, and nothing is built until the provider is; `AddQuartzHttpApi()` before `AddQuartz()` means
what it means after. The pages show one order for the sake of showing one.

Map endpoints:

<!-- snippet: sample_httpapi_pipeline -->
```csharp
WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapQuartzHttpApi("/quartz-api").RequireAuthorization();
```
<!-- endSnippet -->

`UseAuthentication` there is the application's, and it needs a scheme the application registered —
`AddAuthentication(…).AddJwtBearer()`, an API-key handler, whatever you already use. Quartz supplies
none: it authorizes, and something else authenticates. Calling `UseAuthentication()` in an application
that registered no scheme fails at startup, resolving `IAuthenticationSchemeProvider`.

::: danger A mapping that says nothing about authorization does not start
`RequireAuthorization()` there is not decoration. The API adds no authentication and no authorization of
its own, every endpoint below mutates the scheduler it names — `shutdown` and `clear` among them — and a
job scheduled through it carries its type as a **string the request supplies**, stored unresolved and
resolved later with `Type.GetType` against whatever is on the host's probing path. With
[`Quartz.Jobs`](quartz-jobs.md) on that path the string reaches `NativeJob`, which starts a process named
in job data: an unauthenticated endpoint here is remote code execution rather than an information leak.
`Quartz.Plugins` depends on `Quartz.Jobs`, so an application that installed the plugins has `NativeJob`
on its probing path without a line naming `Quartz.Jobs` in its own project file.

`app.MapQuartzHttpApi()` with nothing else said therefore fails at startup — in
`IHostedLifecycleService.StartingAsync`, which runs before the server binds its listener — with a message
naming the three ways to say what you meant:

- `app.MapQuartzHttpApi().RequireAuthorization()` authorizes the whole API;
- `QuartzHttpApiOptions.SchedulerAuthorizationPolicy` authorizes each scheduler on its own — see
  [Authorizing per scheduler](#authorizing-per-scheduler);
- `app.MapQuartzHttpApi().AllowAnonymous()` serves it to anyone, deliberately.

A non-null `AuthorizationOptions.FallbackPolicy` satisfies the check as well, since it covers every
endpoint that states nothing, and so does a `RequireAuthorization()` on a `MapGroup` the API is mapped
into — group metadata flows into the endpoints. An application that calls `AddQuartzHttpApi()` and never
maps anything serves nothing and is left alone.

**An `AllowAnonymous()` on a group counts as the statement, and it wins.** `app.MapGroup("/ops").AllowAnonymous()`
with `MapQuartzHttpApi()` mapped into it starts cleanly and serves the whole mutating API to anyone,
*even if* the mapping also says `RequireAuthorization()` — ASP.NET Core's authorization middleware gives
`IAllowAnonymous` precedence over any `IAuthorizeData` on the same endpoint, whatever order the
conventions were added in. That is the framework's rule rather than Quartz's, and it is the reason
`AllowAnonymous()` is listed above as a way of saying what you meant: the guard cannot tell a deliberate
one from an inherited one.

**For a mapping the guard cannot see before start-up, the refusal comes a moment late.** The check runs
in `IHostedLifecycleService.StartingAsync`, before the server binds its listener — but it can only see
endpoints reachable from the route builder `Map*` was called on, and a `RouteGroupBuilder`'s endpoints do
not carry the group's conventions yet. So a `MapGroup(...).MapQuartzHttpApi()`, and anything mapped from
`Startup.Configure`/`UseEndpoints`, is checked in `StartedAsync` instead. The host still stops, but an
unauthorized API of that shape answers requests for the window between the web host starting and the
guard throwing. Map it on the application directly if that window matters to you.
:::

### Where the API is served

`/quartz-api` is the default, and there are two ways to say something else:

<!-- snippet: sample_httpapi_path -->
```csharp
// at the map site, beside the application's other routes
app.MapQuartzHttpApi("/ops/api").RequireAuthorization();

// or at registration
builder.Services.AddQuartzHttpApi(options => options.ApiPath = "/ops/api");
```
<!-- endSnippet -->

Naming the path where the endpoints are mapped is how the rest of ASP.NET Core reads —
`MapHealthChecks("/health")` — and it keeps the route with the application's other routes. If both are
given, **the pattern passed to `MapQuartzHttpApi` wins**; the parameterless overload uses whatever
`ApiPath` says. A pattern given at the map site has to start with `/`, the same rule `ApiPath` is
validated against.

## Every endpoint

Sixty routes in four groups. `{ApiPath}` is `/quartz-api` unless you said otherwise, and
`{name}` is the scheduler the request is for — every route but the first carries one, and every route
that carries one is subject to
[`SchedulerAuthorizationPolicy`](#authorizing-per-scheduler) when it is set.

The **Answers** column is shorthand for the [response-shape
conventions](#response-shape-conventions): *empty* means `200` with no body, `{ applied }` is the
one-flag form, `{ groups }` / `{ jobs }` / `{ triggers }` are the key-set and group-matcher forms, and
*paged* is the [paged envelope](#listing-endpoints-are-paged). An unknown scheduler is `404` on every
one of them.

### Schedulers — 13

| Method | Path | Answers |
|---|---|---|
| `GET` | `{ApiPath}/schedulers` | Every scheduler the container knows about, built or merely registered — [see below](#the-scheduler-listing-carries-registrations). The one route that names no scheduler, so it filters itself |
| `GET` | `{ApiPath}/schedulers/{name}` | The scheduler and its `SchedulerMetadata` |
| `GET` | `{ApiPath}/schedulers/{name}/context` | `{ context }` — [every value as text](#the-scheduler-context-travels-as-text) |
| `POST` | `{ApiPath}/schedulers/{name}/start` | empty. `?delay=00:00:30` starts it delayed; a negative delay is a `400` |
| `POST` | `{ApiPath}/schedulers/{name}/standby` | empty |
| `POST` | `{ApiPath}/schedulers/{name}/shutdown` | empty. `?waitForJobsToComplete=true` waits for running jobs |
| `POST` | `{ApiPath}/schedulers/{name}/clear` | empty — deletes every job, trigger and calendar |
| `POST` | `{ApiPath}/schedulers/{name}/pause-all` | empty |
| `POST` | `{ApiPath}/schedulers/{name}/resume-all` | empty |
| `GET` | `{ApiPath}/schedulers/{name}/nodes` | The cluster's nodes — [see below](#cluster-nodes) |
| `GET` | `{ApiPath}/schedulers/{name}/execution-limits` | `{ limits, useTriggerGroupWhenUnset }`; `limits` is `null` when nothing is limited |
| `POST` | `{ApiPath}/schedulers/{name}/execution-limits` | empty — replaces the whole set |
| `DELETE` | `{ApiPath}/schedulers/{name}/execution-limits` | empty — the same as posting an empty set |

### Jobs — 21

Every path below is prefixed `{ApiPath}/schedulers/{name}`.

| Method | Path | Answers |
|---|---|---|
| `GET` | `…/jobs` | paged job headers |
| `POST` | `…/jobs/fetch` | Whole job details for a page of keys, at most 1000 |
| `GET` | `…/jobs/{jobGroup}/{jobName}` | The job detail |
| `GET` | `…/jobs/{jobGroup}/{jobName}/exists` | `{ exists }` |
| `GET` | `…/jobs/{jobGroup}/{jobName}/triggers` | Every trigger pointing at that job |
| `GET` | `…/jobs/fire-instances` | paged fire instances — [see below](#fire-instances) |
| `POST` | `…/jobs/{jobGroup}/{jobName}/pause` | `{ applied }` |
| `POST` | `…/jobs/pause` | `{ groups }` — selects by group matcher in the query string |
| `POST` | `…/jobs/keys/pause` | `{ jobs }` — selects by key set in the body |
| `POST` | `…/jobs/{jobGroup}/{jobName}/resume` | `{ applied }` |
| `POST` | `…/jobs/resume` | `{ groups }` |
| `POST` | `…/jobs/keys/resume` | `{ jobs }` |
| `POST` | `…/jobs/{jobGroup}/{jobName}/trigger` | empty — fires the job now; body may carry a `JobDataMap` for the one firing |
| `POST` | `…/jobs/{jobGroup}/{jobName}/interrupt` | `{ applied }` — every execution of that job |
| `POST` | `…/jobs/interrupt/{fireInstanceId}` | `{ applied }` — the one firing |
| `DELETE` | `…/jobs/{jobGroup}/{jobName}` | `{ applied }` |
| `POST` | `…/jobs/delete` | `{ jobs }` |
| `POST` | `…/jobs/delete-by-group` | `{ jobs }` — selects by group matcher in the query string |
| `POST` | `…/jobs` | empty — adds the job; `replace` and `storeNonDurableWhileAwaitingScheduling` are body fields |
| `GET` | `…/jobs/groups` | paged job groups: the four `name*` filters, `paused` |
| `GET` | `…/jobs/groups/{jobGroup}/paused` | `{ paused }` |

### Triggers — 21

| Method | Path | Answers |
|---|---|---|
| `GET` | `…/triggers` | paged trigger headers |
| `POST` | `…/triggers/fetch` | Whole triggers for a page of keys, at most 1000 |
| `GET` | `…/triggers/{triggerGroup}/{triggerName}` | The trigger |
| `GET` | `…/triggers/{triggerGroup}/{triggerName}/exists` | `{ exists }` |
| `GET` | `…/triggers/{triggerGroup}/{triggerName}/state` | `{ state }` — the `TriggerState` name |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/reset-from-error-state` | `{ applied }` |
| `POST` | `…/triggers/keys/reset-from-error-state` | `{ triggers }` |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/pause` | `{ applied }` |
| `POST` | `…/triggers/pause` | `{ groups }` |
| `POST` | `…/triggers/keys/pause` | `{ triggers }` |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/resume` | `{ applied }` |
| `POST` | `…/triggers/resume` | `{ groups }` |
| `POST` | `…/triggers/keys/resume` | `{ triggers }` |
| `GET` | `…/triggers/groups` | paged trigger groups: the four `name*` filters, `paused` |
| `GET` | `…/triggers/groups/{triggerGroup}/paused` | `{ paused }` |
| `POST` | `…/triggers/schedule` | `{ firstFireTimeUtc }` — one job and its trigger |
| `POST` | `…/triggers/schedule-multiple` | empty — several jobs and their triggers in one call |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/unschedule` | `{ applied }` |
| `POST` | `…/triggers/unschedule` | `{ triggers }` |
| `POST` | `…/triggers/unschedule-by-group` | `{ triggers }` — selects by group matcher in the query string |
| `POST` | `…/triggers/{triggerGroup}/{triggerName}/reschedule` | `{ firstFireTimeUtc }`, **`null`** when the trigger did not exist |

### Calendars — 5

| Method | Path | Answers |
|---|---|---|
| `GET` | `…/calendars` | paged calendar names |
| `GET` | `…/calendars/{calendarName}` | The calendar |
| `GET` | `…/calendars/{calendarName}/exists` | `{ exists }` |
| `POST` | `…/calendars` | empty — adds or replaces; `replace` and `updateTriggers` are body fields |
| `DELETE` | `…/calendars/{calendarName}` | `{ applied }` |

::: tip Why `schedule-multiple` is not `schedule`
`POST …/triggers/schedule` computes one first fire time and answers with it. The plural form cannot —
there is one per job — so it is a separate route with an empty body rather than an overload that
sometimes answers and sometimes does not.
:::

## The scheduler listing carries registrations

`GET {ApiPath}/schedulers` answers with every scheduler the container knows about, ordered by name — the
ones something has *registered* as well as the ones something has *built*. It is not paged: a process
runs a handful of schedulers, not a data set.

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

`status` and `schedulerInstanceId` are `null` together, for a registration nothing has built: there is
no scheduler to be in a state or to have an instance id, and listing it did not create one. That is the
only way to tell "this tenant has not started" from "there is no such tenant" — the scheduler's own
routes answer `404` for both.

`origin` says where the scheduler came from: `Container` for one `AddQuartz()` or `AddQuartz(name, …)`
registered, `Runtime` for one that is in the repository without a registration behind it — a scheduler
bound by hand, or a remote one from `AddQuartzHttpClient`.

## Enums travel as names

Every enum the API puts on the wire — a scheduler's `status`, a trigger's `state`, a trigger's
`repeatIntervalUnit`, a `daysOfWeek` entry — is spelled with its name, matching the C# member:

```json
{ "status": "Running" }
{ "state": "Paused" }
```

The names are the contract, so they are stable across versions; the numeric form is still *accepted* on
input, which is what makes an older client's `?state=1` keep working. Filters given in the query string
take a name too: `?state=Paused`.

## A job type is a name, and its two attribute flags may be absent

`jobType` is an assembly-qualified type name and the API treats it as text. The server does not resolve
a name that arrived with a request, and it does not resolve one on the way out either: the type is
loaded where it is needed — when the job fires, and when a store derives a job's attributes — and
nowhere else.

`concurrentExecutionDisallowed` and `persistJobDataAfterExecution` are therefore **nullable**:

- **stated** (`true` / `false`) — your value, and it wins over the type's attributes;
- **omitted or `null`** — whatever `[DisallowConcurrentExecution]` and `[PersistJobDataAfterExecution]`
  on the type say, decided by the side that resolves the type.

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

That request adds a job whose `[DisallowConcurrentExecution]` is intact. Before 4.0 rc.1 the two fields
were plain booleans, so an omitted field arrived as `false` and stored a job its author had declared
unsafe to run concurrently as safe to.

On the way out, a job whose type the answering process cannot resolve reports both flags as `null` —
which is what an operator on a node without the job's assembly sees, rather than a `500`.

## Durations travel as `TimeSpan`

Every duration on the wire is a `TimeSpan` in its invariant form, both ways: a trigger body says
`"repeatIntervalTimeSpan": "120.02:30:59.9990000"`, and the one duration in a query string is spelled
the same way.

```text
POST {ApiPath}/schedulers/{name}/start?delay=00:00:30
```

## The scheduler context travels as text

`GET {ApiPath}/schedulers/{name}/context` answers with the scheduler's context, and **every value in it
goes out as a string**. The context is the application's own map of `string` to `object`, so a value
that is not one is rendered as its invariant text; a null stays null.

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

Two rules make that body the same one everywhere: an instant — a `DateTimeOffset` or a `DateTime` —
is rendered in the round-trip `"O"` format, the ISO-8601 spelling every other instant on this wire
carries, and the entries are ordered by key ordinally rather than in the order a concurrent dictionary
happens to enumerate them.

Text is all a remote reader has — the endpoint hands out a snapshot of a live in-process map, and a
client reading it back gets every entry as a string whatever it was in the scheduler's process. An
entry whose type a caller has to act on belongs in an endpoint of its own rather than in the context.

::: warning The scheduler context is not a secret store
**Every** entry is returned, and the fallback rendering is `Convert.ToString` — which for a record or a
struct with a compiler-generated `ToString` is every field it has. One authorized `GET` therefore dumps
the application's own map, connection strings and API keys included, and
[Jobs](quartz-jobs.md#directoryscanjob) teaches putting shared instances there. The context is exactly as
secret as a job's data map, which is to say not at all: an authorized caller reads both. Keep secrets in
`IConfiguration`, a key vault or the container, and put a *name* in the context if a job needs to find
one.
:::

## Response-shape conventions

**A `200` carries a body exactly when the operation has something to say that the caller could not
have worked out for itself** — the value it computed, or whether it applied. The rule is the same for
every endpoint, so the body follows from what the operation *is* rather than from which endpoint it
was:

| Operation | Answers |
|---|---|
| A read that found its target | `200` with the object |
| A read whose target does not exist | `404` with RFC 7807 problem details |
| A mutation that always acts | `200` with an **empty body** — `AddJob`, `TriggerJob`, `PauseAll`, `ScheduleJobs`, `AddCalendar`, `Start`, `Standby`, `Shutdown`, `Clear`, the execution-limit writes |
| A mutation whose effect may be a no-op | `200` with one flag, **named for what it reports** — `{ "applied": … }` |
| …the same, aimed at a group matcher or a key set | `200` with what it applied to — `{ "groups": [ … ] }`, `{ "jobs": [ … ] }`, `{ "triggers": [ … ] }` |
| A mutation that computed something | `200` with that value — `{ "firstFireTimeUtc": … }` from schedule and reschedule |

An unknown scheduler is `404` whatever the operation was.

The flag is always one boolean, and it is **named for the fact it reports**: `applied` — the entity
existed and the operation changed it. There is no second spelling; an operation that cannot answer
that question about a single entity is a key-set form, and answers with the keys instead.

A partial hit on a key-set form **still deletes or unschedules the keys it found**, which is why those
two answer with the keys rather than with a flag: a boolean could only say that not all of them were
found, and a caller could not tell that from nothing having happened.

### Errors are one shape per kind

Every error the API produces is RFC 7807 problem details. A **client-actionable** error — every `400`
and every `404` — carries `type`, `title`, `status`, `detail` and `Quartz-ExceptionType` naming the
exception the server raised, whichever layer raised it:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "The scheduler has been shut down",
  "Quartz-ExceptionType": "SchedulerException"
}
```

A client maps the Quartz exception names — `SchedulerException`, `JobPersistenceException`,
`ObjectAlreadyExistsException`, … — back to typed exceptions, and treats any other value as opaque;
`HttpScheduler` does exactly that. `Quartz-ExceptionStackTrace` joins them only when
`IncludeStackTraceInProblemDetails` is on.

A **`500` carries neither `Quartz-ExceptionType` nor the exception's message.** It is a fault the caller
cannot act on, so naming what produced it says something about the server's internals and nothing a
client could use — and a driver's message names the server, the database, the login or the constraint as
readily as it names anything else. The `detail` is one fixed sentence, and the real message is logged:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500,
  "detail": "The scheduler failed to handle the request. The failure is recorded in the server's log."
}
```

Turning on `IncludeStackTraceInProblemDetails` — the switch that already says "I am debugging this" —
puts the message back beside the stack trace.

::: warning
The `detail` is a constant, so it is not something to match a client on beyond the status code itself.
A driver fault's own message names the server, the database, the login or the constraint, and none of
that leaves the process.
:::

There is one case where a `400` has **no** body at all, and it is not the API's doing: a query
parameter the framework could not bind — `?skip=not-a-number`, `?includeTotalCount=maybe` — is
rejected before the request reaches an endpoint. A request the endpoint itself rejected —
`?skip=-1`, `?take=lots`, `?state=not-a-state`, a job with no name, unparseable JSON — always answers
with the problem details above.

## Listing endpoints are paged

Every listing endpoint — jobs, triggers, calendars, and the two group listings — takes `skip`, `take` and
`includeTotalCount` query parameters and returns a paged envelope:

```json
{
  "items": [ /* ... */ ],
  "hasMore": true,
  "totalCount": 4213
}
```

`take` defaults to 250 (`PagedQuery.DefaultTake`) when the request names none — ask for as many as the
server will give with **`?take=all`** (`PagedQuery.All` is how it is spelled in code) — `hasMore` is
exact, and `totalCount` is `null` unless `includeTotalCount=true` was asked for, because computing it
costs a second database query. A count with no rows is `?take=0&includeTotalCount=true`, which the stores
answer with the count query alone.

`QuartzHttpApiOptions.MaxPageSize` bounds how many items one request may return, and defaults to
**1000** — the same limit the [bulk key fetch](#a-whole-set-of-keys-in-one-call) has always had. The two
spellings of `take` are answered differently on purpose:

- a **number** above the cap is a `400` naming the cap and the setting that would raise it. `?take=2147483647`
  is one of those: the number behind the sentinel is no longer accepted at the default cap;
- **`all`** is *bounded* by the cap rather than refused by it, because it does not name a number — it says
  "as many as you will give me". A listing whose matches fit under the cap therefore answers exactly as it
  would with no cap at all, and `hasMore` says when it did not. This is what keeps the 3.x-compatible
  listings (`GetJobKeys` and its neighbours) working through
  [`HttpScheduler`](http-client.md): they ask for everything whether the answer is three rows or three
  million. `HttpScheduler` turns a truncated answer to one of those into an exception rather than a short
  list.

Set `MaxPageSize` to `0` where an export or a migration really has to take everything in one call.

| Endpoint | Returns | Filters (besides paging) |
|---|---|---|
| `GET {ApiPath}/schedulers/{name}/jobs` | Job headers: key, description, `jobType` (the same assembly-qualified name the detail body carries), durable, concurrent-execution-disallowed, persist-job-data, requests-recovery | `groupEquals`, `groupContains`, `groupStartsWith`, `groupEndsWith`, and the four `name*` filters |
| `GET {ApiPath}/schedulers/{name}/jobs/groups` | Job groups: `name`, `paused` | `nameEquals`, `nameContains`, `nameStartsWith`, `nameEndsWith`, `paused` |
| `GET {ApiPath}/schedulers/{name}/triggers` | Trigger headers: key, job key, description, trigger type, state, start/end/next/previous fire times, calendar name, priority, execution group | the four `group*` and four `name*` filters, plus `jobName` + `jobGroup` (give both or neither), `calendarName`, `state` |
| `GET {ApiPath}/schedulers/{name}/triggers/groups` | Trigger groups: `name`, `paused` | `nameEquals`, `nameContains`, `nameStartsWith`, `nameEndsWith`, `paused` |
| `GET {ApiPath}/schedulers/{name}/calendars` | Calendar names | `nameEquals`, `nameContains`, `nameStartsWith`, `nameEndsWith` |
| `GET {ApiPath}/schedulers/{name}/jobs/fire-instances` | Fire instances: `fireInstanceId`, trigger key, job key (`null` while only reserved), `schedulerInstanceId`, `state`, `fireTimeUtc`, `scheduledFireTimeUtc`, `executionGroup` | the four `group*` and four `name*` filters (they match the *trigger*), plus `jobName` + `jobGroup` (give both or neither), `schedulerInstanceId`, `state` |

Results are ordered by group and then name, and every page uses the same ordering, so paging through a
result is consistent. Fire instances add a third ordering key, the fire instance id, because one trigger
can have several firings at once and group plus name would not order them. At most one `name*` filter may be given per request; more than one is a `400`.
The filter's text is a literal — a calendar named `50%` is matched by `?nameStartsWith=50%25` and is
not a wildcard.

### Fire instances

`GET {ApiPath}/schedulers/{name}/jobs/fire-instances` lists firings rather than job-execution contexts,
and it is store-backed, so with a persistent job store it covers the whole cluster rather than the node
that answered.

Its `state` filter is the one listing filter with a non-empty default: naming no `state` lists what is
running (`Executing`), because that is the question the endpoint is usually asked. Ask for everything with
`?state=Any`, or for reservations with `?state=Acquired`. Anything else is a `400`.

Three caveats belong on any UI built over this:

- A firing an `ITriggerListener` vetoes does not linger — applying the veto completes it. It can be listed
  for the instant between the store recording the firing and the veto being decided, and never after.
- Elapsed time is your clock minus `fireTimeUtc`, and `fireTimeUtc` was written by the firing node's
  clock. On a cluster with skewed clocks the difference can be negative; clamp it at zero.
- `scheduledFireTimeUtc` is the schedule as the owning node recorded it, which after a misfire is the
  *rescheduled* time. It is not the fire time that was missed, and the gap to `fireTimeUtc` is not misfire
  lateness.

`POST {ApiPath}/schedulers/{name}/jobs/interrupt/{fireInstanceId}` interrupts one of them, where
`POST …/jobs/{group}/{name}/interrupt` interrupts every execution of that job. Both are node-local on the
server side: a firing owned by another node is interrupted by asking that node.

A listing gives you headers, not whole objects. To get the full detail for a page, post the keys back:

- `POST {ApiPath}/schedulers/{name}/jobs/fetch` — body is an array of `{ "name": …, "group": … }`, response is the job details
- `POST {ApiPath}/schedulers/{name}/triggers/fetch` — the same, returning triggers

Keys that do not exist are simply absent from the response, and at most 1000 keys can be fetched per call.

::: warning Changed in 4.x
These endpoints previously returned bare arrays of keys, or a `{ "names": [ … ] }` object for the group and
calendar listings. Both shapes are gone; every listing returns the paged envelope above.
`GET {ApiPath}/schedulers/{name}/triggers/groups/paused` was removed — use
`GET {ApiPath}/schedulers/{name}/triggers/groups?paused=true`.
:::

## Cluster nodes

`GET {ApiPath}/schedulers/{name}/nodes` answers with the scheduler's nodes — the node that handled the
request first, then the rest by instance id. It is not paged: a cluster is a handful of nodes, not a
data set.

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

`state` is `Alive`, `Overdue` or `Failed`, and is decided by the same predicate the store's recovery
sweep applies — a node reported `Failed` is a node whose in-flight work the cluster is about to take
over, after which it stops being listed. `Overdue` means a missed check-in and nothing more.

`checkInInterval` is a `TimeSpan` like every other duration here, and it is the interval *that* node
was configured with rather than the reader's. Both times are `null` when the store keeps no check-in
history — an in-memory store, or a persistent one with clustering switched off — which is what a
single-node answer looks like: one node, `isCurrentNode: true`, `Alive`, and no times. `null` is not
zero, so a reader must not fall back to `DateTimeOffset.MinValue` here.

The verdicts are what the answering node believes, read off its own clock, so on a cluster with skewed
clocks two nodes can disagree. Join the listing to
`GET {ApiPath}/schedulers/{name}/jobs/fire-instances` on `schedulerInstanceId` to see what each node is
running.

## Pause and resume report what they did

Pause and resume are the mutations most often aimed at a key that has gone, so they are worth spelling
out — but the rule is the general one above, and every single-key mutation that can be a no-op answers
the same way:

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

(`POST …/jobs/delete` and `POST …/triggers/unschedule` take a key set, so they answer with the keys
they applied to, [like every other key-set form](#a-whole-set-of-keys-in-one-call).)

`applied` is `false` when the key does not exist or the operation was a no-op (pausing an already
paused trigger, resuming a trigger that was not paused, resetting a trigger that is not in the error
state). The group-matcher forms — `POST …/jobs/pause`, `…/jobs/resume`, `…/triggers/pause`,
`…/triggers/resume` — return the names of the groups the operation affected:

```json
{ "groups": [ "reporting", "imports" ] }
```

Those four are the wire form of `PauseJobGroups`, `ResumeJobGroups`, `PauseTriggerGroups` and
`ResumeTriggerGroups`: a group operation, answering with the groups it recorded, where the
`…/keys/pause` and `…/keys/resume` routes beside them answer with the keys they moved.

### A whole set of keys in one call

Pausing forty triggers one request at a time is forty round trips, forty scheduling signals and forty
chances to get half of them done. The key-set forms take the keys in the body and answer with the keys
they applied to:

| Endpoint | Body | Answers |
|---|---|---|
| `POST …/jobs/keys/pause`, `…/jobs/keys/resume` | `{ "jobs": [ { "name": …, "group": … } ] }` | `{ "jobs": [ … ] }` |
| `POST …/triggers/keys/pause`, `…/triggers/keys/resume` | `{ "triggers": [ { "name": …, "group": … } ] }` | `{ "triggers": [ … ] }` |
| `POST …/triggers/keys/reset-from-error-state` | `{ "triggers": [ … ] }` | `{ "triggers": [ … ] }` |
| `POST …/jobs/delete` | `{ "jobs": [ { "name": …, "group": … } ] }` | `{ "jobs": [ … ] }` |
| `POST …/triggers/unschedule` | `{ "triggers": [ { "name": …, "group": … } ] }` | `{ "triggers": [ … ] }` |

The answer is the plural of `{ "applied": … }`: a key the operation did not apply to — one that names
nothing, one that was already paused, one that was not in the error state — is simply **absent** from
the list, never an error. The order is the order the keys were given in. `answer.length ===
request.length` is the "every key was found" question, and when it is not, the list says which ones
were.

The pause, resume and reset forms live under `keys/` because the collection-level `pause` and
`resume` already belong to the group-matcher forms, which select by query string rather than by body.
`delete` and `unschedule` had no group-matcher form when they were named, so the plain path is theirs
and the group form says which one it is: `delete-by-group` and `unschedule-by-group`.

The server does the whole set in one pass — one lock and one transaction for the ADO store — and
signals the scheduling change once for the call. Listener events stay per key: one `TriggerPaused` /
`JobPaused` / `TriggerResumed` / `JobResumed` / `JobDeleted` / `JobUnscheduled` for each key the
operation applied to, and nothing at all for the rest. There is no key-set listener event,
deliberately: `TriggersPaused(null)` means *every group*, and a monitoring listener would read it as
a total outage.

### A whole group in one call

Deleting by group is how a caller calls off a correlation — a saga, a tenant, a conversation, all of
whose firings share a trigger group — without listing its keys first, which is a window in which
another node can add one more.

| Endpoint | Selects by | Answers |
|---|---|---|
| `POST …/jobs/delete-by-group` | `?groupEquals=`, `?groupStartsWith=`, `?groupEndsWith=`, `?groupContains=` | `{ "jobs": [ … ] }` |
| `POST …/triggers/unschedule-by-group` | the same four | `{ "triggers": [ … ] }` |

The four query parameters are the ones `…/jobs/pause` and `…/triggers/pause` take, and naming none of
them means *every group*. The answer is the keys, not the group names the pause endpoints return: a
deleted group has nothing left to remember about it, so what a caller can act on is what went. A job
left with no triggers and no durability goes with its triggers and is not named — the answer to an
unschedule is about triggers.

The server resolves the group inside the same lock that empties it, and signals the scheduling change
once. Listener events are per key here too: one `JobDeleted` or `JobUnscheduled` for each key removed,
and nothing when the group was empty.

## Configuration options

`QuartzHttpApiOptions` supports:

| Option | Default | What it does |
|---|---|---|
| `ApiPath` | `/quartz-api` | The base path every endpoint is served under — see [Where the API is served](#where-the-api-is-served) |
| `IncludeStackTraceInProblemDetails` | `false` | Adds `Quartz-ExceptionStackTrace` to RFC 7807 error payloads, and puts a `500`'s real message back in `detail` |
| `MaxPageSize` | `1000` | The most items one paged request may return; `0` leaves them unbounded — see [Listing endpoints are paged](#listing-endpoints-are-paged) |
| `SchedulerAuthorizationPolicy` | none | The policy every route that names a scheduler is held to, evaluated against that scheduler — see [Authorizing per scheduler](#authorizing-per-scheduler) |

There is one set of these per process, not one per scheduler: `ApiPath` describes the endpoints, and
every scheduler is reached under it. Calling `services.AddQuartzHttpApi(configure)` twice therefore
configures the same options twice, and the callback registered last wins for any setting both of them
touch.

### Authorizing per scheduler

`RequireAuthorization(...)` on what `MapQuartzHttpApi()` returns covers the whole API uniformly, which is
the right shape when everyone who reaches the API may reach every scheduler in the process. When they may
not, name a policy in `SchedulerAuthorizationPolicy` and it is evaluated per request as
`IAuthorizationService.AuthorizeAsync(user, new SchedulerResource(name), policy)`, against the
`{schedulerName}` the route carries. A caller who fails gets `403` with problem details,
decided **before** the scheduler is looked up — so a `404` only ever answers a name the caller was allowed
to ask about — and `GET {ApiPath}/schedulers` is filtered to the schedulers they may act on. The
application writes one `AuthorizationHandler<TRequirement, SchedulerResource>`; Quartz supplies the
resource and asks. `QuartzDashboardOptions.SchedulerAuthorizationPolicy` takes the same policy against the
same resource, so one handler answers for both. The worked example, with the handler, is in
[Multi-tenancy](../multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler).

Setting it in a container with no authorization services fails at startup. The check is authorization and
never authentication: an anonymous caller gets whatever the policy says, which is a `403` when it refuses,
so keep `RequireAuthorization()` on the mapped group if they should be challenged with a `401` first.

## Calling it from .NET

`Quartz.HttpClient` is the client half of this contract. Its `HttpScheduler` implements `IScheduler` over
these endpoints, so code that schedules jobs against a remote scheduler looks like code that schedules them
against a local one:

```shell
dotnet add package Quartz.HttpClient
```

<!-- snippet: sample_httpapi_client -->
```csharp
IScheduler scheduler = new HttpScheduler("MyScheduler", httpClient);
await scheduler.TriggerJob(new JobKey("nightly-report"));
```
<!-- endSnippet -->

In an application with a container, register it instead and inject `IScheduler` as usual — naming the
`IHttpClientFactory` client that carries the base address and the authentication.

**The base address is the site root plus `ApiPath`, and it must end with `/`.** The endpoint paths this
page documents are relative to it, so a base address of the site root alone answers `404` on every call,
and one without the trailing slash is refused by the `HttpScheduler` constructor.

<!-- snippet: sample_httpapi_client_registration -->
```csharp
// The base address is the site root *plus the API path*, and it must end with "/"
builder.Services.AddHttpClient("quartz", client => client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/"));
builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");
```
<!-- endSnippet -->

The wire format is the one documented on this page, so any HTTP client speaks it; the package is the
convenience of not writing that yourself. [HTTP Client](http-client.md) covers registration,
authentication, serializer matching and what does not travel.

## The wire contract is source-generated

The bodies on this page are a closed set, and Quartz states them as a source-generated
`JsonSerializerContext`: a scheduler, a job detail, a page of triggers, a problem-details error and every
request that goes the other way are described at compile time rather than discovered by reflecting over
the type. The server and `Quartz.HttpClient` share the one context, so both ends of a call are generated,
and adding a body to the API means adding it there too.

Three things on the wire are deliberately left open. An `ITrigger` and an `ICalendar` are read and
written by converters that consult the scheduler's serializer registry — which is what lets a custom
trigger or calendar type travel at all — and the values inside a `JobDataMap` are whatever the
application put there, so nothing generated can name them ahead of time. The generated contract is asked
first and reflection second, so a body never reflects on its way to those converters, and the reflection
that remains is over the payload rather than over the contract.

## Production hardening

- Require authentication/authorization on `MapQuartzHttpApi()`. Startup refuses a mapping that states
  nothing, but `AllowAnonymous()` is a way to say nothing that startup accepts — do not reach for it to
  make the message go away
- Do not expose either this or the [dashboard](dashboard.md) to a network you would not hand a shell on:
  a job's type is a string the request names, and `Quartz.Jobs` puts `NativeJob` within reach of it
- Keep `IncludeStackTraceInProblemDetails` disabled in production — it returns the stack trace *and* a
  `500`'s real message
- Restrict mutating operations (schedule, delete, pause/resume, shutdown) to trusted operator roles.
  Quartz has no per-operation permission model to do it with: **a caller who passes authorization is
  trusted with the whole API**, down to reading every job's data map. `SchedulerAuthorizationPolicy`
  narrows *which schedulers* a caller reaches and nothing else, so anything finer belongs in the policy
  or in a gateway in front of this
- Leave `MaxPageSize` set. One request cannot then materialize an unbounded result
- In clustered setups, treat API calls as scheduler control operations that affect cluster-wide behavior
- There is **no rate limiting** on this surface. ASP.NET Core's own rate limiter middleware applies to it
  like any other endpoint, and nothing in Quartz configures one
