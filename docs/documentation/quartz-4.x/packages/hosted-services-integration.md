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
