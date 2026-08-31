---

title: Observability
---

# Observability

Quartz publishes traces and metrics through `System.Diagnostics` — an `ActivitySource` and a `Meter`, both
named `Quartz` — so nothing has to be installed to make a scheduler observable. What is installed is
whatever collects them.

The two names are public constants, so they can be subscribed to without typing a string twice:

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

`QuartzInstrumentation` is in the `Quartz.Diagnostics` namespace, and both constants are `"Quartz"`, so an
existing `AddSource("Quartz")` keeps working.

::: warning Upgrading from 3.x
Every instrument and every attribute was renamed in 4.0, and two of 3.x's four instruments are gone — while
five new ones cover misfires, acquisition, cluster check-in and recovery, and store round trips. Dashboards
and alerts written against the old names do not survive the upgrade — the migration guide has the
[complete old → new table](../migration-guide.md#old-and-new-telemetry-names).
:::

## Traces

Three kinds of span: the execution, the veto, and one per job store round trip.

| Span | Kind | When |
|---|---|---|
| `Quartz.Job.Execute` | `Internal` | A job runs. The span covers the whole fire, and records the exception when one is thrown. |
| `Quartz.Job.Veto` | `Internal` | A trigger listener vetoed the fire, so the job did not run. |
| `Quartz.JobStore.<operation>` | `Client` | One per store operation. The twenty-nine names are the members of `Quartz.Diagnostics.OperationName.JobStore`. |

The thirty-three store operations are the ones that change something or hand work to the scheduler:

`AcquireNextTriggers`, `TriggersFired`, `TriggeredJobComplete`, `ReleaseAcquiredTrigger`, `ScheduleJob`,
`ScheduleJobs`, `AddJob`, `AddTrigger`, `AddCalendar`, `DeleteJob`, `DeleteJobs`, `DeleteTrigger`,
`DeleteTriggers`, `DeleteCalendar`, `ReplaceTrigger`, `UpdateTriggerDetails`, `PauseTrigger`,
`PauseTriggers`, `PauseTriggerGroups`, `PauseJob`, `PauseJobs`, `PauseJobGroups`, `ResumeTrigger`,
`ResumeTriggers`, `ResumeTriggerGroups`, `ResumeJob`, `ResumeJobs`, `ResumeJobGroups`,
`PauseAll`, `ResumeAll`, `ResetTriggerFromErrorState`, `ResetTriggersFromErrorState`, `Clear`.

Pausing by key and pausing by group matcher are separate spans, because they are separate operations:
one moves the triggers it was given, the other records that a group is paused and catches what is
added to it afterwards.

Reads — `GetJob`, `Exists`, the `Query*` members — are deliberately not spans. A dashboard listing
triggers would otherwise produce a span per page.

::: tip Every store, not just the database one
Store tracing is a decorator over `IJobStore`, applied to whatever store the scheduler was built with.
The in-memory store, a community package's store and a store you wrote yourself all emit these spans;
before 4.0.0 they came from inside the ADO.NET store and nothing else produced any.
:::

The span names are constants, on `Quartz.Diagnostics.OperationName`. Attributes are namespaced `quartz.*`,
and are constants on `Quartz.Diagnostics.ActivityTags`:

| Attribute | On |
|---|---|
| `quartz.scheduler.name`, `quartz.scheduler.id` | every span |
| `quartz.job.name`, `quartz.job.group`, `quartz.job.type` | job spans |
| `quartz.trigger.name`, `quartz.trigger.group` | job spans; store spans about one trigger |
| `quartz.execution.group` | job spans, when the trigger names an execution group |
| `quartz.fire.instance.id` | job spans — the id of this one firing, which is also what `IScheduler.InterruptFireInstance` takes |
| `quartz.job.name`, `quartz.job.group` | store spans about one job |
| `quartz.jobstore.batch.size` | `Quartz.JobStore.AcquireNextTriggers` — how many triggers the scheduler asked for |
| `quartz.jobstore.trigger.count` | `Quartz.JobStore.AcquireNextTriggers` (how many came back) and `.TriggersFired` (how many were fired) |
| `error.type` | any span that ended in a failure |

### Linking a firing to what scheduled it

A job runs minutes, hours or days after the call that scheduled it, quite possibly on another node. When
that call was made inside an `Activity`, the scheduler records its W3C trace context on the trigger — under
the reserved keys `SchedulerConstants.TraceParent` and `SchedulerConstants.TraceState` — and the firing's
`Quartz.Job.Execute` span carries an `ActivityLink` back to it. Nothing needs configuring, and an HTTP API
request gets it without asking, because the endpoint runs inside ASP.NET Core's server span.

It is a **link** rather than a parent on purpose: the firing is its own trace root, so a trace never has to
stay open across the wait. That is the shape OpenTelemetry gives an asynchronous producer and the consumer
that eventually picks the work up, and every backend that renders links will walk from the firing back to
the request that asked for it.

The cost is two string entries on each trigger's data map, visible wherever trigger data is —
`MergedJobDataMap`, the dashboard, `GET /triggers`. Turn it off with:

```csharp
q.ConfigureScheduler(options => options.PropagateTraceContext = false);
```

::: tip The trigger's map, never the job's
`[PersistJobDataAfterExecution]` writes back only the job's map, so the two never interact — a persisted
job cannot carry a `traceparent` forward into its next firing.
:::

## Metrics

Eight instruments, all on the `Quartz` meter. **Every measurement carries `quartz.scheduler.name` and
`quartz.scheduler.id`** — the name says which scheduler, the id says which node of it, and a cluster is
several nodes sharing one name.

| Instrument | Type | Unit | Extra attributes | What it measures |
|---|---|---|---|---|
| `quartz.job.execution.duration` | `Histogram<double>` | `s` | `quartz.trigger.group`, `quartz.trigger.name`, `quartz.job.group`, `quartz.job.name`, `quartz.execution.group`¹, `error.type`² | How long a job took. Its **count** is the number of executions. |
| `quartz.job.execution.active` | `UpDownCounter<long>` | `{job}` | the same identity attributes, `quartz.execution.group`¹ | How many jobs are running right now. |
| `quartz.trigger.misfire` | `Counter<long>` | `{trigger}` | `quartz.trigger.group`, `quartz.execution.group`¹ | Firings that were owed and did not happen on time. |
| `quartz.trigger.acquisition.duration` | `Histogram<double>` | `s` | — | How long the scheduling loop waited on its store for the next batch. |
| `quartz.trigger.acquired` | `Counter<long>` | `{trigger}` | — | How many triggers those rounds came back with. |
| `quartz.cluster.checkin.duration` | `Histogram<double>` | `s` | `error.type`² | How long a cluster check-in took. Recorded per attempt, so a retried one is two measurements. |
| `quartz.cluster.recovery.trigger` | `Counter<long>` | `{trigger}` | `quartz.cluster.recovered.instance.id` | Fired-trigger rows recovered from a node that failed. |
| `quartz.jobstore.operation.duration` | `Histogram<double>` | `s` | `quartz.jobstore.operation`, `error.type`² | Every round trip to the store, named by the operation. |

¹ Only when the trigger names an execution group. A trigger in no group carries no such attribute rather
than an empty one, so the two are not folded into one series.
² Only when the operation failed. The value is the fully-qualified name of the exception type.

`quartz.jobstore.operation`'s value is one of the twenty-nine `Quartz.JobStore.*` names above, so the same
string finds a slow operation in a trace and in a metric. Its histogram's count is how many of each
operation there were, and the `error.type`-tagged part of that count is how many failed.

The two cluster instruments come from the ADO.NET store, which is the only clustered one. The other five
are store-agnostic.

### Reading the numbers

A histogram carries its own count, which is why there is no execution counter and no error counter: the
number of executions is `quartz.job.execution.duration`'s count, and the number of failures is the part of
that count tagged with `error.type` — which also says *which* exception, something a plain error counter
never could.

`error.type` is the OpenTelemetry convention rather than a Quartz name. It is deliberately not on
`quartz.job.execution.active`: an up-down counter's increment and decrement must carry identical
attributes, and whether a job will fail is not known when it starts.

::: warning Cardinality
`quartz.job.name` and `quartz.trigger.name` are per job and per trigger, and `quartz.scheduler.id` is per
node. A backend can find itself with a series per node per trigger. Drop the name attributes in a view
before they reach the backend unless you know you need them; the group attributes are usually the ones
worth keeping.
:::

The meter is created from the container's `IMeterFactory` when there is one — which `AddMetrics()`, and
therefore every application built on the generic host, registers. That is what lets two schedulers, or two
hosts in one test process, keep their measurements apart.

## OpenTelemetry.Instrumentation.Quartz

[OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz)
is the OpenTelemetry community's Quartz instrumentation library, and it was written for 3.x.

::: danger It produces nothing against 4.0, and does not say so
`AddQuartzInstrumentation()` yields **zero spans** on Quartz 4.x. Nothing throws, nothing warns, and the
call still compiles — an upgraded application simply stops seeing its job spans.
:::

The reason is that the two versions publish through different `System.Diagnostics` mechanisms. 3.x wrote
to a `DiagnosticListener` named `Quartz`, creating each `Activity` with `new Activity(...)` and no
`ActivitySource` behind it. The package subscribes to exactly that: a `DiagnosticSourceSubscriber` filtered
to the listener named `Quartz`, plus `AddLegacySource("Quartz.Job.Execute")` and
`AddLegacySource("Quartz.Job.Veto")` — and "legacy source" in the OpenTelemetry SDK means precisely an
activity that has *no* `ActivitySource`.

4.x emits from an `ActivitySource` named `Quartz`. Its activities are therefore not legacy activities, and
nothing writes to a `DiagnosticListener` at all, so both halves of the subscription match nothing.

The 4.0 way is the two lines at the top of this page — `AddSource(QuartzInstrumentation.ActivitySourceName)`
and `AddMeter(QuartzInstrumentation.MeterName)`. There is no package to install:

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

What is lost with the package is its `QuartzInstrumentationOptions.TracedOperations` filter. Subscribing
directly records both `Quartz.Job.Execute` and `Quartz.Job.Veto`; drop one with an OpenTelemetry
[processor or a sampler](https://opentelemetry.io/docs/languages/dotnet/) if a vetoed fire is not worth a
span to you. What is gained is everything 4.0 added — the store spans and all eight metrics — none of
which the package knows about.

## Older packages

`Quartz.OpenTelemetry.Instrumentation` is obsolete and is not part of 4.x. Subscribe to the activity source
directly, as at the top of this page.

### Coming from Quartz.OpenTracing

`Quartz.OpenTracing` is not part of 4.x either. It was built on the `DiagnosticSource` events that 4.x
replaced with `System.Diagnostics.Activity`, and there is no 4.x release of it — the OpenTracing project
itself is archived. Replace an `AddQuartzOpenTracing` call with the OpenTelemetry setup at the top of this
page.

## Logging

Quartz logs through `Microsoft.Extensions.Logging` and uses whatever the application has configured; there is
nothing to wire up. The types no container builds — a listener or trigger you constructed, the static
helpers, the jobs in `Quartz.Jobs` — are pointed at a logger factory with
`Quartz.Diagnostics.LogProvider.SetLogProvider(loggerFactory)`.

For a history of every job and trigger event as log entries, rather than as traces, the
[history plugins](quartz-plugins.md) write one.
