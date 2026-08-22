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

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddQuartzHttpApi();

builder.AddQuartz(q => { });
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

The API serves every scheduler in the container through one set of endpoints — a request names the
scheduler it is for — so it is added to the container rather than to a scheduler. The same call can be
written inside an `AddQuartz` callback, `q.AddQuartzHttpApi(...)`, which is convenient when there is one
scheduler and one place that configures it.

Map endpoints:

```csharp
WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapQuartzHttpApi("/quartz-api").RequireAuthorization();
```

### Where the API is served

`/quartz-api` is the default, and there are two ways to say something else:

```csharp
// at the map site, beside the application's other routes
app.MapQuartzHttpApi("/ops/api");

// or at registration
builder.Services.AddQuartzHttpApi(options => options.ApiPath = "/ops/api");
```

Naming the path where the endpoints are mapped is how the rest of ASP.NET Core reads —
`MapHealthChecks("/health")` — and it keeps the route with the application's other routes. If both are
given, **the pattern passed to `MapQuartzHttpApi` wins**; the parameterless overload uses whatever
`ApiPath` says. A pattern given at the map site has to start with `/`, the same rule `ApiPath` is
validated against.

## Endpoint groups

- **Schedulers**: list schedulers, read metadata/context, start, stand-by, shutdown, clear, pause-all, resume-all
- **Jobs**: query jobs, fetch details by key, check existence, list fire instances, pause/resume, trigger, interrupt, add, delete
- **Triggers**: query triggers, fetch by key, read state, pause/resume, reset from error state, schedule/unschedule/reschedule
- **Calendars**: query names, get details, add/replace, delete

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

::: warning Changed in 4.x
`status` and `state` were emitted as integers in the 4.0 previews (`"status": 1`). A client that read
them as numbers needs to read names instead, or parse both.
:::

## Response-shape conventions

Which shape an operation answers with depends on what it has to say:

| Operation | Answers |
|---|---|
| A read that found its target | `200` with the object |
| A read whose target does not exist | `404` with RFC 7807 problem details |
| A write that succeeded and has nothing to report | `200` with an **empty body** — `AddJob`, `TriggerJob`, `PauseAll`, `ScheduleJobs`, … |
| A write whose outcome the caller needs | `200` with a one-field object — `{ "applied": … }`, `{ "jobFound": … }`, `{ "calendarFound": … }`, `{ "triggerFound": … }`, `{ "interrupted": … }`, `{ "groups": [ … ] }` |

An unknown scheduler is `404` whatever the operation was.

`400` has two shapes, and the difference is *where* the request was rejected:

- A query parameter that could not be bound at all — `?take=not-a-number`, `?state=not-a-state` — never
  reaches the endpoint, so the answer is `400` with **no body**.
- A request the endpoint itself rejected — `?skip=-1`, a job with no name, unparseable JSON — is `400`
  with problem details carrying a `detail` that says why.

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

`take` defaults to 250 (`PagedQuery.DefaultTake`) when the request names none — ask for everything
explicitly with `?take=2147483647` — `hasMore` is exact, and `totalCount` is `null` unless
`includeTotalCount=true` was asked for, because computing it costs a second database query. A count
with no rows is `?take=0&includeTotalCount=true`, which the stores answer with the count query alone.

| Endpoint | Returns | Filters (besides paging) |
|---|---|---|
| `GET {ApiPath}/schedulers/{name}/jobs` | Job headers: key, description, `jobType` (the same assembly-qualified name the detail body carries), durable, concurrent-execution-disallowed, persist-job-data, requests-recovery | `groupEquals`, `groupContains`, `groupStartsWith`, `groupEndsWith`, and the four `name*` filters |
| `GET {ApiPath}/schedulers/{name}/jobs/groups` | Job groups: `name`, `paused` | `name` (one group, matched exactly), `paused` |
| `GET {ApiPath}/schedulers/{name}/triggers` | Trigger headers: key, job key, description, trigger type, state, start/end/next/previous fire times, calendar name, priority, execution group | the four `group*` and four `name*` filters, plus `jobName` + `jobGroup` (give both or neither), `calendarName`, `state` |
| `GET {ApiPath}/schedulers/{name}/triggers/groups` | Trigger groups: `name`, `paused` | `name` (one group, matched exactly), `paused` |
| `GET {ApiPath}/schedulers/{name}/calendars` | Calendar names | `nameEquals`, `nameContains`, `nameStartsWith`, `nameEndsWith` |
| `GET {ApiPath}/schedulers/{name}/jobs/fire-instances` | Fire instances: `fireInstanceId`, trigger key, job key (`null` while only reserved), `schedulerInstanceId`, `state`, `fireTimeUtc`, `scheduledFireTimeUtc`, `executionGroup` | the four `group*` and four `name*` filters (they match the *trigger*), plus `jobName` + `jobGroup` (give both or neither), `schedulerInstanceId`, `state` |

Results are ordered by group and then name, and every page uses the same ordering, so paging through a
result is consistent. Fire instances add a third ordering key, the fire instance id, because one trigger
can have several firings at once and group plus name would not order them. At most one `name*` filter may be given per request; more than one is a `400`.
The filter's text is a literal — a calendar named `50%` is matched by `?nameStartsWith=50%25` and is
not a wildcard.

### Fire instances

`GET {ApiPath}/schedulers/{name}/jobs/fire-instances` replaces 4.0-preview's
`GET …/jobs/currently-executing`, which returned a bare array of whole job-execution contexts and could
only ever describe the node that answered. The listing is store-backed, so with a persistent job store it
covers the whole cluster.

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

## Pause and resume report what they did

The single-key mutations that follow the missing-key rule answer `200 OK` with a JSON body telling
whether anything happened:

- `POST …/jobs/{group}/{name}/pause`, `…/resume`
- `POST …/triggers/{group}/{name}/pause`, `…/resume`
- `POST …/triggers/{group}/{name}/reset-from-error-state`

```json
{ "applied": true }
```

`applied` is `false` when the key does not exist or the operation was a no-op (pausing an already
paused trigger, resuming a trigger that was not paused, resetting a trigger that is not in the error
state). The group-matcher forms — `POST …/jobs/pause`, `…/jobs/resume`, `…/triggers/pause`,
`…/triggers/resume` — return the names of the groups the operation affected:

```json
{ "groups": [ "reporting", "imports" ] }
```

::: warning Changed in 4.x
These endpoints previously returned `200 OK` with an empty body. Old clients that ignored the body
keep working, but a 4.0-final `HttpScheduler` against a 4.0-preview server throws on these calls
because it expects the body — upgrade the server first.
:::

### A whole set of keys in one call

Pausing forty triggers one request at a time is forty round trips, forty scheduling signals and forty
chances to get half of them done. The key-set forms take the keys in the body and answer with the keys
they applied to:

| Endpoint | Body | Answers |
|---|---|---|
| `POST …/jobs/keys/pause`, `…/jobs/keys/resume` | `{ "jobs": [ { "name": …, "group": … } ] }` | `{ "jobs": [ … ] }` |
| `POST …/triggers/keys/pause`, `…/triggers/keys/resume` | `{ "triggers": [ { "name": …, "group": … } ] }` | `{ "triggers": [ … ] }` |
| `POST …/triggers/keys/reset-from-error-state` | `{ "triggers": [ … ] }` | `{ "triggers": [ … ] }` |

The answer is the plural of `{ "applied": … }`: a key the operation did not apply to — one that names
nothing, one that was already paused, one that was not in the error state — is simply **absent** from
the list, never an error. The order is the order the keys were given in.

They live under `keys/` because the collection-level `pause` and `resume` already belong to the
group-matcher forms, which select by query string rather than by body.

The server does the whole set in one pass — one lock and one transaction for the ADO store — and
signals the scheduling change once for the call. Listener events stay per key: one `TriggerPaused` /
`JobPaused` / `TriggerResumed` / `JobResumed` for each key the operation applied to, and nothing at
all for the rest. There is no key-set listener event, deliberately: `TriggersPaused(null)` means
*every group*, and a monitoring listener would read it as a total outage.

## Configuration options

`QuartzHttpApiOptions` supports:

- `ApiPath` (default: `/quartz-api`) - base path for all API endpoints
- `IncludeStackTraceInProblemDetails` (default: `false`) - includes stack traces in RFC 7807 error payloads

There is one set of these per process, not one per scheduler: `ApiPath` describes the endpoints, and
every scheduler is reached under it. Calling `AddQuartzHttpApi(configure)` from inside two `AddQuartz`
callbacks therefore configures the same options twice, and the callback registered last wins for any
setting both of them touch.

## Calling it from .NET

`Quartz.HttpClient` is the client half of this contract. Its `HttpScheduler` implements `IScheduler` over
these endpoints, so code that schedules jobs against a remote scheduler looks like code that schedules them
against a local one:

```shell
dotnet add package Quartz.HttpClient
```

```csharp
IScheduler scheduler = new HttpScheduler("MyScheduler", httpClient);
await scheduler.TriggerJob(new JobKey("nightly-report"));
```

In an application with a container, register it instead and inject `IScheduler` as usual — naming the
`IHttpClientFactory` client that carries the base address and the authentication:

```csharp
builder.Services.AddHttpClient("quartz", client => client.BaseAddress = new Uri("https://scheduler.example.com/"));
builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");
```

The wire format is the one documented on this page, so any HTTP client speaks it; the package is the
convenience of not writing that yourself. [HTTP Client](http-client.md) covers registration,
authentication, serializer matching and what does not travel.

## Production hardening

- Require authentication/authorization on `MapQuartzHttpApi()`
- Keep `IncludeStackTraceInProblemDetails` disabled in production
- Restrict mutating operations (schedule, delete, pause/resume, shutdown) to trusted operator roles
- In clustered setups, treat API calls as scheduler control operations that affect cluster-wide behavior
