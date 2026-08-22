---

title: Observability
---

# Observability

Quartz publishes traces and metrics through `System.Diagnostics` — an `ActivitySource` and a `Meter`, both
named `Quartz` — so nothing has to be installed to make a scheduler observable. What is installed is
whatever collects them.

The two names are public constants, so they can be subscribed to without typing a string twice:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(QuartzInstrumentation.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(QuartzInstrumentation.MeterName)
        .AddOtlpExporter());
```

`QuartzInstrumentation` is in the `Quartz.Diagnostics` namespace, and both constants are `"Quartz"`, so an
existing `AddSource("Quartz")` keeps working.

::: warning Upgrading from 3.x
Every instrument and every attribute was renamed in 4.0, and two of the four instruments are gone. Dashboards
and alerts written against the old names do not survive the upgrade — the migration guide has the
[complete old → new table](../migration-guide.md#old-and-new-telemetry-names).
:::

## Traces

One activity per job execution, plus one per job store operation:

| Span | When |
|---|---|
| `Quartz.Job.Execute` | A job runs. The span covers the whole fire, and records the exception when one is thrown. |
| `Quartz.Job.Veto` | A trigger listener vetoed the fire, so the job did not run. |
| `Quartz.JobStore.*` | One per store operation — `Quartz.JobStore.AcquireNextTriggers`, `.TriggersFired`, `.ScheduleJob`, `.PauseTrigger` and the rest. |

The span names are constants too, on `Quartz.Diagnostics.OperationName`.

Attributes are namespaced `quartz.*`, and are constants on `Quartz.Diagnostics.ActivityTags`:

| Attribute | On |
|---|---|
| `quartz.scheduler.name`, `quartz.scheduler.id` | every span |
| `quartz.job.name`, `quartz.job.group`, `quartz.job.type` | job spans |
| `quartz.trigger.name`, `quartz.trigger.group` | job spans |
| `quartz.fire.instance.id` | job spans — the id of this one firing, which is also what `IScheduler.InterruptFireInstance` takes |
| `quartz.jobstore.trigger.count`, `quartz.jobstore.batch.size` | job store spans |

## Metrics

| Instrument | Type | Unit | Attributes |
|---|---|---|---|
| `quartz.job.execution.duration` | `Histogram<double>` | `s` | the five identity attributes, plus `error.type` when the execution failed |
| `quartz.job.execution.active` | `UpDownCounter<long>` | `{job}` | the five identity attributes |

Two instruments answer more than the four they replaced. A histogram carries its own count, so the number of
executions is `quartz.job.execution.duration`'s count and the number of failures is the part of that count
tagged with `error.type` — which also says *which* exception, something a plain error counter never could.

`error.type` is the OpenTelemetry convention rather than a Quartz name, and its value is the exception type's
name. It is deliberately not on `quartz.job.execution.active`: an up-down counter's increment and decrement
must carry identical attributes, and whether a job will fail is not known when it starts.

The meter is created from the container's `IMeterFactory` when there is one — which `AddMetrics()`, and
therefore every application built on the generic host, registers. That is what lets two schedulers, or two
hosts in one test process, keep their measurements apart.

## OpenTelemetry.Instrumentation.Quartz

[OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz)
is the OpenTelemetry community's Quartz instrumentation library. It subscribes to the same activity source
and adds filtering — which operations to record — over doing it yourself:

```shell
dotnet add package OpenTelemetry.Instrumentation.Quartz
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddQuartzInstrumentation());
```

## Older packages

`Quartz.OpenTelemetry.Instrumentation` is obsolete and is not part of 4.x. Use the community package above.

### Coming from Quartz.OpenTracing

`Quartz.OpenTracing` is not part of 4.x either. It was built on the `DiagnosticSource` events that 4.x
replaced with `System.Diagnostics.Activity`, and there is no 4.x release of it — the OpenTracing project
itself is archived. Replace an `AddQuartzOpenTracing` call with the OpenTelemetry setup at the top of this
page.

## Logging

Quartz logs through `Microsoft.Extensions.Logging` and uses whatever the application has configured; there is
nothing to wire up. Code that reaches Quartz from outside a container can point it at a logger factory with
`Quartz.Diagnostics.LogProvider.SetLogProvider(loggerFactory)`.

For a history of every job and trigger event as log entries, rather than as traces, the
[history plugins](quartz-plugins.md) write one.
