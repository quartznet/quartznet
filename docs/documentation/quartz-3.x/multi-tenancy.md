---
title: 'Multi-Tenancy'
---

# Multi-Tenancy

Quartz has no `Tenant` concept. What it has are three separations you can build one out of — a
scheduler, a group, and a `SCHED_NAME` — and this page is about picking the right one and knowing
exactly what it does and does not isolate.

If you have not yet chosen a model, read [Tenancy Patterns](../tenancy-patterns.md) first: it surveys
how other schedulers partition tenants and names the axes that decide. This page is the 3.x
mechanics.

## Choosing a model

| | Scheduler per tenant | Group per tenant | Database or prefix per tenant |
|---|---|---|---|
| **Isolation** | strongest: separate job store, thread pool, listeners, plugins, calendars | logical only — one scheduler, one pool | strongest at rest; one process still runs them all |
| **Tenants known at** | startup, or runtime outside the container | any time | startup |
| **Add a tenant at runtime** | yes, but you own its lifetime | yes | no |
| **Per-tenant concurrency limits** | yes, naturally | yes, via execution groups | yes |
| **Cost per tenant** | a scheduling loop, a connection pool, a thread pool | ~nothing | a schema |
| **Fits** | tens of tenants, strong isolation needs | hundreds or thousands of tenants | regulatory separation of data |

They compose. The common shape for a SaaS with many small tenants is *one* scheduler, groups per
tenant, one database — and a second scheduler for the handful of tenants that bought isolation.

## Scheduler per tenant

`AddQuartz(name, …)` registers a named scheduler. The name becomes that scheduler's
`quartz.scheduler.instanceName`, the name of its `QuartzOptions`, and — with a persistent store — its
`SCHED_NAME`, so its configuration and its rows always agree.

```csharp
foreach (string tenant in tenants)
{
    builder.Services.AddQuartz(tenant, q =>
    {
        q.UsePersistentStore(s =>
        {
            s.UseSqlServer(sqlServer => sqlServer.ConnectionString = connectionStrings[tenant]);
            s.UseSystemTextJsonSerializer();
            s.UseClustering();
        });
        q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 5);
        q.ScheduleJob<NightlyReportJob>(trigger => trigger
            .WithIdentity("nightly", tenant)
            .WithCronSchedule("0 30 2 * * ?"));
    });
}

builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

One `AddQuartzHostedService` starts them all. Behind that single call sit up to two hosted services:
one that enumerates every named scheduler, always registered, and one for the default scheduler,
registered only when an unnamed `AddQuartz()` has already put `ISchedulerFactory` in the container.

::: warning
Order matters. `AddQuartz()` must be called **before** `AddQuartzHostedService()` for the *default*
scheduler, because the default hosted service is only registered when `ISchedulerFactory` is already
in the service collection. Named schedulers are unaffected by the ordering.
:::

Setting `SchedulerName` inside a named `AddQuartz(name, …)` block throws — the name is the
registration key and cannot drift from it.

### Getting one back

There is no keyed `IScheduler` on 3.x. Named schedulers are not registered in the container at all;
they are created by the hosted service and bound into the repository. Inject
`Quartz.Spi.ISchedulerRepository`:

```csharp
public class TenantOpsService
{
    private readonly ISchedulerRepository schedulerRepository;

    public TenantOpsService(ISchedulerRepository schedulerRepository)
    {
        this.schedulerRepository = schedulerRepository;
    }

    public async Task TriggerNightly(string tenant)
    {
        IScheduler? scheduler = schedulerRepository.Lookup(tenant);
        if (scheduler is not null)
        {
            await scheduler.TriggerJob(new JobKey("nightly", tenant));
        }
    }
}
```

::: warning
`ISchedulerFactory` is only registered when an unnamed `AddQuartz()` call has been made. In a
container holding only named schedulers there is no `ISchedulerFactory` to inject — use
`ISchedulerRepository`.

Named schedulers appear in the repository only once the hosted service has created and started them,
so they are not there during application startup.
:::

There are **two** repositories, and mixing them up is the most common 3.x multi-scheduler mistake.
`SchedulerRepository.Instance` is a process-wide static that `StdSchedulerFactory` and
`DirectSchedulerFactory` bind into by default. The DI integration registers its *own*
`ISchedulerRepository` singleton in the container, and the named-scheduler factory binds there
instead. A scheduler created through DI is therefore **not** in `SchedulerRepository.Instance`, and a
scheduler created by a bare `StdSchedulerFactory` is **not** in the container's repository — nor,
consequently, in the Dashboard's scheduler list. Always inject `ISchedulerRepository` in a DI
application; never reach for the static.

### What is per scheduler

Each named scheduler gets its own job store, thread pool, listeners, plugins, calendars, jobs and
triggers. Listeners and calendars registered inside a named `AddQuartz(name, …)` block are attached to
that scheduler only. Plugins configured by `quartz.plugin.*` properties are instantiated once per
scheduler, from that scheduler's own property bag, so `q.UseXmlSchedulingConfiguration(...)`,
`q.UseJobAutoInterrupt(...)` and the rest all work inside a named block.

`QuartzOptions` are *named options* whose name is the scheduler's name, which is what
`builder.Services.Configure<QuartzOptions>("DurableScheduler", …)` binds to. Note that unlike 4.x,
3.x does not have per-scheduler `ThreadPoolOptions` or `AdoJobStoreOptions` — a named scheduler's
components are configured through `quartz.*` properties on its own builder.

### What is not

- **`QuartzHostedServiceOptions` is global.** `WaitForJobsToComplete`, `StartDelay` and
  `AwaitApplicationStarted` apply to every scheduler uniformly; there is no
  `AddQuartzHostedService(name, …)` overload on 3.x.
- **Job types are shared.** See [per-tenant services inside a job](#per-tenant-services-inside-a-job).
- **`scheduler.Context["Quartz.ServiceProvider"]` is set only for the default scheduler.** A plugin
  that reaches for the container through the scheduler context works on the default scheduler and
  silently does not on a named one.
- **The health check covers the default scheduler only.** See
  [health checks](#health-checks-and-observability).

A named scheduler colliding with a default scheduler renamed to the same string is not caught at
registration; it surfaces at host start as a `SchedulerException` reading
`Scheduler with name 'X' already exists.` from the repository. Two named schedulers with the same name
*are* caught at registration — but only when the names match exactly. The registration check compares
ordinally while the repository compares case-insensitively, so `AddQuartz("Acme")` beside
`AddQuartz("acme")` passes registration and fails at start. Derive tenant scheduler names from a
single normalised source.

## Group per tenant

One scheduler; the tenant is the group half of every key.

```csharp
JobKey job = new("nightly-report", tenantId);
TriggerKey trigger = new("nightly", tenantId);
```

Everything that takes a `GroupMatcher` then becomes tenant-scoped:

```csharp
// everything this tenant has scheduled
IReadOnlyCollection<TriggerKey> theirs =
    await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(tenantId));

// suspend a tenant
await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(tenantId));

// is a tenant suspended?
IReadOnlyCollection<string> paused = await scheduler.GetPausedTriggerGroups();
bool suspended = paused.Contains(tenantId);

// or, directly
bool alsoSuspended = await scheduler.IsTriggerGroupPaused(tenantId);

// offboard a tenant
IReadOnlyCollection<JobKey> jobs =
    await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(tenantId));
await scheduler.DeleteJobs(jobs.ToList());
```

`GroupMatcher<TKey>` also offers `GroupStartsWith`, `GroupEndsWith`, `GroupContains` and `AnyGroup`,
and `AndMatcher` / `OrMatcher` / `NotMatcher` compose them.

::: warning
Pause tenants by **trigger** group. The ADO.NET job store does not persist paused *job* group state —
`IsJobGroupPaused` always returns `false` there, because `PauseJobs` pauses the individual triggers of
the jobs in the group without recording the group itself. `RAMJobStore` does track it, so the two
stores differ; write against the persistent behaviour.
:::

Listeners take matchers too, so a per-tenant listener is one registration:

```csharp
q.AddJobListener<AuditListener>(GroupMatcher<JobKey>.GroupEquals(tenantId));
```

Several matchers on one listener are OR-ed. Matchers can be added and removed at runtime through
`IListenerManager.AddJobListenerMatcher` / `RemoveJobListenerMatcher` / `SetJobListenerMatchers`.
Scheduler listeners are not matchable — a tenant-scoped `ISchedulerListener` is not expressible.

`DeleteJobs`, `UnscheduleJobs` and `ScheduleJobs` take key collections rather than matchers on 3.x,
which is why offboarding is the two-step above. `Clear()` takes no matcher at all: it empties the
whole scheduler, never one tenant.

Calendars are a flat namespace within a scheduler — there is no calendar group. Under group-per-tenant,
tenants share it and must namespace the names themselves (`"acme:holidays"`).

### Per-tenant concurrency quotas

Execution groups cap how many threads a category of work may use on a node. Give each tenant an
execution group and a limit:

```csharp
services.AddQuartz(q =>
{
    q.UseExecutionLimits(limits => limits
        .ForGroup("acme", maxConcurrent: 8)      // a big tenant
        .ForGroup("initech", maxConcurrent: 2)
        .ForOtherGroups(maxConcurrent: 1));      // everyone else gets one thread each
});
```

or as properties:

```text
quartz.executionLimit.acme = 8
quartz.executionLimit.initech = 2
quartz.executionLimit.* = 1
```

::: warning
On 3.x the execution group is a **separate tag on the trigger**; it is not derived from the trigger
group. Every trigger for a tenant must carry it explicitly:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly", tenantId)
    .ForJob(job)
    .WithExecutionGroup(tenantId)      // <- required; the key group is not consulted
    .WithCronSchedule("0 30 2 * * ?")
    .Build();
```

Quartz 4.x adds `UseTriggerGroupWhenUnset()`, which lets the trigger group stand in for the execution
group so that no trigger has to repeat it. There is no 3.x equivalent — forget the tag on one trigger
and that trigger runs unlimited, or falls into whatever `ForOtherGroups` allows.
:::

::: warning
`WithExecutionGroup` is on `TriggerBuilder`, **not** on the DI `ITriggerConfigurator`. So
`q.AddTrigger(t => t.WithExecutionGroup("acme"))` does not compile, and neither does the
`q.ScheduleJob<T>(trigger => …)` form. Triggers that need an execution group must be built with
`TriggerBuilder` and passed to `scheduler.ScheduleJob(...)` at runtime, or declared through
[JSON scheduling](packages/json-configuration.md), where `ExecutionGroup` is a recognised property on
a trigger definition.

Reading it back has the same asymmetry: `ITrigger` does not declare `ExecutionGroup` on 3.x, so
`(await scheduler.GetTrigger(key)).ExecutionGroup` does not compile either — cast to
`Quartz.Impl.Triggers.AbstractTrigger`.
:::

Limits can also be changed at runtime, per node:

```csharp
await scheduler.SetExecutionLimits(new ExecutionLimits()
    .ForGroup("acme", 8)
    .ForOtherGroups(1));
```

`SetExecutionLimits` and `GetExecutionLimits` are **extension methods** on `IScheduler`, not interface
members, and they require the scheduler to be a `StdScheduler`. Against a `RemoteScheduler` or a
custom `IScheduler` they throw a `SchedulerException`. Limits take effect on the next acquisition
cycle; pass `null` to clear them.

With an ADO.NET job store, persisting a trigger's execution group needs an `EXECUTION_GROUP` column on
`QRTZ_TRIGGERS`. The column is optional on 3.x — the store probes for it at startup and logs at Debug
level when it is missing — but without it nothing is persisted and every trigger looks ungrouped after
a restart. See [Execution Groups](tutorial/execution-groups.md) for the DDL.

::: warning
Execution limits are **per node**, held in memory, and nothing about them is persisted or coordinated.
On a three-node cluster, a limit of 8 for `acme` means up to 24 concurrent Acme jobs. See
[honest limits](#honest-limits).
:::

### Pinning a tenant to hardware

[Node affinity](tutorial/node-affinity.md) is the other per-tenant cluster control, and unlike
execution limits it *is* persisted. `TriggerBuilder.WithPreferredNode(instanceId)` records which node
should pick a trigger up, with failover to another node if the preferred one is down. Where execution
limits say "how much of this node may this tenant use", node affinity says "which nodes are this
tenant's" — which is usually the more useful question when tenants have bought dedicated capacity.

Node affinity needs a stable `quartz.scheduler.instanceId`. `AUTO` generates a fresh id on every
restart, and the store logs a warning when it detects one.

## Shared database

Every Quartz table has `SCHED_NAME` as the first column of its primary key, and every statement
filters on it. Two schedulers with different names therefore share tables without seeing each other's
rows, and that is a property of the schema rather than of the code paths — there is no query that
forgets. `SCHED_NAME` is bound to `quartz.scheduler.instanceName`; the only statements that do not
carry it are the schema probes, which select no rows.

Table prefix is the other axis. `quartz.jobStore.tablePrefix` (default `QRTZ_`) is a per-scheduler
setting, so two tenants can have entirely separate table *sets* in one database:

```csharp
builder.Services.AddQuartz("acme", q => q.UsePersistentStore(s =>
{
    s.UseSqlServer(sqlServer =>
    {
        sqlServer.ConnectionString = sharedConnectionString;
        sqlServer.TablePrefix = "ACME_QRTZ_";
    });
    s.UseSystemTextJsonSerializer();
}));
```

Three rules:

- **Different scheduler name is enough.** Prefixes are for keeping tenants in separate *tables*, which
  is a backup-and-restore or a permissions decision, not an isolation one.
- **The prefix has to match the DDL.** Nothing derives one from the other; you run the DDL with the
  prefix substituted.
- **A wrong prefix is caught at startup.** `PerformSchemaValidation` is on by default, so a missing or
  mis-prefixed table is reported at startup rather than surfacing as the first failing operation an
  hour later.

::: warning
Two schedulers sharing a database with the **same** `SCHED_NAME` are, by construction, indistinguishable
from two nodes of one cluster — because that is exactly what they look like to the schema. Schema
validation will not catch it, and they will steal each other's triggers. The duplicate-name check
protects you only within one container; across processes, the name is a contract you keep.

`instanceName` identifies the *logical* scheduler and is what tenants differ by. `instanceId`
identifies a *node* of one logical scheduler, and is what cluster members differ by. Getting these the
wrong way round is what produces the failure above.
:::

Also worth knowing: with a persistent store, `[DisallowConcurrentExecution]` is enforced cluster-wide
by counting rows in `QRTZ_FIRED_TRIGGERS` — but that count is filtered by `SCHED_NAME`, so it does not
reach across tenants. That is the intended behaviour, and it means two tenants can each run their own
copy of the same job type concurrently.

If you use the Redis lock handler, its keys already include the scheduler name, so a shared Redis is
safe across tenants that differ by `SCHED_NAME`.

## Per-tenant services inside a job

A job needs to reach *its* tenant's database, its tenant's configuration, its tenant's feature flags.
`MicrosoftDependencyInjectionJobFactory` creates a DI scope for every job execution — unconditionally
on 3.x, the older `CreateScope` and `AllowDefaultConstructor` options are obsolete and ignored — and
the scope is prepared before the job and everything it injects are constructed.

The hook is `protected virtual void ConfigureScope(IServiceScope, TriggerFiredBundle, IScheduler)`,
reached by subclassing:

```csharp
public sealed class TenantJobFactory : MicrosoftDependencyInjectionJobFactory
{
    public TenantJobFactory(IServiceProvider serviceProvider, IOptions<QuartzOptions> options)
        : base(serviceProvider, options)
    {
    }

    protected override void ConfigureScope(
        IServiceScope scope,
        TriggerFiredBundle bundle,
        IScheduler scheduler)
    {
        scope.ServiceProvider.GetRequiredService<TenantHolder>().TenantId = bundle.Trigger.Key.Group;
    }
}
```

```csharp
services.AddScoped<TenantHolder>();
services.AddQuartz(q => q.UseJobFactory<TenantJobFactory>());
```

::: tip Quartz 4.x
Quartz 4.x adds `q.ConfigureJobScope((scope, bundle, scheduler) => …)`, a delegate registration that
needs no subclass and whose callbacks combine. There is no 3.x equivalent; subclassing is the way.
:::

Two things make this work, and both are worth knowing:

- **The hook is synchronous.** An asynchronous hook would be awaited, and the `ExecutionContext`
  restored on the way back would discard exactly the `AsyncLocal<T>` values it exists to set. Both the
  scoped-holder pattern above and an `AsyncLocal<T>` work; the holder is easier to test and does not
  depend on execution context flow.
- **The job is created on the execution path**, not during initialization, so values set here flow into
  `Execute`.

The `TriggerFiredBundle` gives you the whole firing to derive the tenant from — `Trigger.Key.Group`,
`JobDetail.Key.Group`, or a value out of `Trigger.JobDataMap` — plus the `IScheduler` that fired it,
which is the tenant itself under the scheduler-per-tenant model. Inside `Execute`, the same identity is
available from `context.Trigger.Key.Group` or `context.MergedJobDataMap`.

Alternatively, read the tenant inside the job and resolve per-tenant services by key. Quartz 3.x makes
no keyed registrations of its own, but keyed lookups pass through the job's scope, so your own keyed
services resolve:

```csharp
public class ReportJob : IJob
{
    private readonly IServiceProvider services;

    public ReportJob(IServiceProvider services) => this.services = services;

    public async Task Execute(IJobExecutionContext context)
    {
        string tenant = context.Trigger.Key.Group;
        ITenantStore store = services.GetRequiredKeyedService<ITenantStore>(tenant);
        await store.WriteReport(context.CancellationToken);
    }
}
```

Keyed services require `Microsoft.Extensions.DependencyInjection` 8.0 or later, so this pattern needs
an application on `net8.0` or newer — Quartz 3.x itself still supports `netstandard2.0` and .NET
Framework, where it is unavailable.

::: warning
Registering a job type keyed does **not** work. The job factory resolves the job type unkeyed from the
scope and falls back to direct activation when nothing is registered, so a keyed job-type registration
is silently never consulted. Key the job's *dependencies*, not the job.
:::

For anything more involved — resolving jobs from a tenant-owned container, say — implement
`Quartz.Spi.IJobFactory` directly (`IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)` plus
`void ReturnJob(IJob job)`) and register it with `q.UseJobFactory<T>()`. On Quartz 4.x that interface
is in `Quartz.Extensibility` and returns a `ValueTask<JobScope>`, so a custom factory does not port
across unchanged.

::: warning
On a **named** scheduler, `UseJobFactory<T>()` configures the factory by property and deliberately
does not replace the container's global singleton, so the type is constructed by the named scheduler's
own factory rather than resolved as a registered service. Make sure its constructor dependencies are
registered in the container.
:::

## Health checks and observability

**Health checks are not per tenant on 3.x.** `AddQuartzServer` registers a single health check named
`quartz-scheduler`, which resolves the **default** scheduler from `ISchedulerFactory`, checks that it
is started and performs one store round-trip. Named schedulers are not covered, the check type is
internal so it cannot be registered again by hand, and on `netstandard2.0` there is no health check at
all.

Note the consequence for a scheduler-per-tenant container: the check is registered unconditionally,
but it resolves `ISchedulerFactory`, which a container holding **only** named schedulers never
registers. If you use `AddQuartzServer` without a default scheduler, either register a default one or
use `AddQuartzHostedService` and write your own check.

Tags are the only knob on the built-in one:

```csharp
services.AddQuartzServer(
    options => options.WaitForJobsToComplete = true,
    healthCheckTags: new[] { "ready" });
```

For per-tenant health, write your own `IHealthCheck` that injects `ISchedulerRepository` and walks the
schedulers you care about.

::: tip Quartz 4.x
Quartz 4.x adds `AddQuartzHealthChecks` on a scheduler's own builder, defaulting the check name to
`quartz-scheduler-<scheduler name>` so several can be registered side by side.
:::

**Traces** come from `DiagnosticListener` and `Activity`, under the listener name `Quartz`. The tag
names are public constants on `Quartz.Logging.DiagnosticHeaders`:

| Constant | Tag |
|---|---|
| `DiagnosticHeaders.SchedulerName` | `scheduler.name` |
| `DiagnosticHeaders.SchedulerId` | `scheduler.id` |
| `DiagnosticHeaders.FireInstanceId` | `fire.instance.id` |
| `DiagnosticHeaders.TriggerGroup` | `trigger.group` |
| `DiagnosticHeaders.TriggerName` | `trigger.name` |
| `DiagnosticHeaders.JobGroup` | `job.group` |
| `DiagnosticHeaders.JobName` | `job.name` |
| `DiagnosticHeaders.JobType` | `job.type` |

Under scheduler-per-tenant the tenant is `scheduler.name`; under group-per-tenant it is `job.group`
and `trigger.group`. That is a good reason to make the group the raw tenant id rather than a decorated
string.

::: warning Cardinality
`job.name` and `trigger.name` are per job and per trigger. Multiply that by a tenant dimension and a
backend can find itself with a series per tenant per trigger. Drop the name tags before they reach the
backend unless you know you need them.
:::

Two gaps to plan around:

- **There are no metrics on 3.x.** Quartz 3.x publishes no `Meter` and no counters or histograms; the
  4.x `quartz.job.execution.*` instruments have no 3.x counterpart. Traces, and your own
  instrumentation, are what you have.
- **Nothing puts the tenant into a logging scope.** If you want every log line a job writes to carry
  its tenant, open an `ILogger.BeginScope` yourself — in `Execute`, in a job base class, or in a custom
  job factory. `q.UseStructuredJobLogging()` and `q.UseStructuredTriggerLogging()` emit Quartz's own
  history entries with named parameters, which helps, but they do not scope your job's logging.

## Honest limits

Things multi-tenant deployments ask Quartz for and do not get.

**Execution limits are per node, not cluster-wide.** The running count for each execution group lives
in a dictionary in memory on the scheduler thread. Nothing is persisted and nodes do not coordinate. A
cluster-wide cap is not available; the closest approximation is dividing the cap by the node count,
which is wrong whenever a node is down. For a cluster-aware per-tenant control, use
[node affinity](tutorial/node-affinity.md) instead — it is persisted.

**There is no rate limiting.** Execution limits cap *concurrency*, not throughput. "This tenant may
run 100 jobs an hour" is not something Quartz can express; build it in the job, or in the thing the job
calls.

**A starved group's triggers misfire; they do not queue.** When a group is at its limit its triggers
are skipped during acquisition and left as they are, keeping their original next fire time. If the
starvation outlasts the misfire threshold, the ordinary misfire machinery claims them and the
trigger's misfire instruction decides whether the occurrence is skipped or rescheduled. Choose misfire
instructions deliberately for triggers in limited groups.

**Job types are shared across schedulers.** Job classes are resolved unkeyed from the one container, so
two schedulers cannot register different implementations for the same job type. Give each tenant its
own job type, or — far better — one job type that reads its tenant from the firing.

**Nothing stops a job reaching another tenant's data.** `IJobExecutionContext.Scheduler` hands the
running job the whole scheduler, with `DeleteJob`, `PauseTriggers` and `Clear` on it. There is no
group-scoped scheduler façade and no authorization hook. Group-per-tenant is a naming convention with
tooling support, not an enforcement boundary. If tenant code must not be able to reach across, that is
a process boundary, not a group.

**Dashboard authorization is per process, all or nothing.** The Dashboard has one
`AuthorizationPolicy`, one `IDashboardAuthorizationFilter` and one `ReadOnly` flag; there is no
per-scheduler policy and no scheduler-name claim check, even though its scheduler selector lists every
scheduler in the repository. If tenants must reach their own scheduler and not each other's, enforce
that outside Quartz.

**A shut-down scheduler cannot be restarted.** `Start()` on a shut-down scheduler throws
`The Scheduler cannot be restarted after Shutdown() has been called.`, and every other operation throws
`The Scheduler has been Shutdown.`. `Standby()` / `Start()` is the pause-and-resume pair. Shutdown does
remove the scheduler from the repository, so a subsequent `StdSchedulerFactory.GetScheduler()` builds a
fresh one rather than handing back the corpse — but it is a new scheduler, not a revived one.

**A per-tenant thread pool is a real cost.** Under the scheduler-per-tenant model each tenant gets a
scheduling loop that wakes on its own idle timer, a thread pool, and — with a persistent store — a
connection pool and a cluster check-in. That is fine for tens of tenants and not for thousands.

## Onboarding a tenant while the process runs

Under the group-per-tenant model this is a non-question: scheduling a job for a new group is an
ordinary API call.

Under the scheduler-per-tenant model it is still possible, because on 3.x scheduler construction is not
bound to the DI container. `AddQuartz` mutates `IServiceCollection`, which is closed once the container
is built — but `StdSchedulerFactory` will build a scheduler from a `NameValueCollection` at any time:

```csharp
NameValueCollection props = new()
{
    ["quartz.scheduler.instanceName"] = tenantId,
    ["quartz.threadPool.maxConcurrency"] = "5",
    ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
    ["quartz.jobStore.tablePrefix"] = "QRTZ_",
    ["quartz.jobStore.dataSource"] = "default",
    ["quartz.dataSource.default.connectionString"] = connectionStrings[tenantId],
    ["quartz.dataSource.default.provider"] = "SqlServer",
};

IScheduler scheduler = await new StdSchedulerFactory(props).GetScheduler();
await scheduler.Start();
```

`DirectSchedulerFactory.Instance.CreateScheduler(name, instanceId, threadPool, jobStore)` does the same
with hand-built parts and no property strings.

Three things you take on by doing this:

- **The repository split.** Both routes bind into the static `SchedulerRepository.Instance`, not the
  container's `ISchedulerRepository` — so the new scheduler will not appear in
  `ISchedulerRepository.LookupAll()` or in the Dashboard. To join them, subclass `StdSchedulerFactory`
  and override `protected virtual ISchedulerRepository GetSchedulerRepository()` to return the
  container's instance.
- **Its lifetime is yours.** The hosted service enumerates named schedulers once at start; a scheduler
  created afterwards is not started, stopped or shut down by the host. Register an
  `IHostApplicationLifetime.ApplicationStopping` callback, or an `IHostedService` of your own.
- **Job resolution.** A bare `StdSchedulerFactory` uses `PropertySettingJobFactory`, which activates
  job types directly and needs a public parameterless constructor. For container-resolved jobs, set
  `scheduler.JobFactory = new MicrosoftDependencyInjectionJobFactory(serviceProvider, options)` before
  starting it.

## See also

- [Tenancy Patterns](../tenancy-patterns.md) — prior art, the deciding axes, and the anti-patterns
- [Multiple Schedulers](packages/multiple-schedulers.md) — the mechanics of naming and keying schedulers
- [Execution Groups](tutorial/execution-groups.md) — per-node thread limits in full
- [Node Affinity](tutorial/node-affinity.md) — pinning triggers to nodes, persisted and cluster-aware
- [Clustering](tutorial/advanced-enterprise-features.md) — what a shared database gives you
- [Microsoft DI Integration](packages/microsoft-di-integration.md) — job factories and scopes
