---

title: Hosted Services Integration
---

[Quartz](https://www.nuget.org/packages/Quartz)
provides integration with [hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzHostedService` on the host
application builder, or on `IServiceCollection`. This will add a hosted Quartz server into process that
will be started and stopped based on applications lifetime.

::: tip
See [Quartz documentation](microsoft-di-integration) to learn more about configuring Quartz scheduler, jobs and triggers.

Need multiple independent schedulers in one application? See [Multiple Schedulers](multiple-schedulers.md).
:::

The hosted service starts every scheduler in the container, and resolves them when the host starts —
so `AddQuartz` and `AddQuartzHostedService` can be called in either order. The options apply to every
scheduler; one that has to differ is configured by name with
`AddQuartzHostedService("SchedulerName", options => …)`.

::: warning
Calling `AddQuartzHostedService()` without registering any scheduler throws at startup: the hosted
service was asked for, so something was meant to run. Register a scheduler with `AddQuartz(...)`.
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
| `WaitForJobsToComplete` | `false` | Shutdown does not return until the jobs still executing have finished. Without it the host stops while they are still running. Whether they are also asked to stop is the scheduler's `ShutdownJobInterruption` setting. |
| `AwaitApplicationStarted` | `true` | Jobs do not start until application startup has completed, so nothing fires while the rest of the application is still coming up. |
| `StartDelay` | none | Starts the scheduler this long after it otherwise would. With `AwaitApplicationStarted`, the delay is counted from the completion of startup. |
| `AutoStart` | `true` | Whether the hosted service starts the scheduler at all. `false` has it built, initialized and bound, but left for the application to start — see [A scheduler the application starts itself](#a-scheduler-the-application-starts-itself). |

To take part in the lifecycle itself — a warm-up before the scheduler starts, a drain after it stops — derive
from `QuartzHostedService` and register the subclass; its `StartingAsync`, `StartedAsync`, `StoppingAsync` and
`StoppedAsync` are virtual, and `Schedulers` gives it the schedulers it is running:

<!-- snippet: sample_hosted_derived_service -->
```csharp
builder.AddQuartzHostedService<WarmUpBeforeSchedulingService>(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

`builder.AddQuartz(...)` is `builder.Services.AddQuartz(...)` with the application's configuration
already found: it reads the `Quartz` section, so anything described in `appsettings.json` is applied
before your callback. The `IServiceCollection` overloads are unchanged, and are what to use for a
configuration section under a different name:

<!-- snippet: sample_hosted_configuration_section -->
```csharp
builder.Services.AddQuartz(builder.Configuration.GetSection("Scheduling"), q => { });
```
<!-- endSnippet -->

A string names a scheduler, here as everywhere else in Quartz — `builder.AddQuartz("reporting", …)`
registers a scheduler called `reporting`, reading its settings from `Quartz:Schedulers:reporting` when
the section describes several. `builder.AddQuartzSchedulers()` registers one per child of that
sub-section.

## A scheduler the application starts itself

A library that owns its own leader election, a message bus that has to be connected before anything may
fire, a module that comes up after the rest of the application — each wants the container to build and
bind its scheduler, and wants to press start itself. `AutoStart = false` says so:

<!-- snippet: sample_hosted_deferred_start -->
```csharp
builder.AddQuartz("reporting", q => { });

// Built, initialized and bound with the host, but left in Created for the application to start
builder.AddQuartzHostedService("reporting", options => options.AutoStart = false);
```
<!-- endSnippet -->

The scheduler is still resolved, initialized and bound when the host starts, so `ISchedulerRegistry`, the
dashboard and `GET /schedulers` all see it; it simply sits in `Created` until something calls
`scheduler.Start()`. Not registering the hosted service at all would have produced the same non-start
and lost the shutdown handling with it, which is the reason this is a setting rather than an omission.

`AutoStart` wins over `AwaitApplicationStarted` and `StartDelay`. Both of those say *when* the hosted
service starts a scheduler; it does not start this one at all, so neither applies.

Shutdown is unchanged. The hosted service shuts down every scheduler it created, started or not, so
opting out of the start is not opting out of the stop.

It is a per-scheduler setting like the rest, so one scheduler can be deferred while its siblings start
with the host — the example above defers `reporting` and leaves every other scheduler in the container
alone. A library embedding Quartz in someone else's application is the case this exists for.

## Health checks

The scheduler's health check ships in the core `Quartz` package and registers on the standard
`IHealthChecksBuilder`, so a worker with no web stack at all can carry it. It reports *healthy* while
the scheduler is running and can reach its store, *degraded* while it is in standby or waiting for the
application to start it, and *unhealthy* otherwise. Add it alongside an application's other checks:

<!-- snippet: sample_hosted_health_check -->
```csharp
builder.Services.AddHealthChecks().AddQuartz();
```
<!-- endSnippet -->

`services.AddQuartzHealthChecks()` is the same thing for an application that has no other checks to
compose with.

A scheduler whose `AutoStart` is `false` is *degraded* while it sits in `Created`, not *unhealthy*: it is
doing what it was configured to do, and failing the probe would take a correctly configured node out of
rotation for the whole window before the application presses start. The check reads that scheduler's own
`QuartzHostedServiceOptions`, so a `Created` scheduler that nothing opted out of is *unhealthy* as
before — including one in an application with no hosted service registered at all, where nothing is
going to start it.

The registration can be customized via the optional configuration callback, for example to attach tags
so the check can be filtered into separate liveness and readiness probes:

<!-- snippet: sample_hosted_health_check_options -->
```csharp
builder.Services.AddHealthChecks().AddQuartz(options =>
{
    options.Name = "quartz-scheduler";   // the default, or quartz-scheduler-<name> for a named scheduler
    options.Tags.AddRange(["ready", "live"]);
    options.FailureStatus = HealthStatus.Unhealthy;
});
```
<!-- endSnippet -->

The callback is one source of `QuartzHealthCheckOptions` among several: the settings go through the
options pipeline, so `services.Configure<QuartzHealthCheckOptions>(...)` and a bound configuration
section mean the same thing, whichever order they are written in.

A named scheduler has a check of its own, reporting on *its* scheduler. Name it on the health checks
builder, or ask for one from inside `AddQuartz`:

<!-- snippet: sample_hosted_named_health_check -->
```csharp
builder.Services.AddHealthChecks().AddQuartz("reporting", options => options.Tags.Add("ready"));

// or, where the scheduler is configured
builder.Services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());
```
<!-- endSnippet -->

Its options are that scheduler's, so they are configured under its name:

<!-- snippet: sample_hosted_named_health_check_options -->
```csharp
builder.Services.Configure<QuartzHealthCheckOptions>("reporting", options => options.Tags.Add("ready"));
```
<!-- endSnippet -->

Serving the report over HTTP is `MapHealthChecks`, which is ASP.NET Core's —
[ASP.NET Core Integration](aspnet-core-integration.md#health-checks) has that half, including what
becomes of *degraded* at an HTTP probe.

## Shutdown has a budget

The host gives `StopAsync` a token that fires after `HostOptions.ShutdownTimeout` — thirty seconds by
default — and that token bounds the wait for running jobs. When it fires the schedulers stop *waiting*:
they still shut their job stores, plugins and listeners down, and their listeners are still told they
stopped, so nothing is left half torn down. A warning naming the scheduler is logged when it happens.

The jobs themselves are not cancelled by the deadline. Whether a shutting-down scheduler asks them to stop
is `QuartzSchedulerOptions.ShutdownJobInterruption`, which defaults to never, and a job that has to end on
request watches `IJobExecutionContext.CancellationToken`. So with `WaitForJobsToComplete = true` and jobs
that outlive the budget, the host stops with those jobs still running and their job store updates
unfinished — configure `HostOptions.ShutdownTimeout` upwards if that matters more than a prompt stop.

Several registered schedulers are shut down at the same time rather than one after another, so the budget
covers all of them together instead of being divided between them.
