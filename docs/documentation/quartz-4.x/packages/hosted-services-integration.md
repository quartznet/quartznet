---

title: Hosted Services Integration
---

[Quartz](https://www.nuget.org/packages/Quartz) runs schedulers as a
[hosted service](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services), started and
stopped with the application.

## Installation

The hosted service is in the core package. The `worker` and `web` templates already reference the host; a plain
`console` project needs it added:

```shell
dotnet add package Quartz
dotnet add package Microsoft.Extensions.Hosting
```

## Using

Call `AddQuartzHostedService` on the host application builder or on `IServiceCollection`. Configuring the
scheduler, jobs and triggers is covered in [Microsoft DI Integration](microsoft-di-integration); several
schedulers in one application in [Multiple Schedulers](multiple-schedulers.md).

- The hosted service starts every scheduler in the container.
- It resolves them when the host starts, so `AddQuartz` and `AddQuartzHostedService` can be called in either
  order.
- Options apply to every scheduler. Override one by name with
  `AddQuartzHostedService("SchedulerName", options => …)`.

::: warning
`AddQuartzHostedService()` with no scheduler registered throws at startup. Register one with `AddQuartz(...)`.
:::

**Example program utilizing hosted services configuration**

<!-- snippet: sample_hosted_program -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// see Quartz documentation about how to configure different configuration aspects
builder.AddQuartz(q =>
{
    // your configuration here
});

// Quartz hosting
builder.AddQuartzHostedService(options =>
{
    // when shutting down we want jobs to complete gracefully
    options.WaitForJobsToComplete = true;
});

await builder.Build().RunAsync();
```
<!-- endSnippet -->

## Options

`QuartzHostedServiceOptions`:

| Option | Default | Description |
|---|---|---|
| `WaitForJobsToComplete` | `false` | Shutdown waits for running jobs to finish |
| `AwaitApplicationStarted` | `true` | Scheduler starts only after application startup completes |
| `StartDelay` | none | Extra delay before start; counted from startup completion with `AwaitApplicationStarted` |
| `AutoStart` | `true` | `false`: built, initialized and bound, but not started; see [below](#a-scheduler-the-application-starts-itself) |

Without `WaitForJobsToComplete`, the host stops while jobs are still running. Whether running jobs are also
asked to stop is the scheduler's `ShutdownJobInterruption` setting.

To run code around the lifecycle (a warm-up before start, a drain after stop), derive from
`QuartzHostedService` and register the subclass. `StartingAsync`, `StartedAsync`, `StoppingAsync` and
`StoppedAsync` are virtual; `Schedulers` lists the schedulers it runs.

<!-- snippet: sample_hosted_derived_service -->
```csharp
builder.AddQuartzHostedService<WarmUpBeforeSchedulingService>(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

`builder.AddQuartz(...)` is `builder.Services.AddQuartz(...)` that also reads the `Quartz` configuration
section, so `appsettings.json` settings apply before your callback. For a section with another name, use the
`IServiceCollection` overload:

<!-- snippet: sample_hosted_configuration_section -->
```csharp
builder.Services.AddQuartz(builder.Configuration.GetSection("Scheduling"), q => { });
```
<!-- endSnippet -->

A string argument is a scheduler name. `builder.AddQuartz("reporting", …)` registers scheduler `reporting` and
reads `Quartz:Schedulers:reporting` when the section describes several. `builder.AddQuartzSchedulers()`
registers one per child of that sub-section.

## A scheduler the application starts itself

Set `AutoStart = false` when the application must start the scheduler itself: after its own leader election,
after a message bus connects, or when a module comes up late.

<!-- snippet: sample_hosted_deferred_start -->
```csharp
builder.AddQuartz("reporting", q => { });

// Built, initialized and bound with the host, but left in Created for the application to start
builder.AddQuartzHostedService("reporting", options => options.AutoStart = false);
```
<!-- endSnippet -->

- The scheduler is resolved, initialized and bound when the host starts. `ISchedulerRegistry`, the dashboard
  and `GET /schedulers` see it.
- It stays in `Created` until something calls `scheduler.Start()`.
- `AutoStart` overrides `AwaitApplicationStarted` and `StartDelay`, which then do not apply.
- Shutdown is unchanged: the hosted service shuts down every scheduler it created, started or not. Not
  registering the hosted service at all would lose that shutdown handling.
- It is per scheduler: the example defers `reporting` and leaves the others to start with the host.

## Health checks

The scheduler health check is in the core `Quartz` package and registers on the standard
`IHealthChecksBuilder`, so a worker with no web stack can use it.

| Status | When |
|---|---|
| *Healthy* | Running and can reach its store |
| *Degraded* | In standby, or in `Created` with `AutoStart = false` |
| *Unhealthy* | Anything else |

<!-- snippet: sample_hosted_health_check -->
```csharp
builder.Services.AddHealthChecks().AddQuartz();
```
<!-- endSnippet -->

`AddQuartz("reporting", q => q.AddQuartzHealthChecks())` registers the same check from a named scheduler's
builder, without repeating the name.

The check reads each scheduler's own `QuartzHostedServiceOptions`. A scheduler in `Created` that did not opt
out of `AutoStart` is *unhealthy*, including in an application with no hosted service, where nothing will
start it.

Customize the registration in the callback, for example with tags for separate liveness and readiness probes:

<!-- snippet: sample_hosted_health_check_options -->
```csharp
builder.Services.AddHealthChecks().AddQuartz(options =>
{
    options.Name = "quartz-scheduler";   // the default, or quartz-scheduler-<name> for a named scheduler
    options.Tags.AddRange(["ready", "live"]);
    options.FailureStatus = HealthStatus.Unhealthy;

    // What a scheduler in standby reports. Degraded by default; say Unhealthy where a standby
    // node must leave the rotation and there is no HTTP probe to remap the status code at.
    options.StandbyStatus = HealthStatus.Unhealthy;
});
```
<!-- endSnippet -->

`QuartzHealthCheckOptions` go through the options pipeline: the callback,
`services.Configure<QuartzHealthCheckOptions>(...)` and a bound configuration section are equivalent, in any
order.

| Option | Meaning |
|---|---|
| `Name` | `quartz-scheduler`, or `quartz-scheduler-<name>` for a named scheduler |
| `Tags` | Tags for filtering into probes |
| `FailureStatus` | What the registration reports when the check fails |
| `StandbyStatus` | What a scheduler in standby reports; default *degraded* |
| `StaleFiringTolerance` | Overdue-trigger detection; default off (`null`); see [below](#saying-that-a-scheduler-has-stopped-firing) |

*Degraded* answers HTTP 200, and a worker project has no endpoint to remap it. Set
`StandbyStatus = HealthStatus.Unhealthy` if standby nodes must leave the rotation. It covers standby only: a
scheduler in `Created` because `AutoStart` is `false` still reports *degraded*.

A named scheduler has its own check. Register it on the health checks builder or inside `AddQuartz`:

<!-- snippet: sample_hosted_named_health_check -->
```csharp
builder.Services.AddHealthChecks().AddQuartz("reporting", options => options.Tags.Add("ready"));

// or, where the scheduler is configured
builder.Services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());
```
<!-- endSnippet -->

Configure its options under its name:

<!-- snippet: sample_hosted_named_health_check_options -->
```csharp
builder.Services.Configure<QuartzHealthCheckOptions>("reporting", options => options.Tags.Add("ready"));
```
<!-- endSnippet -->

Serving the report over HTTP (`MapHealthChecks`) and how *degraded* maps to a status code are in
[ASP.NET Core Integration](aspnet-core-integration.md#health-checks).

### Saying that a scheduler has stopped firing

The checks above test reachability, not progress. A scheduler with a wedged thread, a full thread pool or an
unreleased store lock still reports `Running`, answers store queries and checks in to its cluster, while firing
nothing. Set `StaleFiringTolerance` to detect this:

<!-- snippet: sample_hosted_health_check_stale_firing -->
```csharp
builder.Services.AddHealthChecks().AddQuartz(options =>
{
    // Degraded once a trigger is three misfire thresholds overdue, unhealthy at six.
    // Off (null) by default: what counts as overdue is the application's to say.
    options.StaleFiringTolerance = 3;
});
```
<!-- endSnippet -->

- The check looks for a schedulable trigger whose fire time is overdue by more than `StaleFiringTolerance`
  misfire thresholds: `AdoJobStoreOptions.MisfireThreshold` or `InMemoryJobStoreOptions.MisfireThreshold`, or
  one minute for a custom store that exposes neither.
- Overdue by that much: *degraded*. Overdue by twice that: *unhealthy*.
- The report data carries `overdueTrigger`, `overdueSince` and `overdueBy`.
- Start with `3`, as for `ClusterCheckinTolerance`. A trigger can be one threshold late before it counts as
  misfired, and one more sweep late before the misfire handler (whose default interval on the database store is
  the misfire threshold) reaches it, so the value must be more than one.
- Off by default: a scheduler working down a backlog after a maintenance window is behind but healthy.
- Not evaluated for a scheduler in standby; that verdict comes before the store is queried.
- A scheduler with all triggers paused has nothing schedulable, so it never counts as stalled.
- Left unset, the check costs what it did before.

## Shutdown has a budget

The host's `StopAsync` token fires after `HostOptions.ShutdownTimeout` (default thirty seconds) and bounds the
wait for running jobs.

- When it fires, schedulers stop *waiting*. They still shut down job stores, plugins and listeners, and
  listeners are still notified. A warning naming the scheduler is logged.
- The deadline does not cancel jobs. `QuartzSchedulerOptions.ShutdownJobInterruption` (default: never) decides
  whether a shutting-down scheduler asks them to stop; a job that must end on request watches
  `IJobExecutionContext.CancellationToken`.
- With `WaitForJobsToComplete = true` and jobs that outlive the budget, the host stops with those jobs running
  and their job store updates unfinished. Raise `HostOptions.ShutdownTimeout` if that matters more than a prompt
  stop.
- Several schedulers shut down in parallel and share one budget.
