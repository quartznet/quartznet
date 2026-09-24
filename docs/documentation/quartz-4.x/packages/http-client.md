---
title: 'HTTP Client'
---

[Quartz.HttpClient](https://www.nuget.org/packages/Quartz.HttpClient) is the client half of the
[HTTP API](http-api.md). `HttpScheduler` implements `IScheduler` over HTTP, so an operator process, control
panel or deployment script drives a remote scheduler with the same code as a local one. The
[dashboard](dashboard.md#fronting-a-scheduler-in-another-process-over-http) can render and drive a scheduler
registered this way.

```shell
dotnet add package Quartz.HttpClient
```

## What it pairs with

The server must run the Quartz HTTP API, from `Quartz.AspNetCore`:

<!-- snippet: sample_httpclient_server_side -->
```csharp
builder.Services.AddQuartzHttpApi();
// ...
app.MapQuartzHttpApi("/quartz-api").RequireAuthorization();
```
<!-- endSnippet -->

Two things must match:

- **The path.** `HttpClient.BaseAddress` plus the API path must reach the endpoints. Simplest: a base address
  that includes the API path.
- **The scheduler name.** The client's scheduler name must be the remote scheduler's `SchedulerName`. A
  mismatch is a `404`, not a connection error.

`BaseAddress` must end in `/`, or the constructor rejects it; otherwise relative endpoint paths would resolve
against the wrong segment.

## Registering the client

Name an `IHttpClientFactory` client, so the handler is pooled and recycled:

<!-- snippet: sample_httpclient_registration -->
```csharp
builder.Services.AddHttpClient("quartz", client =>
{
    client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");
```
<!-- endSnippet -->

| Overload | Use when |
|---|---|
| `AddQuartzHttpClient(string schedulerName, string httpClientName, JsonSerializerOptions?)` | the client is registered with `AddHttpClient` (normal case) |
| `AddQuartzHttpClient(string schedulerName, Func<IServiceProvider, HttpClient> createHttpClient, JsonSerializerOptions?)` | the client is built from other services, or from something the factory does not know |
| `AddQuartzHttpClient(Action<HttpClientOptions> configure)` | setting several options at once |

`HttpClientOptions` has `SchedulerName`, `HttpClientName`, `CreateHttpClient` and `JsonSerializerOptions`.

- Set **exactly one** of `HttpClientName` and `CreateHttpClient`. Neither or both fails validation at
  registration with `OptionsValidationException`.
- `CreateHttpClient` runs once, when the scheduler is first resolved, and receives the container.
- The scheduler never disposes the client `CreateHttpClient` returns; its creator owns it. (The option is a
  factory because an options object is bound, cached and shared, and a live client in it would have no owner.)

### Injecting it

A remote scheduler is registered like a local one: **keyed by its name**, and also unkeyed while it is the only
scheduler in the container. See [Multiple Schedulers](multiple-schedulers.md) for naming and keying.

<!-- snippet: sample_httpclient_controller -->
```csharp
public sealed class OpsController(IScheduler scheduler);                          // one scheduler
```
<!-- endSnippet -->

With a second scheduler in the container, name the one you mean:

<!-- snippet: sample_httpclient_keyed_controller -->
```csharp
public sealed class OpsController([FromKeyedServices("MyScheduler")] IScheduler scheduler);
```
<!-- endSnippet -->

<!-- snippet: sample_httpclient_resolve_keyed -->
```csharp
IScheduler reporting = provider.GetRequiredKeyedService<IScheduler>("reporting");
```
<!-- endSnippet -->

The unkeyed registration is `TryAdd`, so a second remote scheduler does not replace the first. With two, inject
by key.

### Beside a local scheduler

`AddQuartz()` registers the local default scheduler in the same unkeyed slot. With both, **call `AddQuartz()`
first**:

<!-- snippet: sample_httpclient_beside_local -->
```csharp
builder.Services.AddQuartz();                                   // owns GetRequiredService<IScheduler>()
builder.Services.AddQuartzHttpClient("MyScheduler", "quartz");  // reachable by name
```
<!-- endSnippet -->

- The local scheduler owns `GetRequiredService<IScheduler>()`.
- The remote one is always `GetRequiredKeyedService<IScheduler>("MyScheduler")` or
  `[FromKeyedServices("MyScheduler")]`.
- The other order throws `InvalidOperationException` at registration. Registration is first-wins, so the unkeyed
  scheduler would silently be the remote one, and code expecting its own scheduler would schedule jobs in
  another process.
- A named local scheduler (`AddQuartz("Local", …)`) is keyed by name and does not use the unkeyed slot, so its
  order does not matter.

::: warning Changed in 4.x
The generic `AddQuartzHttpClient<TScheduler>()` overloads are gone. Two remote schedulers used to need a marker
interface implemented by a runtime-emitted type; the service key replaces it.
:::

Registration also binds the scheduler into the container's `ISchedulerRepository`, so it appears in
`GetAllSchedulers`, the dashboard and a locally hosted HTTP API. Under a host this happens at startup; without a
host it happens on first injection, as before.

### Constructing one directly

No container needed:

<!-- snippet: sample_httpclient_without_container -->
```csharp
using HttpClient http = new() { BaseAddress = new Uri("https://scheduler.example.com/quartz-api/") };
IScheduler scheduler = new HttpScheduler("MyScheduler", http);

await scheduler.TriggerJob(new JobKey("nightly-report", "reports"));
```
<!-- endSnippet -->

## Authentication

The client has no authentication of its own. Configure the `HttpClient` as for any other API:

```csharp
builder.Services.AddHttpClient("quartz", client =>
    {
        client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/");
    })
    .AddHttpMessageHandler<BearerTokenHandler>()
    .AddStandardResilienceHandler();
```

On the server, use `app.MapQuartzHttpApi("/quartz-api").RequireAuthorization()`. The API can shut down, delete
and pause everything, so an unauthenticated endpoint is a remote kill switch.

## Serialization must match the server

The wire format is System.Text.Json with Quartz's converters. The client copies the options you pass and adds
the converters to the copy, so one `JsonSerializerOptions` can be shared across clients.

Register custom trigger and calendar serializers **on both sides**; the client cannot see the remote
scheduler's registrations:

<!-- snippet: sample_httpclient_custom_serializers -->
```csharp
SystemTextJsonSerializerRegistry registry = new();
registry.AddTriggerSerializer<MyTrigger>(new MyTriggerSerializer());

IScheduler scheduler = new HttpScheduler("MyScheduler", http, jsonSerializerOptions: null, registry);
```
<!-- endSnippet -->

A serializer registered in the container (the same registration the server uses with `AddQuartz`) is picked up
automatically, because `AddQuartzHttpClient` resolves the container-wide registry.

## What travels, and what does not

The wire carries data, not objects.

- **Job details are rebuilt.** A `JobDetailDto` carries name, group, job type name, description, `Durable`,
  `RequestsRecovery`, `ConcurrentExecutionDisallowed`, `PersistJobDataAfterExecution` and the job data map.
  `GetJobDetail` builds a standard job detail from them; a custom `IJobDetail` and its behaviour stay on the
  server.
- **The job type is a name**: the server's assembly-qualified type name, treated as text. The client never
  resolves it or loads or probes an assembly for it. The type need not exist locally to list, pause, trigger,
  schedule or add a job.
- **The two attribute-derived flags can be absent.** `concurrentExecutionDisallowed` and
  `persistJobDataAfterExecution` are nullable. `null` means "whatever `[DisallowConcurrentExecution]` /
  `[PersistJobDataAfterExecution]` on the type says"; a value overrides it. Omit them when adding a job to let
  the side that resolves the type decide. A job whose type the answering process cannot resolve reports `null`,
  not `false`, instead of failing.
- **Enums are names.** `status`, `state`, `repeatIntervalUnit`, `daysOfWeek` and the rest travel as the C#
  member name; the names are the contract. Numeric forms are still accepted on input, for older clients.

## What is not supported remotely

Both throw `NotSupportedException`, naming the member and the reason.

| Member | Why not |
|---|---|
| `Context` | a live object in the scheduler's process; a copy over HTTP could not be written back |
| `ListenerManager` | listeners run in the process that executes jobs |

- A `TriggerListener` registered on a client would never see anything, because nothing fires here. Register
  listeners where the scheduler runs.
- **Reading what the listeners report is supported**: the client reads the target's event stream; see
  [Events](#events).
- Instead of `Context`, read `GET {apiPath}/schedulers/{name}/context`.

## Blocking members

`SchedulerInstanceId` and `Status` call the remote scheduler **synchronously**, blocking the thread for the round
trip. `SchedulerName` is free; the client already knows it. (`Context` never reaches the remote scheduler; see
above.)

`Status` is one request, replacing `IsStarted` / `InStandbyMode` / `IsShutdown`, which were three requests to
the same endpoint.

Do not read the two properties on a request path. Call their asynchronous twins on `IScheduler`, `GetStatus()`
and `GetSchedulerInstanceId()`, which make the same one request without holding a thread:

<!-- snippet: sample_httpclient_status -->
```csharp
SchedulerStatus status = await scheduler.GetStatus(cancellationToken);
string instanceId = await scheduler.GetSchedulerInstanceId(cancellationToken);
```
<!-- endSnippet -->

They are default interface members that return the property, so a local scheduler behaves as before at no cost;
only a proxy overrides them. The properties remain, and remain blocking: `IScheduler` declares them, and a
property cannot be awaited.

**Quartz no longer reads either property from a scheduler in another process.**

- `ISchedulerRepository` used to read `Status` under its lock on every lookup, so one unreachable target
  stalled every lookup in the process, including the HTTP API's scheduler resolution, for the client timeout. It
  now skips proxies: unreachable is not shut down, and this process cannot restart a remote scheduler anyway.
- The scheduler listing asks `GetStatus()` and `GetSchedulerInstanceId()` under a two-second deadline for the
  whole listing. A target that does not answer is `SchedulerStatus.Unknown` with no instance id.

Give the client a short `Timeout` anyway: every other read waits for it.

**The timeout does not bound the event stream.** `HttpClient.Timeout` covers the request and reading a
response to its end. The [event stream](#events) opens with `HttpCompletionOption.ResponseHeadersRead`, so the
timeout covers only getting the response headers. A ten-second `Timeout` and a stream open all afternoon work
together.

`GetMetadata()` returns both values and the rest of the scheduler's details in one request; prefer it when you
need more than the status:

<!-- snippet: sample_httpclient_metadata -->
```csharp
SchedulerMetadata metadata = await scheduler.GetMetadata(cancellationToken);
```
<!-- endSnippet -->

`IsProxy` is `true` for an HTTP scheduler. `SchedulerTypeName`, `JobStoreTypeName` and `ThreadPoolTypeName` are
**strings**, not `System.Type`, so they can describe types that do not exist in this process.

## History

`AddQuartzHttpClient` also registers an `IExecutionHistoryStore`, keyed by the scheduler's name. It reads what
the target has **run** and **missed** through the API's [history routes](http-api.md#execution-history); a job
store holds only what is scheduled. It is what gives a dashboard fronting a scheduler over HTTP its History
page.

```csharp
IExecutionHistoryStore history = provider.GetRequiredKeyedService<IExecutionHistoryStore>("QuartzScheduler");
PagedResult<ExecutionHistoryEntry> page = await history.QueryExecutions(new ExecutionHistoryQuery
{
    SchedulerName = "QuartzScheduler",
    JobContains = "nightly"
});
```

- Read-only: history is recorded where jobs run, so `AddExecution` and `AddMisfire` throw
  `NotSupportedException`.
- Every read also throws `NotSupportedException` when the target's API predates the history routes (it answers
  `404`), so a caller can show "this target serves no history".
- A `404` naming an unknown scheduler still arrives as `HttpClientException`.

## Events

`AddQuartzHttpClient` also registers a reader of the target's [event stream](http-api.md#the-event-stream),
keyed by the scheduler's name. It gives a live view of a scheduler in another process; the dashboard's Live Logs
page reads it.

```csharp
ISchedulerEventSource events = provider.GetRequiredKeyedService<ISchedulerEventSource>("QuartzScheduler");

await foreach (SchedulerEvent raised in events.Subscribe("QuartzScheduler", cancellationToken))
{
    Console.WriteLine($"{raised.OccurredAtUtc:u} {raised.Kind} on {raised.SchedulerInstanceId}");
}
```

::: tip Internal in 4.1
`ISchedulerEventSource`, `SchedulerEvent` and `SchedulerEventKind` are **internal** in 4.1: the sample shows what
the dashboard does, not callable API. The [wire format](http-api.md#the-event-stream) is public and stable, so
read the route directly with `SseParser`, a browser `EventSource`, or any server-sent events client. A public
seam over the reader may be added later.
:::

- One subscription is one enumeration, however many connections it takes.
- A dropped stream is reopened after a delay that doubles from one second to thirty; a connection that delivers
  anything resets it. A restarting target is a gap, not the end of the feed.
- Nothing is replayed across a reconnection. For what fell into the gap, use the
  [history routes](http-api.md#execution-history).
- Heartbeats are consumed by the reader (to tell a quiet scheduler from a dead connection), not passed on.

Retried: a refused connection, a dropped socket, a gateway error, the client's own timeout. Reported, ending the
enumeration:

| Response | Arrives as |
|---|---|
| `404` with no body (API predates the route) | `NotSupportedException`: the target serves no event stream |
| Unknown scheduler | `HttpClientException` |
| Refused by the target's policy | the `403` |

## Paging and bulk fetch over the wire

The query family (see [Querying Jobs and Triggers](../tutorial/querying-jobs-and-triggers.md)) maps onto
query-string parameters:

<!-- snippet: sample_httpclient_query_triggers -->
```csharp
PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
{
    Group = GroupMatcher<TriggerKey>.GroupStartsWith("reporting-"),
    State = TriggerState.Error,
    Skip = 0,
    Take = 100,
    IncludeTotalCount = true,
}, cancellationToken);
```
<!-- endSnippet -->

- `Skip`, `Take` and `IncludeTotalCount` become `skip`, `take` and `includeTotalCount`.
- Matchers become `groupStartsWith`, `nameEquals` and their siblings.
- `take` defaults to 250 at both ends.
- `QueryFireInstances` works the same way and shows what is running across the whole cluster (the listing is
  store-backed).
- `QueryClusterNodes` takes no query. It reads `GET …/schedulers/{name}/nodes` and returns the nodes, the one that
  served the request first. "Current" means current on the *server* (the client has no cluster identity); the
  order is not re-sorted.

Bulk fetch posts the keys back:

<!-- snippet: sample_httpclient_bulk_fetch -->
```csharp
List<IJobDetail> details = await scheduler.GetJobDetails(keys, cancellationToken);
```
<!-- endSnippet -->

The endpoint accepts **at most 1000 keys per call**; page the keys if you have more.

## Errors

**A server-side exception is rethrown as the same type.** The problem details name the exception type and the
client rebuilds it, so a `catch` written for a local scheduler works against a remote one:

| The server raised | The client rethrows |
|---|---|
| `SchedulerException` | `SchedulerException` |
| `InvalidConfigurationException` | `InvalidConfigurationException` |
| `JobExecutionException` | `JobExecutionException` |
| `JobPersistenceException` | `JobPersistenceException` |
| `SchedulerConfigException` | `SchedulerConfigException` |
| `LockException` | `LockException` |
| `NoSuchDelegateException` | `NoSuchDelegateException` |
| `ObjectAlreadyExistsException` | `ObjectAlreadyExistsException` |
| anything else | `HttpClientException` |

`ObjectAlreadyExistsException` is what `ScheduleJob` and `AddJob` raise for a duplicate; catching it works over
HTTP as in process.

`HttpClientException` covers the rest: a request rejected before it reached a scheduler, an unknown scheduler
name, a response without problem details, an unreadable body.

- It derives from `SchedulerException`, so one `catch (SchedulerException)` covers both.
- Its message carries the RFC 7807 problem details. `QuartzHttpApiOptions.IncludeStackTraceInProblemDetails` on
  the server adds the server's stack trace; use it in development only.
- For a `500`, `detail` is a fixed sentence, *"The scheduler failed to handle the request. The failure is
  recorded in the server's log."*, not the exception's message. `IncludeStackTraceInProblemDetails` restores the
  message.

The 3.x-compatible listings (`GetJobKeys`, `GetTriggerKeys`, `GetCalendarNames`, `GetJobGroupNames`,
`GetTriggerGroupNames`, `GetPausedTriggerGroups`) request every match. The server returns at most
`QuartzHttpApiOptions.MaxPageSize` (default 1000). Above that cap the client throws an `HttpClientException`
naming `MaxPageSize` instead of returning a partial list. For large listings, use the `Query*` members with your
own `Take`, or raise the cap on the server.

A `404` on a read is not an error: `GetJobDetail` and `GetTrigger` return `null`, as a local scheduler would.

## Security: what the server can and cannot make the client do

The client trusts the server for *data*, and for nothing else.

- **Names stay names.** Nothing in the client calls `Type.GetType` on a server-supplied string. A server cannot
  make your runtime look for an assembly it names, which would run a module initializer in whatever matched and
  steer your `AssemblyResolve` handlers.
- **Error bodies are matched against a closed list** of eight known Quartz exceptions. Anything else becomes an
  `HttpClientException` with the server's `detail` as text. No type is loaded or activated by name.
- **The transport is yours.** Quartz changes none of your `HttpClient` settings: TLS and certificate
  validation, redirects (`HttpClientHandler.AllowAutoRedirect`, on by default), the response buffer cap
  (`HttpClient.MaxResponseContentBufferSize`) and the timeout. For a server you do not control, set
  `MaxResponseContentBufferSize` and a `Timeout`, and turn redirects off if credentials travel in a header.
- **Credentials are yours.** Any `DelegatingHandler` or default header you attach goes on every request to that
  `BaseAddress`; see [Authentication](#authentication).

The server's trust boundary is in [Production hardening](http-api.md#production-hardening).
