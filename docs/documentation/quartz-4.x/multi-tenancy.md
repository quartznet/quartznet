---
title: 'Multi-Tenancy'
---

# Multi-Tenancy

Quartz has no `Tenant` concept, and it is not going to get one. What it has instead are three
separations you can build one out of — a scheduler, a group, and a `SCHED_NAME` — and this page is
about picking the right one and knowing exactly what it does and does not isolate.

If you have not yet chosen a model, read [Tenancy Patterns](../tenancy-patterns.md) first: it surveys
how other schedulers partition tenants and names the axes that decide. This page is the 4.x
mechanics.

## Choosing a model

| | Scheduler per tenant | Group per tenant | Database or prefix per tenant |
|---|---|---|---|
| **Isolation** | strongest: separate job store, thread pool, clock, listeners | logical only — one scheduler, one pool | strongest at rest; one process still runs them all |
| **Tenants known at** | startup | any time | startup |
| **Add a tenant at runtime** | no (needs a new container) | yes | no |
| **Per-tenant concurrency limits** | yes, naturally | yes, via execution groups | yes |
| **Cost per tenant** | a scheduling loop, a connection pool, a thread pool | ~nothing | a schema |
| **Fits** | tens of tenants, strong isolation needs | hundreds or thousands of tenants | regulatory separation of data |

They compose. The common shape for a SaaS with many small tenants is *one* scheduler, groups per
tenant, one database — and a second scheduler for the handful of tenants that bought isolation.

The question that usually decides it: **can a tenant appear while the process is running?** If yes, the
scheduler-per-tenant model is out, because a scheduler cannot be added to a container that has already
been built.

## Scheduler per tenant

`AddQuartz(name, …)` registers a named scheduler. The name is its instance name, the key its components
are registered under, and the name of its options — so its registrations and its configuration always
agree.

```csharp
foreach (string tenant in tenants)
{
    builder.Services.AddQuartz(tenant, q =>
    {
        q.UsePersistentStore(s =>
        {
            s.UseSqlServer(connectionStrings[tenant]);
            s.UseClustering();
        });
        q.UseDefaultThreadPool(maxConcurrency: 5);
        q.AddJob<NightlyReportJob>(j => j.WithIdentity("nightly"));
        q.AddTrigger<NightlyReportJob>(t => t.WithCronSchedule("0 30 2 * * ?"));
    });
}

builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
```

One `AddQuartzHostedService` starts them all. Calling the named overload —
`AddQuartzHostedService(tenant, o => …)` — configures *that* scheduler's start options and still
registers only one hosted service; two would each start every scheduler in the container.

### Injecting one

A named scheduler is keyed by its name:

```csharp
public sealed class TenantOpsService([FromKeyedServices("acme")] IScheduler scheduler);
```

```csharp
IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>(tenant);
```

::: warning
The unkeyed `IScheduler` is **the default scheduler** — the one registered by `AddQuartz(q => …)` with
no name. In a container holding only named schedulers there is no unkeyed registration at all, and
`GetRequiredService<IScheduler>()` throws. Resolve by key, or register a default scheduler as well.
:::

Trying to give a named scheduler and the default scheduler the same name is caught at registration:
`AddQuartz(o => o.InstanceName = "acme")` beside `AddQuartz("acme", …)` fails with a message naming both
calls, rather than as a duplicate-name `ArgumentException` from somewhere inside host start. Names are
compared case-insensitively.

### What is per scheduler

Almost everything. Each named scheduler gets its own keyed registration of the job factory, the
signaler, the thread pool, the job store, the driver delegate, the object serializer, the instance-id
generator, the scheduler itself and its factory — plus its own listeners, plugins, calendars, jobs and
triggers.

Options are **named options** whose name is the scheduler's name, and the container rewrites
`IOptions<T>` for a scheduler's own components so that `.Value` means *that* scheduler's settings.
`QuartzSchedulerOptions`, `ThreadPoolOptions`, `InMemoryJobStoreOptions`, `AdoJobStoreOptions`,
`ClusteringOptions`, `QuartzOptions` and `JobFactoryOptions` all work this way, and
`ConfigureOptions<TOptions>()` opts your own options type in.

A clock is per scheduler too, when you set one:

```csharp
builder.Services.AddQuartz("acme", q => q.UseTimeProvider(acmeClock));
```

A scheduler with no clock of its own inherits the container's, which is what lets an application-wide
`TimeProvider` reach all of them without being told about each.

### What is not

A handful of things are container-wide, shared by every scheduler in the process:

| | |
|---|---|
| `ITypeLoader` | type loading is a container-wide concern; `UseTypeLoader<T>()` **replaces** it for everyone |
| `ISchedulerRepository` | one per container — that is what makes `GetAllSchedulers` and the dashboard see all of them |
| `SystemTextJsonSerializerRegistry` | the HTTP API, the dashboard and the HTTP client serialize triggers without knowing which scheduler they came from |
| `Meters` | built from the container's `IMeterFactory` |
| `DataSourceOptions` | named after the **data source**, not the scheduler, so several schedulers can read through the same one |
| `QuartzHttpApiOptions` | one per process — see [honest limits](#honest-limits) |

And one that is not a container service at all: **`LogProvider`** is process-wide static state.
`SetLogProvider(loggerFactory)` sets it for everything in the process, deliberately.

### Health checks per tenant

`AddQuartzHealthChecks` called on a *scheduler's* builder checks that scheduler, and defaults its name
to `quartz-scheduler-<scheduler name>` so several can be registered side by side:

```csharp
builder.Services.AddQuartz("acme", q => q.AddQuartzHealthChecks(o => o.Tags.Add("tenant:acme")));
```

Called on `IServiceCollection` instead, it checks the default scheduler only.

## Group per tenant

One scheduler; the tenant is the group half of every key.

```csharp
JobKey job = new("nightly-report", tenantId);
TriggerKey trigger = new("nightly", tenantId);
```

Everything that takes a matcher then becomes tenant-scoped:

```csharp
// everything this tenant has scheduled
PagedResult<TriggerHeader> theirs = await scheduler.QueryTriggers(new TriggerQuery
{
    Group = GroupMatcher<TriggerKey>.GroupEquals(tenantId),
    Take = 100,
    IncludeTotalCount = true,
});

// suspend a tenant
List<string> paused = await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(tenantId));

// is a tenant suspended?
PagedResult<TriggerGroup> group = await scheduler.QueryTriggerGroups(
    new TriggerGroupQuery { Name = tenantId, Take = 1 });
bool suspended = group.Items is [{ Paused: true }];
```

Pause state is real and queryable for **trigger** groups. Job group pause state is not persisted by the
ADO store, which reports every job group as unpaused — pause tenants by trigger group.

Listeners take matchers too, so a per-tenant listener is one registration:

```csharp
q.AddJobListener<AuditListener>(Matchers.Group<JobKey>(StringOperator.Equality, tenantId));
```

### Per-tenant concurrency quotas

Execution groups cap how many threads a category of work may use on a node. When the schedule already
partitions work by trigger group — a tenant per group — the trigger group can stand in for the
execution group, so a quota is one line per tenant and no change to any trigger:

```csharp
q.UseExecutionLimits(limits => limits
    .UseTriggerGroupWhenUnset()
    .ForGroup("acme", 8)          // a big tenant
    .ForGroup("initech", 2)
    .ForOtherGroups(1));          // everyone else gets one thread each
```

`UseTriggerGroupWhenUnset()` changes nothing about the data — the trigger still carries no execution
group, and the store still persists none. It changes only how a limit is looked up. Three consequences:

- An explicit `ExecutionGroup` on a trigger always wins.
- `ForDefaultGroup` stops catching anything, because with this on nothing is ungrouped.
  Unlisted tenants fall under `ForOtherGroups`.
- Each unlisted group gets **its own** allowance from `ForOtherGroups`, not a shared one. Three unlisted
  tenants under `ForOtherGroups(1)` can run three jobs, one each.

`Unlimited(group)` is not the same as leaving a group out: an unlisted group falls back to
`ForOtherGroups`, an explicitly unlimited one does not.

Limits can also be changed at runtime, per node, with `SetExecutionLimits` / `GetExecutionLimits` — they
take effect on the next acquisition cycle, and `null` clears them.

::: warning
Execution limits are **per node**, held in memory, and nothing about them is persisted. On a
three-node cluster, `ForGroup("acme", 8)` means up to 24 concurrent Acme jobs. See
[honest limits](#honest-limits).
:::

## Shared database

Every Quartz table has `SCHED_NAME` as the first column of its primary key, and every statement filters
on it. Two schedulers with different names therefore share tables without seeing each other's rows, and
that is a property of the schema rather than of the code paths — there is no query that forgets.

Table prefix is the other axis. `AdoJobStoreOptions.TablePrefix` (default `QRTZ_`) is a per-scheduler
option, so two tenants can have entirely separate table *sets* in one database:

```csharp
builder.Services.AddQuartz("acme", q => q.UsePersistentStore(s =>
{
    s.UseSqlServer(sharedConnectionString);
    s.Configure(o => o.TablePrefix = "ACME_QRTZ_");
}));
```

Three rules:

- **Different scheduler name is enough.** Prefixes are for keeping tenants in separate *tables*, which
  is a backup-and-restore or a permissions decision, not an isolation one.
- **The prefix has to match the DDL.** Nothing derives one from the other; you run the DDL with the
  prefix substituted.
- **A wrong prefix is caught at startup.** `PerformSchemaValidation` is on by default, and a missing or
  mis-prefixed table is reported once, by name, with a message telling you to run the schema scripts —
  rather than surfacing as the first failing operation an hour later.

::: warning
Two schedulers sharing a database with the **same** `SCHED_NAME` are, by construction, indistinguishable
from two nodes of one cluster — because that is exactly what they look like to the schema. Schema
validation will not catch it, and they will steal each other's triggers. The duplicate-name check
protects you only within one container; across processes, the name is a contract you keep.
:::

## Per-tenant services inside a job

A job needs to reach *its* tenant's database, its tenant's configuration, its tenant's feature flags.
The scheduler builds jobs from a DI scope, and `ConfigureJobScope` prepares that scope before the job
and everything it injects are constructed:

```csharp
public static class TenantContext
{
    private static readonly AsyncLocal<string?> current = new();

    public static string? Current
    {
        get => current.Value;
        internal set => current.Value = value;
    }
}
```

```csharp
q.ConfigureJobScope((scope, bundle, scheduler) =>
{
    TenantContext.Current = bundle.Trigger.Key.Group;
});
```

Two things make this work, and both are deliberate:

- **The hook is synchronous.** An asynchronous hook would be awaited, and the `ExecutionContext`
  restored on the way back would discard exactly the `AsyncLocal<T>` values it exists to set.
- **The job is created on the execution path**, not during initialization, so values set here flow into
  `Execute`.

Callbacks combine rather than replace, and run in the order they were added.

The `TriggerFiredBundle` gives you the whole firing to derive the tenant from — `Trigger.Key.Group`,
`JobDetail.Key.Group`, or a value out of `Trigger.JobDataMap` — plus the `IScheduler` that fired it,
which is the tenant itself under the scheduler-per-tenant model.

::: tip
There is no built-in scoped `IJobExecutionContext` or accessor: the context does not exist yet when the
hook runs. The two patterns are the `AsyncLocal` above, and resolving a scoped holder object from
`scope.ServiceProvider` and populating it. The second is easier to test and does not depend on execution
context flow.
:::

For anything more involved — resolving jobs from a tenant-owned container, say — implement `IJobFactory`
and register it with `UseJobFactory<T>()`, or derive from `MicrosoftDependencyInjectionJobFactory` and
override `protected virtual void ConfigureScope(...)`. An override that does not call `base` takes the
delegate's place.

Per-fire options are a snapshot: read what the tenant's configuration says *inside* the hook or the job,
not once at startup, if tenants can be reconfigured while the process runs.

## Honest limits

Things multi-tenant deployments ask Quartz for and do not get:

**Execution limits are per node, not cluster-wide.** They live in a field on the in-process scheduler
and count against an in-memory dictionary of what is running here. Nothing is persisted and nodes do not
coordinate. A cluster-wide cap is not available; the closest approximation is dividing the cap by the
node count, which is wrong whenever a node is down.

**There is no rate limiting.** Execution limits cap *concurrency*, not throughput. "This tenant may run
100 jobs an hour" is not something Quartz can express; build it in the job, or in the thing the job
calls.

**HTTP API and dashboard authorization is per process, all or nothing.** One `MapQuartzHttpApi` serves
every scheduler in the container, with the scheduler named in the route
(`{apiPath}/schedulers/{schedulerName}/…`), and `RequireAuthorization(...)` applies uniformly to all of
it. The dashboard has a single `AuthorizationPolicy` and a single `ReadOnly` flag. There is no
per-scheduler policy and no scheduler-name claim check. If tenants must reach their own scheduler and
not each other's, enforce that **outside** Quartz — a process per tenant, or a proxy or middleware that
authorizes on the `{schedulerName}` route segment.

**Tenants cannot be onboarded at runtime *through the DI path*.** Schedulers are registered against
`IServiceCollection`, which is closed once the container is built, and the hosted service enumerates
them once at start. Nor can a scheduler be restarted after `Shutdown()`: the container owns its parts'
lifetimes, and `GetScheduler()` throws rather than resurrecting a thread pool and a job store
underneath a scheduler that can never run again. `Standby()` / `Start()` is the pause-and-resume pair.

That is a limit of the DI path, not of the library. `QuartzSchedulerBuilder` builds a scheduler from a
container of its own, at any point in the process's life, and `ISchedulerRepository.Bind` makes the
result visible to `GetAllSchedulers`, the dashboard and the HTTP API:

```csharp
IScheduler tenant = await QuartzSchedulerBuilder.Create()
    .ConfigureScheduler(o => o.InstanceName = tenantId)
    .UsePersistentStore(s => s.UseSqlServer(connectionStrings[tenantId]))
    .BuildScheduler();

await tenant.Start();
app.Services.GetRequiredService<ISchedulerRepository>().Bind(tenant);
```

What you take on by doing this: the returned `StandaloneSchedulerFactory` owns the container, so *you*
start the scheduler and dispose the factory — the hosted service will not; the scheduler's jobs resolve
from its own container rather than the application's unless you give it an `IJobFactory` that bridges;
and health checks registered at startup do not cover it. `Bind` throws on a duplicate name, and
offboarding is `Remove` plus disposing the factory.

Weigh that against the group-per-tenant model, where onboarding is a `ScheduleJob` call and none of
the above applies.

**A per-tenant thread pool is a real cost.** Under the scheduler-per-tenant model each tenant gets a
scheduling loop that wakes on its own idle timer, a thread pool, and — with a persistent store — a
connection pool and a cluster check-in. That is fine for tens of tenants and not for thousands.

## Observability

Both signals carry the scheduler name, so per-tenant dashboards work under the scheduler-per-tenant
model with no extra instrumentation:

- **Traces.** `quartz.scheduler.name` and `quartz.scheduler.id` are on every execution span, along with
  `quartz.job.group`, `quartz.job.name`, `quartz.trigger.group`, `quartz.trigger.name` and
  `quartz.fire.instance.id`.
- **Metrics.** `quartz.job.execution.active` and `quartz.job.execution.duration` both carry
  `quartz.scheduler.name`, `quartz.trigger.group`, `quartz.trigger.name`, `quartz.job.group` and
  `quartz.job.name`. (The scheduler *id* is on spans only.)

Under the group-per-tenant model, `quartz.trigger.group` and `quartz.job.group` **are** the tenant, so
the same dashboards work by grouping on those instead. That is a good reason to make the group the raw
tenant id rather than a decorated string.

::: warning Cardinality
`quartz.job.name` and `quartz.trigger.name` are per job and per trigger. Multiply that by a tenant
dimension and a metrics backend can find itself with a series per tenant per trigger. Drop the name tags
in a view before they reach the backend unless you know you need them.
:::

The tag names are public constants — `ActivityTags.SchedulerName`, `ActivityTags.TriggerGroup` and the
rest — so a view or a filter can reference them rather than repeat the strings.

## See also

- [Multiple Schedulers](packages/multiple-schedulers.md) — the mechanics of naming and keying schedulers
- [Execution Groups](tutorial/execution-groups.md) — per-node thread limits in full
- [Querying Jobs and Triggers](tutorial/querying-jobs-and-triggers.md) — group-filtered listings
- [Clustering](tutorial/advanced-enterprise-features.md) — what a shared database gives you
- [Migration Guide](migration-guide.md) — including why a shut-down scheduler cannot be restarted
