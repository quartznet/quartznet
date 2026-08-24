# Quartz.HttpClient

[Quartz.HttpClient](https://www.nuget.org/packages/Quartz.HttpClient) is the client half of the Quartz
[HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html).
`HttpScheduler` is a full `IScheduler` implementation whose calls go over the wire, so an operator
process, a control panel or a deployment script schedules jobs against a remote scheduler with the same
code it would use against a local one.

## Installation

```shell
dotnet add package Quartz.HttpClient
```

The server has to be running the HTTP API, which ships in
[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore).

## Usage

<!-- snippet: sample_readme_httpclient -->
```csharp
builder.Services.AddHttpClient("quartz", client =>
    client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/"));

builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");
```
<!-- endSnippet -->

`IScheduler` is then injectable like any local one — keyed by scheduler name, and unkeyed while it is
the only scheduler in the container. `new HttpScheduler(name, httpClient)` does the same without a
container.

Two things must line up: the base address plus the API path have to reach the endpoints, and the name
the client is registered with has to be the remote scheduler's own `SchedulerName` — a mismatch is a
`404`, not a connection error. The base address must end in `/`.

`Context` and `ListenerManager` throw `NotSupportedException`: both are live in-process objects of the
scheduler, and listeners run where the jobs run. The remaining property members of `IScheduler`
(`Status`, `SchedulerInstanceId`) each block the calling thread for a round trip, so use
`GetMetadata()` on a request path.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-client.html>
