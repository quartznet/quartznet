---
title: HTTP API
---

Quartz HTTP API is provided by [Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) and exposes scheduler management endpoints for ASP.NET Core apps.

## Installation

Add package references:

```shell
Install-Package Quartz.AspNetCore
Install-Package Quartz
```

## Basic setup

Configure Quartz and enable the HTTP API:

```csharp
services.AddQuartzHttpApi();

services.AddQuartz(q => { });
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

The API serves every scheduler in the container through one set of endpoints — a request names the
scheduler it is for — so it is added to the container rather than to a scheduler. The same call can be
written inside an `AddQuartz` callback, `q.AddQuartzHttpApi(...)`, which is convenient when there is one
scheduler and one place that configures it.

Map endpoints:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.UseEndpoints(endpoints =>
{
    endpoints.MapQuartzHttpApi("/quartz-api").RequireAuthorization();
});
```

### Where the API is served

`/quartz-api` is the default, and there are two ways to say something else:

```csharp
// at the map site, beside the application's other routes
endpoints.MapQuartzHttpApi("/ops/api");

// or at registration
services.AddQuartzHttpApi(options => options.ApiPath = "/ops/api");
```

Naming the path where the endpoints are mapped is how the rest of ASP.NET Core reads —
`MapHealthChecks("/health")` — and it keeps the route with the application's other routes. If both are
given, **the pattern passed to `MapQuartzHttpApi` wins**; the parameterless overload uses whatever
`ApiPath` says. A pattern given at the map site has to start with `/`, the same rule `ApiPath` is
validated against.

## Endpoint groups

- **Schedulers**: list schedulers, read metadata/context, start, stand-by, shutdown, clear, pause-all, resume-all
- **Jobs**: query jobs, fetch details by key, check existence, list currently executing, pause/resume, trigger, interrupt, add, delete
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

Three shapes, and which one an operation uses depends on what it has to say:

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
| `GET {ApiPath}/schedulers/{name}/jobs` | Job headers: key, description, `jobType` (the same assembly-qualified name the detail body carries), durable, concurrent-execution-disallowed, persist-job-data, requests-recovery | `groupEquals`, `groupContains`, `groupStartsWith`, `groupEndsWith` |
| `GET {ApiPath}/schedulers/{name}/jobs/groups` | Job groups: `name`, `paused` | `paused` |
| `GET {ApiPath}/schedulers/{name}/triggers` | Trigger headers: key, job key, description, trigger type, state, start/end/next/previous fire times, calendar name, priority, execution group | the four `group*` filters, plus `jobName` + `jobGroup` (give both or neither), `calendarName`, `state` |
| `GET {ApiPath}/schedulers/{name}/triggers/groups` | Trigger groups: `name`, `paused` | `paused` |
| `GET {ApiPath}/schedulers/{name}/calendars` | Calendar names | — |

Results are ordered by group and then name, and every page uses the same ordering, so paging through a
result is consistent.

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

## Configuration options

`QuartzHttpApiOptions` supports:

- `ApiPath` (default: `/quartz-api`) - base path for all API endpoints
- `IncludeStackTraceInProblemDetails` (default: `false`) - includes stack traces in RFC 7807 error payloads

There is one set of these per process, not one per scheduler: `ApiPath` describes the endpoints, and
every scheduler is reached under it. Calling `AddQuartzHttpApi(configure)` from inside two `AddQuartz`
callbacks therefore configures the same options twice, and the callback registered last wins for any
setting both of them touch.

## Production hardening

- Require authentication/authorization on `MapQuartzHttpApi()`
- Keep `IncludeStackTraceInProblemDetails` disabled in production
- Restrict mutating operations (schedule, delete, pause/resume, shutdown) to trusted operator roles
- In clustered setups, treat API calls as scheduler control operations that affect cluster-wide behavior
