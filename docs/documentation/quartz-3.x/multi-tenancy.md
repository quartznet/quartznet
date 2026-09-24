---
title: 'Multi-Tenancy'
---

# Multi-Tenancy

Quartz has no `Tenant` concept. You build one from a scheduler, a group or a `SCHED_NAME`. This page
covers the 3.x mechanics of each and what each isolates. To choose a model, read
[Tenancy Patterns](../tenancy-patterns.md) first.

## Choosing a model

| | Scheduler per tenant | Group per tenant | Database or prefix per tenant |
|---|---|---|---|
| **Isolation** | strongest: separate job store, thread pool, listeners, plugins, calendars | logical only: one scheduler, one pool | strongest at rest; one process still runs them all |
| **Tenants known at** | startup, or runtime outside the container | any time | startup |
| **Add a tenant at runtime** | yes, but you own its lifetime | yes | no |
| **Per-tenant concurrency limits** | yes, naturally | yes, via execution groups | yes |
| **Cost per tenant** | a scheduling loop, a connection pool, a thread pool | ~nothing | a schema |
| **Fits** | tens of tenants, strong isolation needs | hundreds or thousands of tenants | regulatory separation of data |

The models combine. A common SaaS shape: one scheduler with a group per tenant and one database,
plus a second scheduler for the few tenants that pay for isolation.

## Scheduler per tenant

`AddQuartz(name, …)` registers a named scheduler. The name becomes its
`quartz.scheduler.instanceName`, the name of its `QuartzOptions`, and, with a persistent store, its
`SCHED_NAME`.

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

One `AddQuartzHostedService` starts them all. It always registers a hosted service for the named
schedulers, and one for the default scheduler only if an unnamed `AddQuartz()` has already registered
`ISchedulerFactory`.

::: warning
For the *default* scheduler, call `AddQuartz()` **before** `AddQuartzHostedService()`. Order does not
matter for named schedulers.
:::

Setting `SchedulerName` inside a named `AddQuartz(name, …)` block throws: the name is the
registration key.

### Getting one back

3.x has no keyed `IScheduler`, and named schedulers are not in the container: the hosted service
creates them and binds them into the repository. Inject `Quartz.Spi.ISchedulerRepository`:

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
`ISchedulerFactory` exists only after an unnamed `AddQuartz()`, so with only named schedulers use
`ISchedulerRepository`. Named schedulers appear in it only once the hosted service has started them,
not during application startup.
:::

There are **two** repositories, and mixing them up is the most common 3.x multi-scheduler mistake:

| Repository | Who binds into it |
|---|---|
| `SchedulerRepository.Instance` (process-wide static) | `StdSchedulerFactory` and `DirectSchedulerFactory`, by default |
| the container's `ISchedulerRepository` singleton | the DI integration's named-scheduler factory |

A scheduler created through DI is **not** in `SchedulerRepository.Instance`. One created by a bare
`StdSchedulerFactory` is **not** in the container's repository, nor in the Dashboard's scheduler list.
In a DI application, inject `ISchedulerRepository`; never use the static.

### What is per scheduler

Each named scheduler has its own job store, thread pool, listeners, plugins, calendars, jobs and
triggers. Listeners and calendars registered in a named `AddQuartz(name, …)` block attach to that
scheduler only. `quartz.plugin.*` plugins are created per scheduler from its own properties, so
`q.UseXmlSchedulingConfiguration(...)`, `q.UseJobAutoInterrupt(...)` and the rest work in a named
block.

`QuartzOptions` are named options keyed by the scheduler name:
`builder.Services.Configure<QuartzOptions>("DurableScheduler", …)`. Unlike 4.x, 3.x has no
per-scheduler `ThreadPoolOptions` or `AdoJobStoreOptions`; use `quartz.*` properties on the named
scheduler's builder.

### What is not

- **`QuartzHostedServiceOptions` is global.** `WaitForJobsToComplete`, `StartDelay` and
  `AwaitApplicationStarted` apply to every scheduler; 3.x has no `AddQuartzHostedService(name, …)`
  overload.
- **Job types are shared.** See [per-tenant services inside a job](#per-tenant-services-inside-a-job).
- **`scheduler.Context["Quartz.ServiceProvider"]` is set only for the default scheduler.** A plugin
  that reaches the container through the scheduler context silently fails on a named one.
- **The health check covers the default scheduler only.** See
  [health checks](#health-checks-and-observability).

Name collisions:

- A named scheduler and a default scheduler renamed to the same string are not caught at
  registration. Host start fails with a `SchedulerException`:
  `Scheduler with name 'X' already exists.`
- Two named schedulers with the same name are caught at registration, but only on an exact match.
  Registration compares ordinally and the repository case-insensitively, so `AddQuartz("Acme")` beside
  `AddQuartz("acme")` passes registration and fails at start.

Derive tenant scheduler names from a single normalised source.

## Group per tenant

One scheduler; the tenant is the group of every key.

```csharp
JobKey job = new("nightly-report", tenantId);
TriggerKey trigger = new("nightly", tenantId);
```

Everything that takes a `GroupMatcher` is then tenant-scoped:

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

`GroupMatcher<TKey>` also has `GroupStartsWith`, `GroupEndsWith`, `GroupContains` and `AnyGroup`;
`AndMatcher` / `OrMatcher` / `NotMatcher` combine them.

::: warning
Pause tenants by **trigger** group. The ADO.NET job store does not persist paused *job* group state:
`IsJobGroupPaused` always returns `false` there, because `PauseJobs` pauses the triggers of the jobs
in the group without recording the group. `RAMJobStore` does track it. Write against the persistent
behaviour.
:::

Listeners take matchers too, so a per-tenant listener is one registration:

```csharp
q.AddJobListener<AuditListener>(GroupMatcher<JobKey>.GroupEquals(tenantId));
```

- Several matchers on one listener are OR-ed.
- `IListenerManager.AddJobListenerMatcher` / `RemoveJobListenerMatcher` / `SetJobListenerMatchers`
  change matchers at runtime.
- Scheduler listeners take no matcher, so a tenant-scoped `ISchedulerListener` is not possible.
- On 3.x `DeleteJobs`, `UnscheduleJobs` and `ScheduleJobs` take key collections, not matchers; hence
  the two-step offboarding above.
- `Clear()` takes no matcher: it empties the whole scheduler, never one tenant.
- Calendars are one flat namespace per scheduler, with no calendar group. Tenants must prefix the
  names themselves (`"acme:holidays"`).

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
On 3.x the execution group is a **separate tag on the trigger**, not derived from the trigger group.
Every trigger of a tenant must carry it:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly", tenantId)
    .ForJob(job)
    .WithExecutionGroup(tenantId)      // <- required; the key group is not consulted
    .WithCronSchedule("0 30 2 * * ?")
    .Build();
```

Quartz 4.x adds `UseTriggerGroupWhenUnset()`, which uses the trigger group as the execution group.
3.x has no equivalent: a trigger without the tag runs unlimited, or under whatever `ForOtherGroups`
allows.
:::

::: warning
`WithExecutionGroup` is on `TriggerBuilder`, **not** on the DI `ITriggerConfigurator`, so
`q.AddTrigger(t => t.WithExecutionGroup("acme"))` does not compile, nor does the
`q.ScheduleJob<T>(trigger => …)` form. Build such triggers with `TriggerBuilder` and pass them to
`scheduler.ScheduleJob(...)` at runtime, or declare them through
[JSON scheduling](packages/json-configuration.md), where `ExecutionGroup` is a trigger property.

`ITrigger` does not declare `ExecutionGroup` on 3.x, so
`(await scheduler.GetTrigger(key)).ExecutionGroup` does not compile either. Cast to
`Quartz.Impl.Triggers.AbstractTrigger`.
:::

Limits can be changed at runtime, per node:

```csharp
await scheduler.SetExecutionLimits(new ExecutionLimits()
    .ForGroup("acme", 8)
    .ForOtherGroups(1));
```

- `SetExecutionLimits` and `GetExecutionLimits` are **extension methods** on `IScheduler` and require
  a `StdScheduler`. On a `RemoteScheduler` or a custom `IScheduler` they throw `SchedulerException`.
- Limits take effect on the next acquisition cycle. Pass `null` to clear them.
- With an ADO.NET job store, a trigger's execution group is persisted only if `QRTZ_TRIGGERS` has an
  `EXECUTION_GROUP` column. The column is optional on 3.x: the store probes for it at startup and logs
  at Debug level when it is missing. Without it every trigger looks ungrouped after a restart. See
  [Execution Groups](tutorial/execution-groups.md) for the DDL.

::: warning
Execution limits are **per node**, in memory, and neither persisted nor coordinated. On a three-node
cluster, a limit of 8 for `acme` allows up to 24 concurrent Acme jobs. See [limits](#limits).
:::

### Pinning a tenant to hardware

[Node affinity](tutorial/node-affinity.md) is persisted, unlike execution limits.
`TriggerBuilder.WithPreferredNode(instanceId)` records which node should pick a trigger up, with
failover to another node if that one is down. Use it when tenants have bought dedicated capacity:
it sets which nodes are the tenant's, where execution limits set how much of a node it may use.

Node affinity needs a stable `quartz.scheduler.instanceId`. `AUTO` generates a new id on every
restart, and the store logs a warning when it detects one.

## Shared database

Every Quartz table has `SCHED_NAME` as the first primary-key column, and every statement filters on
it, so schedulers with different names share tables without seeing each other's rows. `SCHED_NAME` is
bound to `quartz.scheduler.instanceName`. Only the schema probes, which select no rows, omit it.

`quartz.jobStore.tablePrefix` (default `QRTZ_`) is set per scheduler, so tenants can also have
separate table *sets* in one database:

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

- **A different scheduler name is enough to isolate.** Use separate prefixes for backup, restore or
  permissions reasons.
- **The prefix must match the DDL.** Run the DDL with the prefix substituted; nothing derives one from
  the other.
- **A wrong prefix fails at startup.** `PerformSchemaValidation` is on by default, so a missing or
  mis-prefixed table is reported at startup, not at the first failing operation.

::: warning
Two schedulers sharing a database with the **same** `SCHED_NAME` look exactly like two nodes of one
cluster. Schema validation does not catch it, and they steal each other's triggers. The duplicate-name
check works only within one container; across processes, keeping names unique is up to you.

Tenants differ by `instanceName` (the *logical* scheduler); cluster members differ by `instanceId`
(a *node*). Swapping the two causes the failure above.
:::

With a persistent store, `[DisallowConcurrentExecution]` counts rows in `QRTZ_FIRED_TRIGGERS`
filtered by `SCHED_NAME`. It is cluster-wide but not cross-tenant: two tenants can each run the same
job type concurrently, as intended.

The Redis lock handler's keys include the scheduler name, so a shared Redis is safe across tenants
that differ by `SCHED_NAME`.

## Per-tenant services inside a job

`MicrosoftDependencyInjectionJobFactory` creates a DI scope for every job execution; on 3.x always,
since the older `CreateScope` and `AllowDefaultConstructor` options are obsolete and ignored. Set the
tenant on that scope before the job and its dependencies are constructed: subclass the factory and
override `protected virtual void ConfigureScope(IServiceScope, TriggerFiredBundle, IScheduler)`.

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
needs no subclass and whose callbacks combine. 3.x has no equivalent; subclass instead.
:::

- **The hook is synchronous**, so `AsyncLocal<T>` values it sets survive; an awaited hook would lose
  them when the `ExecutionContext` is restored. The scoped holder above also works, is easier to test
  and does not depend on execution context flow.
- **The job is created on the execution path**, not during initialization, so values set here flow
  into `Execute`.

Derive the tenant from the `TriggerFiredBundle`: `Trigger.Key.Group`, `JobDetail.Key.Group`, or a
value in `Trigger.JobDataMap`. Under scheduler-per-tenant, the `IScheduler` that fired it is the
tenant. Inside `Execute`, use `context.Trigger.Key.Group` or `context.MergedJobDataMap`.

Or read the tenant inside the job and resolve per-tenant services by key. Quartz 3.x makes no keyed
registrations of its own, but keyed lookups pass through the job's scope, so your own keyed services
resolve:

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

Keyed services need `Microsoft.Extensions.DependencyInjection` 8.0 or later, so an application on
`net8.0` or newer. Quartz 3.x also supports `netstandard2.0` and .NET Framework, where this is not
available.

::: warning
Registering a job type keyed does **not** work. The job factory resolves the job type unkeyed and
falls back to direct activation when nothing is registered, so a keyed job-type registration is
silently ignored. Key the job's *dependencies*, not the job.
:::

For more involved cases, such as resolving jobs from a tenant-owned container, implement
`Quartz.Spi.IJobFactory` (`IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)` and
`void ReturnJob(IJob job)`) and register it with `q.UseJobFactory<T>()`. On Quartz 4.x that interface
is in `Quartz.Extensibility` and returns a `ValueTask<JobScope>`, so a custom factory does not port
unchanged.

::: warning
On a **named** scheduler, `UseJobFactory<T>()` sets the factory by property and does not replace the
container's global singleton. The named scheduler's own factory constructs the type instead of
resolving it as a registered service, so register its constructor dependencies in the container.
:::

## Health checks and observability

**Health checks are not per tenant on 3.x.** `AddQuartzServer` registers one health check,
`quartz-scheduler`. It resolves the **default** scheduler from `ISchedulerFactory`, checks that it is
started and does one store round-trip.

- Named schedulers are not covered.
- The check type is internal, so you cannot register it again by hand.
- On `netstandard2.0` there is no health check.
- The check is always registered, but a container with **only** named schedulers has no
  `ISchedulerFactory`. Then register a default scheduler, or use `AddQuartzHostedService` and write
  your own check.

Tags are the only setting on the built-in check:

```csharp
services.AddQuartzServer(
    options => options.WaitForJobsToComplete = true,
    healthCheckTags: new[] { "ready" });
```

For per-tenant health, write an `IHealthCheck` that injects `ISchedulerRepository` and checks the
schedulers you care about.

::: tip Quartz 4.x
Quartz 4.x adds `AddQuartzHealthChecks` on a scheduler's own builder. The check name defaults to
`quartz-scheduler-<scheduler name>`, so several can be registered side by side.
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
and `trigger.group`. So make the group the raw tenant id, not a decorated string.

::: warning Cardinality
`job.name` and `trigger.name` are per job and per trigger. Combined with a tenant dimension, a backend
can end up with a series per tenant per trigger. Drop the name tags before they reach the backend
unless you need them.
:::

- **There are no metrics on 3.x.** Quartz 3.x publishes no `Meter`, counters or histograms; the 4.x
  `quartz.job.execution.*` instruments have no 3.x counterpart. Use traces and your own
  instrumentation.
- **Nothing puts the tenant into a logging scope.** To tag every log line a job writes with its
  tenant, open an `ILogger.BeginScope` yourself: in `Execute`, a job base class or a custom job
  factory. `q.UseStructuredJobLogging()` and `q.UseStructuredTriggerLogging()` log Quartz's own history
  entries with named parameters, but do not scope your job's logging.

## Limits

What multi-tenant deployments ask of Quartz and do not get:

- **Execution limits are per node, not cluster-wide.** Each group's running count is in memory on the
  scheduler thread. Dividing the cap by the node count is the closest approximation, and it is wrong
  whenever a node is down. For a cluster-aware control, use [node affinity](tutorial/node-affinity.md).
- **There is no rate limiting.** Execution limits cap *concurrency*, not throughput. "100 jobs an hour
  per tenant" cannot be expressed in Quartz; build it in the job or in what the job calls.
- **A starved group's triggers misfire; they do not queue.** At its limit, a group's triggers are
  skipped during acquisition and keep their original next fire time. If the starvation outlasts the
  misfire threshold, the trigger's misfire instruction decides whether the occurrence is skipped or
  rescheduled. Choose misfire instructions deliberately for triggers in limited groups.
- **Job types are shared across schedulers.** Job classes are resolved unkeyed from the one container,
  so two schedulers cannot register different implementations of one job type. Prefer one job type
  that reads its tenant from the firing; otherwise give each tenant its own job type.
- **Nothing stops a job reaching another tenant's data.** `IJobExecutionContext.Scheduler` gives the
  running job the whole scheduler, with `DeleteJob`, `PauseTriggers` and `Clear`. There is no
  group-scoped scheduler façade and no authorization hook. Group-per-tenant is a naming convention,
  not an enforcement boundary. If tenant code must not reach across, use a process boundary.
- **Dashboard authorization is per process, all or nothing.** The Dashboard has one
  `AuthorizationPolicy`, one `IDashboardAuthorizationFilter` and one `ReadOnly` flag. There is no
  per-scheduler policy or scheduler-name claim check, though its selector lists every scheduler in the
  repository. To keep tenants out of each other's schedulers, enforce it outside Quartz.
- **A shut-down scheduler cannot be restarted.** `Start()` then throws
  `The Scheduler cannot be restarted after Shutdown() has been called.`, and every other operation
  throws `The Scheduler has been Shutdown.`. Use `Standby()` / `Start()` to pause and resume. Shutdown
  removes the scheduler from the repository, so a later `StdSchedulerFactory.GetScheduler()` builds a
  new scheduler.
- **A per-tenant thread pool costs.** Under scheduler-per-tenant each tenant gets a scheduling loop
  that wakes on its own idle timer, a thread pool, and, with a persistent store, a connection pool and
  a cluster check-in. Fine for tens of tenants, not for thousands.

## Onboarding a tenant while the process runs

Under group-per-tenant, scheduling a job for a new group is an ordinary API call.

Under scheduler-per-tenant it is also possible, because 3.x does not bind scheduler construction to
the DI container. `AddQuartz` changes `IServiceCollection`, which is closed once the container is
built, but `StdSchedulerFactory` builds a scheduler from a `NameValueCollection` at any time:

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

What you take on:

- **The repository split.** Both routes bind into the static `SchedulerRepository.Instance`, not the
  container's `ISchedulerRepository`, so the new scheduler is missing from
  `ISchedulerRepository.LookupAll()` and the Dashboard. To fix it, subclass `StdSchedulerFactory` and
  override `protected virtual ISchedulerRepository GetSchedulerRepository()` to return the container's
  instance.
- **Its lifetime.** The hosted service enumerates named schedulers once at start; the host does not
  start, stop or shut down a scheduler created later. Register an
  `IHostApplicationLifetime.ApplicationStopping` callback, or an `IHostedService` of your own.
- **Job resolution.** A bare `StdSchedulerFactory` uses `PropertySettingJobFactory`, which activates
  job types directly and needs a public parameterless constructor. For container-resolved jobs, set
  `scheduler.JobFactory = new MicrosoftDependencyInjectionJobFactory(serviceProvider, options)` before
  starting it.

## See also

- [Tenancy Patterns](../tenancy-patterns.md): prior art, the deciding axes, anti-patterns
- [Multiple Schedulers](packages/multiple-schedulers.md): naming and keying schedulers
- [Execution Groups](tutorial/execution-groups.md): per-node thread limits
- [Node Affinity](tutorial/node-affinity.md): pinning triggers to nodes, persisted and cluster-aware
- [Clustering](tutorial/advanced-enterprise-features.md): what a shared database gives you
- [Microsoft DI Integration](packages/microsoft-di-integration.md): job factories and scopes
