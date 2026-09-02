---
title: 'HTTP Client'
---

[Quartz.HttpClient](https://www.nuget.org/packages/Quartz.HttpClient) is the client half of the
[HTTP API](http-api.md). `HttpScheduler` is a full `IScheduler` implementation whose calls go over the
wire, so an operator process, a control panel or a deployment script schedules jobs against a remote
scheduler with the same code it would use against a local one.

```shell
dotnet add package Quartz.HttpClient
```

## What it pairs with

The server has to be running the Quartz HTTP API, from `Quartz.AspNetCore`:

<!-- snippet: sample_httpclient_server_side -->
```csharp
builder.Services.AddQuartzHttpApi();
// ...
app.MapQuartzHttpApi("/quartz-api").RequireAuthorization();
```
<!-- endSnippet -->

Two things must line up:

- **The path.** The client's `HttpClient.BaseAddress` plus the API path must reach the endpoints. The
  simplest arrangement is a base address that already includes the API path.
- **The scheduler name.** Every request names the scheduler it is for, and the name the client is
  registered with must be the remote scheduler's own `SchedulerName`. A mismatch is a `404`, not a
  connection error.

`BaseAddress` must end in `/`; the constructor rejects one that does not, because relative endpoint
paths would otherwise resolve against the wrong segment.

## Registering the client

The recommended shape names an `IHttpClientFactory` client, so the handler is pooled and recycled:

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

There are three overloads:

| Overload | Use when |
|---|---|
| `AddQuartzHttpClient(string schedulerName, string httpClientName, JsonSerializerOptions?)` | the client is registered with `AddHttpClient` — the normal case |
| `AddQuartzHttpClient(string schedulerName, Func<IServiceProvider, HttpClient> createHttpClient, JsonSerializerOptions?)` | the client is assembled from other services, or from something the factory does not know about |
| `AddQuartzHttpClient(Action<HttpClientOptions> configure)` | you want to set several things at once |

`HttpClientOptions` carries `SchedulerName`, `HttpClientName`, `CreateHttpClient` and
`JsonSerializerOptions`. **Exactly one** of `HttpClientName` and `CreateHttpClient` must be set; giving
neither or both fails validation at registration, with the same `OptionsValidationException` every other
Quartz options type throws.

`CreateHttpClient` runs once, when the scheduler is first resolved, and is handed the container. The
client it returns belongs to whoever created it — the scheduler never disposes it. That is why the
option is a factory rather than an `HttpClient`: an options object is bound, cached and shared, and a
live client sitting in one has no owner.

### Injecting it

A remote scheduler is registered exactly like a local one: **keyed by its name**, and unkeyed as well
while it is the only scheduler in the container.

<!-- snippet: sample_httpclient_controller -->
```csharp
public sealed class OpsController(IScheduler scheduler);                          // one scheduler
```
<!-- endSnippet -->

Once a second scheduler joins the container, name the one you meant:

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

The unkeyed registration is `TryAdd`, so a second remote scheduler does not quietly take over what
"the scheduler" means — with two of them, inject by key.

### Beside a local scheduler

`AddQuartz()` registers the local default scheduler in the same unkeyed slot, so in a container that has
both, **call `AddQuartz()` first**:

<!-- snippet: sample_httpclient_beside_local -->
```csharp
builder.Services.AddQuartz();                                   // owns GetRequiredService<IScheduler>()
builder.Services.AddQuartzHttpClient("MyScheduler", "quartz");  // reachable by name
```
<!-- endSnippet -->

The local scheduler then owns `GetRequiredService<IScheduler>()`, and the remote one is reached with
`GetRequiredKeyedService<IScheduler>("MyScheduler")` or `[FromKeyedServices("MyScheduler")]` — which is
where it always is, whichever order the two calls are written in.

The other order throws an `InvalidOperationException` at registration. Registration is first-wins, so
`AddQuartzHttpClient(...)` followed by `AddQuartz()` would leave "the scheduler" meaning the remote one
with nothing said about it, and a program that thought it held its own scheduler would be scheduling jobs
in somebody else's process. A named local scheduler — `AddQuartz("Local", …)` — is keyed by its name and
never wanted the unkeyed slot, so it can be registered on either side.

::: warning Changed in 4.x
Driving two remote schedulers used to need a marker interface of its own, implemented by a type emitted
at runtime. The service key says the same thing without the reflection, so the generic
`AddQuartzHttpClient<TScheduler>()` overloads are gone.
:::

Registration also binds the scheduler into the container's `ISchedulerRepository`, so it shows up in
`GetAllSchedulers`, in the dashboard and in a locally hosted HTTP API. Under a host that happens at
startup rather than on first injection; a container with no host stays exactly as lazy as it was.

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

The client carries no authentication of its own — it is an `HttpClient`, so whatever you would do for
any other API works here:

```csharp
builder.Services.AddHttpClient("quartz", client =>
    {
        client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/");
    })
    .AddHttpMessageHandler<BearerTokenHandler>()
    .AddStandardResilienceHandler();
```

Match it on the server with `app.MapQuartzHttpApi("/quartz-api").RequireAuthorization()`. The API is
scheduler *control* — shutdown, delete, pause-all are all in it — so an unauthenticated endpoint is a
remote kill switch.

## Serialization must match the server

Both ends speak the Quartz wire format, which is System.Text.Json with Quartz's own converters. The
client builds its options from a copy of whatever you pass in, adds those converters to the copy, and
leaves your instance untouched — so sharing one `JsonSerializerOptions` across several clients is safe.

Custom trigger and calendar types need their serializers registered **on both sides**. The remote
scheduler's registrations are invisible from this process, so the client cannot discover them:

<!-- snippet: sample_httpclient_custom_serializers -->
```csharp
SystemTextJsonSerializerRegistry registry = new();
registry.AddTriggerSerializer<MyTrigger>(new MyTriggerSerializer());

IScheduler scheduler = new HttpScheduler("MyScheduler", http, jsonSerializerOptions: null, registry);
```
<!-- endSnippet -->

Registering through the container instead — the same `AddQuartz`-side serializer registration the
server uses — is picked up automatically, because `AddQuartzHttpClient` resolves the container-wide
registry.

## What travels, and what does not

The wire carries data, not objects. Three consequences are worth knowing before you build on this:

**Job details are rebuilt.** A `JobDetailDto` carries name, group, job type name, description,
`Durable`, `RequestsRecovery`, `ConcurrentExecutionDisallowed`, `PersistJobDataAfterExecution` and the
job data map. `GetJobDetail` reconstructs a standard job detail from those fields, so a custom
`IJobDetail` implementation on the server comes back as the ordinary one and any behaviour that lived
in your type stays on the server.

**The job type is a name.** It is the assembly-qualified type name as the server has it. The client
does not need the type to exist locally to list, pause or trigger a job — only to reason about it.

**Enums are names.** `status`, `state`, `repeatIntervalUnit`, `daysOfWeek` — all of them travel as the
C# member name, and the names are the contract. Numeric forms are still accepted on input, which is
what keeps an older client working.

## What is not supported remotely

All three throw `NotSupportedException`, with a message that names the member and says why.

| Member | Why not |
|---|---|
| `Context` | the scheduler context is a live object in the scheduler's own process; a copy fetched over HTTP could not be written back |
| `ListenerManager` | listeners run in the process that executes jobs |
| `UpdateTriggerDetails` | the HTTP API has no endpoint for it |

Listeners are the important one: a `TriggerListener` registered on a client would never see anything,
because nothing fires here. Register listeners where the scheduler actually runs.

`Context` is the one that changed shape in the 4.0 alpha series: it used to make a synchronous HTTP
call from a property getter and hand back a detached copy of the remote context, which blocked the
calling thread and silently discarded anything written to it. Read scheduler-wide state from the
endpoint (`GET {apiPath}/schedulers/{name}/context`) if you need it.

## Blocking members

`IScheduler` has three members that are properties rather than methods, and over HTTP two of them are
a request:

`SchedulerInstanceId` and `Status` call the remote scheduler **synchronously**, blocking the calling
thread for the round trip. `SchedulerName` is the one that is free — the client already knows it.
`Context` is not in this list because it does not reach the remote scheduler at all; see above.

`Status` is one request for the whole lifecycle, where the `IsStarted` / `InStandbyMode` / `IsShutdown`
it replaces were three requests to the same endpoint, each reading a different field of the same answer.

Do not touch them on a request path. `GetMetadata()` is the async member that answers most of the same
questions in one call:

<!-- snippet: sample_httpclient_metadata -->
```csharp
SchedulerMetadata metadata = await scheduler.GetMetadata(cancellationToken);
```
<!-- endSnippet -->

Its `IsProxy` is `true` for an HTTP scheduler, and the three type properties — `SchedulerTypeName`,
`JobStoreTypeName`, `ThreadPoolTypeName` — are **strings**, not `System.Type`. That is what lets the
metadata describe a remote scheduler whose types do not exist in this process.

## Paging and bulk fetch over the wire

The query family maps straight onto query-string parameters:

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

`Skip`, `Take` and `IncludeTotalCount` become `skip`, `take` and `includeTotalCount`; matchers become
`groupStartsWith`, `nameEquals` and their siblings. `take` defaults to 250 at both ends, so a client
that leaves it unset gets the same page size the server would have chosen.

`QueryFireInstances` works the same way and is how a remote console shows what is running — across the
whole cluster, since the listing is store-backed. `QueryClusterNodes` is its companion and takes no
query at all: it reads `GET …/schedulers/{name}/nodes` and answers with the nodes themselves, the one
that served the request first. "Current node" therefore means current on the *server*, not on the
client — the client has no identity in the cluster — so the order arrives as the server chose it and is
not re-sorted here.

Bulk fetch posts the keys back:

<!-- snippet: sample_httpclient_bulk_fetch -->
```csharp
List<IJobDetail> details = await scheduler.GetJobDetails(keys, cancellationToken);
```
<!-- endSnippet -->

The endpoint accepts **at most 1000 keys per call**; page the keys if you have more.

## Errors

Anything the server rejects arrives as an `HttpClientException`, which derives from
`SchedulerException`, with the RFC 7807 problem details in the message. Turning on
`QuartzHttpApiOptions.IncludeStackTraceInProblemDetails` on the server puts the server's stack trace in
there too — useful in development, and not something to ship.

A `500` is the exception. From `4.0.0-beta.1` its problem-details `detail` is one fixed sentence —
*"The scheduler failed to handle the request. The failure is recorded in the server's log."* — rather than
the exception's message, so an `HttpClientException` raised by a server fault says only that and points
at the server's log. `IncludeStackTraceInProblemDetails` puts the message back.

The 3.x-compatible listings — `GetJobKeys`, `GetTriggerKeys`, `GetCalendarNames`, `GetJobGroupNames`,
`GetTriggerGroupNames`, `GetPausedTriggerGroups` — ask the server for every match, and a server with
`QuartzHttpApiOptions.MaxPageSize` set (it defaults to 1000) answers with at most that many. Below the cap
they behave exactly as they always have; above it the client raises an `HttpClientException` naming
`MaxPageSize` rather than handing back a page that would read as the whole store. Read a large listing
with the `Query*` members and a `Take` of your own, or raise the cap on the server.

A `404` for a read is not an error: `GetJobDetail` and `GetTrigger` return `null`, exactly as a local
scheduler would.

## See also

- [HTTP API](http-api.md) — the server half, and the full endpoint and wire-format reference
- [Querying Jobs and Triggers](../tutorial/querying-jobs-and-triggers.md) — the query family these calls implement
- [Multiple Schedulers](multiple-schedulers.md) — naming and keying schedulers in one container
