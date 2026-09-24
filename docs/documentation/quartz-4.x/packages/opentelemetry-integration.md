---

title: Observability
---

# Observability

Quartz publishes traces and metrics through `System.Diagnostics`: an `ActivitySource` and a `Meter`, both named
`Quartz`. No Quartz package is needed; install only the collector. Subscribe with the public constants:

<!-- snippet: sample_opentelemetry_subscribe -->
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(QuartzInstrumentation.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(QuartzInstrumentation.MeterName)
        .AddOtlpExporter());
```
<!-- endSnippet -->

`QuartzInstrumentation` is in the `Quartz.Diagnostics` namespace. Both constants are `"Quartz"`, so an existing
`AddSource("Quartz")` keeps working.

::: warning Upgrading from 3.x
4.0 renamed every instrument and attribute, removed two of 3.x's four instruments, and added five for misfires,
acquisition, cluster check-in and recovery, and store round trips. Dashboards and alerts on the old names break;
see the [complete old → new table](../migration-guide.md#old-and-new-telemetry-names).
:::

## Traces

| Span | Kind | When |
|---|---|---|
| `Quartz.Job.Execute` | `Internal` | A job runs; covers the whole fire and records a thrown exception |
| `Quartz.Job.Veto` | `Internal` | A trigger listener vetoed the fire; the job did not run |
| `Quartz.JobStore.<operation>` | `Client` | One per store operation; names are the members of `Quartz.Diagnostics.OperationName.JobStore` |

The thirty-three store operations are those that change something or hand work to the scheduler:

`AcquireNextTriggers`, `TriggersFired`, `TriggeredJobComplete`, `ReleaseAcquiredTrigger`, `ScheduleJob`,
`ScheduleJobs`, `AddJob`, `AddTrigger`, `AddCalendar`, `DeleteJob`, `DeleteJobs`, `DeleteTrigger`,
`DeleteTriggers`, `DeleteCalendar`, `ReplaceTrigger`, `UpdateTriggerDetails`, `PauseTrigger`,
`PauseTriggers`, `PauseTriggerGroups`, `PauseJob`, `PauseJobs`, `PauseJobGroups`, `ResumeTrigger`,
`ResumeTriggers`, `ResumeTriggerGroups`, `ResumeJob`, `ResumeJobs`, `ResumeJobGroups`,
`PauseAll`, `ResumeAll`, `ResetTriggerFromErrorState`, `ResetTriggersFromErrorState`, `Clear`.

- Pausing by key and pausing by group matcher are separate spans. The first moves the given triggers; the second
  records that a group is paused and also catches triggers added to it later.
- Reads (`GetJob`, `Exists`, the `Query*` members) are not spans, so a dashboard listing triggers does not
  produce a span per page.

### How the spans are shaped into traces

- **A firing is its own trace.** `Quartz.Job.Execute` and `Quartz.Job.Veto` are always trace roots and never
  take the ambient `Activity` as parent. What the job traces (an `HttpClient` call, an EF Core query, your own
  spans) are children of the firing.
- **A store span belongs to the caller.** `scheduler.ScheduleJob(…)` inside an HTTP request puts
  `Quartz.JobStore.ScheduleJob` in that request's trace.
- **The scheduler loop's store calls are roots.** `AcquireNextTriggers`, `TriggersFired` and
  `TriggeredJobComplete` belong to no request, and the loop runs for the life of the process.

::: warning Fixed in 4.2.0
Before 4.2.0, each span the scheduler's loop opened became the parent of the next, and each firing hung off
whichever span was current at dispatch. A scheduler produced one trace that grew for the life of the process: a
day of a quiet staging pod was one tree of several thousand spans, with the jobs' `HttpClient` and EF Core spans
buried in it ([#3797](https://github.com/quartznet/quartznet/issues/3797)). Upgrading fixes it; no configuration
change is needed.
:::

::: tip Every store, not just the database one
Store tracing is a decorator over `IJobStore`, applied to any store: in-memory, a community package's, or your
own. Before 4.0.0 only the ADO.NET store emitted store spans.
:::

Span names are constants on `Quartz.Diagnostics.OperationName`. Attributes are namespaced `quartz.*` and are
constants on `Quartz.Diagnostics.ActivityTags`:

| Attribute | On |
|---|---|
| `quartz.scheduler.name`, `quartz.scheduler.id` | every span |
| `quartz.job.name`, `quartz.job.group`, `quartz.job.type` | job spans |
| `quartz.trigger.name`, `quartz.trigger.group` | job spans; store spans about one trigger |
| `quartz.execution.group` | job spans, when the trigger names an execution group |
| `quartz.fire.instance.id` | job spans |
| `quartz.job.name`, `quartz.job.group` | store spans about one job |
| `quartz.jobstore.batch.size` | `Quartz.JobStore.AcquireNextTriggers`: triggers requested |
| `quartz.jobstore.trigger.count` | `Quartz.JobStore.AcquireNextTriggers` (returned) and `.TriggersFired` (fired) |
| `error.type` | any span that ended in a failure |

`quartz.fire.instance.id` identifies one firing; it is the id `IScheduler.InterruptFireInstance` takes.

### Linking a firing to what scheduled it

When the scheduling call runs inside an `Activity`, the scheduler stores its W3C trace context on the trigger,
under the reserved keys `SchedulerConstants.TraceParent` and `SchedulerConstants.TraceState`. The firing's
`Quartz.Job.Execute` span, and a `Quartz.Job.Veto` span, carry an `ActivityLink` back to it.

- The firing may run much later and on another node. It is linked, not parented, so no trace stays open across
  the wait. This is OpenTelemetry's shape for an asynchronous producer and consumer.
- Nothing needs configuring. An HTTP API request gets it automatically, because the endpoint runs inside ASP.NET
  Core's server span.
- Cost: two string entries on each trigger's data map, visible in `MergedJobDataMap`, the dashboard and
  `GET /triggers`.

Turn it off with:

```csharp
q.ConfigureScheduler(options => options.PropagateTraceContext = false);
```

::: tip The trigger's map, never the job's
`[PersistJobDataAfterExecution]` writes back only the job's map, so a persisted job cannot carry a `traceparent`
into its next firing.
:::

## Metrics

Eleven instruments, all on the `Quartz` meter. **Every measurement carries `quartz.scheduler.name` and
`quartz.scheduler.id`**: the name identifies the scheduler, the id its node in a cluster.

Each instrument name is a `const string` on `QuartzInstrumentation.Instruments`; for example
`QuartzInstrumentation.Instruments.JobExecutionDuration` is `quartz.job.execution.duration`. Tests hold the
constants equal to what the meter emits and snapshot the name, kind, unit and description of every instrument.

| Instrument | Type | Unit | Extra attributes | Measures |
|---|---|---|---|---|
| `quartz.job.execution.duration` | `Histogram<double>` | `s` | `quartz.trigger.group`, `quartz.trigger.name`, `quartz.job.group`, `quartz.job.name`, `quartz.execution.group`¹, `error.type`² | Job duration; **count** = executions |
| `quartz.job.execution.active` | `UpDownCounter<long>` | `{job}` | the same identity attributes, `quartz.execution.group`¹ | Jobs running now |
| `quartz.trigger.misfire` | `Counter<long>` | `{trigger}` | `quartz.trigger.group`, `quartz.execution.group`¹ | Firings not made on time |
| `quartz.trigger.retry` | `Counter<long>` | `{trigger}` | `quartz.trigger.group`, `quartz.execution.group`¹ | Retries scheduled after a job failed |
| `quartz.trigger.retries_exhausted` | `Counter<long>` | `{trigger}` | `quartz.trigger.group`, `quartz.execution.group`¹ | Failed occurrences whose retry policy ran out |
| `quartz.trigger.acquisition.duration` | `Histogram<double>` | `s` | — | Loop's wait on the store for the next batch |
| `quartz.trigger.acquired` | `Counter<long>` | `{trigger}` | — | Triggers returned by those rounds |
| `quartz.cluster.checkin.duration` | `Histogram<double>` | `s` | `error.type`² | Cluster check-in duration, per attempt |
| `quartz.cluster.recovery.trigger` | `Counter<long>` | `{trigger}` | `quartz.cluster.recovered.instance.id` | Fired-trigger rows recovered from a failed node |
| `quartz.jobstore.operation.duration` | `Histogram<double>` | `s` | `quartz.jobstore.operation`, `error.type`² | Every store round trip, by operation |
| `quartz.jobstore.lock.wait.duration` | `Histogram<double>` | `s` | `quartz.jobstore.lock`, `error.type`² | One attempt to take a job store lock |

¹ Only when the trigger names an execution group. A trigger in no group has no such attribute (not an empty
one), so the two are separate series.
² Only when the operation failed. The value is the exception type's fully-qualified name.

- `quartz.trigger.retry` counts retries scheduled, not attempts configured: an unused policy adds nothing.
- `quartz.trigger.retries_exhausted` counts once per occurrence that gave up, never per attempt. It has the same
  attributes as `quartz.trigger.retry`, so the two divide.
- `quartz.cluster.checkin.duration` records each attempt: a retried check-in is two measurements.
- `quartz.jobstore.lock.wait.duration` does not record a re-entrant acquisition, which waited for nothing.
- `quartz.jobstore.operation` is one of the thirty-three `Quartz.JobStore.*` names above, so one string finds
  a slow operation in traces and metrics. The histogram count is the number of each operation; the
  `error.type`-tagged part is the failures.
- `quartz.jobstore.lock` is the `LOCK_NAME` value: `TRIGGER_ACCESS` (taken by every scheduling operation) or
  `STATE_ACCESS` (taken by cluster check-in).

The lock-wait histogram is the one instrument that reports *during* a stall. A lock statement blocked behind
another session returns and throws nothing, so no operation or failure is recorded. The matching warning is
event 3716 in [Log Events](../log-events.md); the case is
[A Lock Held by a Connection That Is Gone](../../troubleshooting.md#a-lock-held-by-a-connection-that-is-gone).

The two cluster instruments and the lock-wait histogram come from the ADO.NET store, the only clustered one. The
other eight work with any store.

### Reading the numbers

- There is no execution counter and no error counter. Executions are the count of
  `quartz.job.execution.duration`; failures are the part tagged with `error.type`, which also names the
  exception.
- `error.type` is the OpenTelemetry convention. It is not on `quartz.job.execution.active`: an up-down
  counter's increment and decrement need identical attributes, and failure is not known at start.

::: warning Cardinality
`quartz.job.name` and `quartz.trigger.name` are per job and per trigger, and `quartz.scheduler.id` is per node,
so a backend can get a series per node per trigger. Unless you need them, drop the name attributes in a view;
the group attributes are usually the ones to keep.
:::

The meter is created from the container's `IMeterFactory` when there is one. `AddMetrics()`, and so every
generic-host application, registers it. This keeps measurements of two schedulers, or two hosts in one test
process, apart.

## OpenTelemetry.Instrumentation.Quartz

[OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz) is
the OpenTelemetry community's instrumentation library, written for 3.x.

::: danger It produces nothing against 4.0, and does not say so
`AddQuartzInstrumentation()` yields **zero spans** on Quartz 4.x. Nothing throws or warns and the call still
compiles; an upgraded application silently loses its job spans.
:::

| | 3.x | 4.x |
|---|---|---|
| Publishes through | a `DiagnosticListener` named `Quartz`, `new Activity(...)` with no `ActivitySource` | an `ActivitySource` named `Quartz` |
| The package subscribes with | `DiagnosticSourceSubscriber` for that listener, plus `AddLegacySource("Quartz.Job.Execute")` and `AddLegacySource("Quartz.Job.Veto")` | matches nothing |

In the OpenTelemetry SDK a "legacy source" is an activity with *no* `ActivitySource`. 4.x activities have one,
and 4.x writes nothing to a `DiagnosticListener`, so neither half of the subscription matches.

Replace it with the two calls at the top of this page, `AddSource(QuartzInstrumentation.ActivitySourceName)` and
`AddMeter(QuartzInstrumentation.MeterName)`, and remove the package:

<!-- Not a compiled sample: the first block references a package this repository does not take, and
     taking a NuGet dependency purely to compile a documentation sample is not worth it. -->

```diff
- builder.Services.AddOpenTelemetry()
-     .WithTracing(tracing => tracing.AddQuartzInstrumentation());
+ builder.Services.AddOpenTelemetry()
+     .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
+     .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
```

```diff
- <PackageReference Include="OpenTelemetry.Instrumentation.Quartz" Version="1.*" />
```

- Lost: the package's `QuartzInstrumentationOptions.TracedOperations` filter. Subscribing directly records both
  `Quartz.Job.Execute` and `Quartz.Job.Veto`; drop one with an OpenTelemetry
  [processor or sampler](https://opentelemetry.io/docs/languages/dotnet/).
- Gained: everything 4.0 added, including the store spans and all eleven instruments.

## Older packages

`Quartz.OpenTelemetry.Instrumentation` is obsolete and not part of 4.x. Subscribe to the activity source
directly, as at the top of this page.

### Coming from Quartz.OpenTracing

`Quartz.OpenTracing` is not part of 4.x and has no 4.x release; the OpenTracing project is archived. It used the
`DiagnosticSource` events that 4.x replaced with `System.Diagnostics.Activity`. Replace an
`AddQuartzOpenTracing` call with the OpenTelemetry setup at the top of this page.

## Logging

Quartz logs through `Microsoft.Extensions.Logging`, using the application's configuration; nothing to wire up.
Types no container builds (a listener or trigger you constructed, the static helpers, the jobs in `Quartz.Jobs`)
use the factory set with `Quartz.Diagnostics.LogProvider.SetLogProvider(loggerFactory)`.

For job and trigger history as log entries rather than traces, use the [history plugins](quartz-plugins.md).
