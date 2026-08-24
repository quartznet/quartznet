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

<!-- snippet: sample_tenancy_scheduler_per_tenant -->
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
<!-- endSnippet -->

One `AddQuartzHostedService` starts them all. Calling the named overload —
`AddQuartzHostedService(tenant, o => …)` — configures *that* scheduler's start options and still
registers only one hosted service; two would each start every scheduler in the container.

### Starting and stopping all of them

The hosted service builds every scheduler in the container while the host starts, and then starts
them: immediately under `AwaitApplicationStarted = false`, and otherwise — which is the default —
once `ApplicationStarted` fires. So a tenant's scheduler exists before the application runs and is
firing shortly after it does.

Two things about that follow the tenant count rather than the code:

- **A start that fails takes the tenants already created down with it.** The schedulers built before
  the failure are already bound to the repository, so the hosted service shuts them down before it
  rethrows rather than leaving them running with nothing left to stop them.
- **Shutdown is one deadline for every tenant, not one each.** The schedulers are shut down
  concurrently, so `HostOptions.ShutdownTimeout` bounds the whole set — with
  `WaitForJobsToComplete = true` the host waits as long as the slowest tenant's jobs take, not as long
  as all of them added together. Each scheduler owns its own thread pool, job store and scheduler
  thread, so there is nothing for them to serialize behind.

### Injecting one

A named scheduler is keyed by its name:

<!-- snippet: sample_tenancy_inject_named -->
```csharp
public sealed class TenantOpsService([FromKeyedServices("acme")] IScheduler scheduler);
```
<!-- endSnippet -->

<!-- snippet: sample_tenancy_resolve_named -->
```csharp
IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>(tenant);
```
<!-- endSnippet -->

::: warning
The unkeyed `IScheduler` is **the default scheduler** — the one registered by `AddQuartz(q => …)` with
no name. In a container holding only named schedulers there is no unkeyed registration at all, and
`GetRequiredService<IScheduler>()` throws. Resolve by key, or register a default scheduler as well.
:::

Trying to give a named scheduler and the default scheduler the same name is caught at registration:
`AddQuartz(o => o.InstanceName = "acme")` beside `AddQuartz("acme", …)` fails with a message naming both
calls, rather than as a duplicate-name `ArgumentException` from somewhere inside host start.

::: warning A scheduler name is compared two different ways
Not one, and knowing which is which saves an afternoon:

- **Case-insensitive**: the duplicate-name check, and every `ISchedulerRepository` lookup. So
  `AddQuartz("Acme", …)` beside `AddQuartz("acme", …)` is refused as one name, `repository.Lookup("acme")`
  finds the scheduler registered as `Acme`, and so does the HTTP API route `…/schedulers/acme/…`, which
  resolves through that repository.
- **Ordinal**: keyed resolution out of the container, and named options. So for a scheduler registered
  as `Acme`, `GetRequiredKeyedService<IScheduler>("acme")` throws — the container compares service keys
  by equality, and string equality is ordinal — and `Configure<QuartzOptions>("acme", …)` configures
  nothing, because the options framework matches instance names ordinally too.

Neither comparison is wrong for what it does; they are simply different, and only the first one is
forgiving. Spell the name once, put it in a constant, and use that constant everywhere.
:::

### Listing them

`ISchedulerFactory.GetAllSchedulers()` lists the schedulers something has already *created*. Under this
model that is the wrong question: a tenant nobody has asked for yet is still a tenant, and building every
one of them to find out what exists is exactly the cost you were avoiding.

`ISchedulerRegistry` reads the registrations instead:

<!-- snippet: sample_tenancy_scheduler_registry -->
```csharp
ISchedulerRegistry registry = provider.GetRequiredService<ISchedulerRegistry>();

foreach (SchedulerRegistration tenant in await registry.QuerySchedulers())
{
    Console.WriteLine($"{tenant.Name}: {tenant.Status?.ToString() ?? "registered, not created"}");
}
```
<!-- endSnippet -->

`Status` is `null` when no scheduler exists under that name, and asking does not build one. A scheduler
that has been *shut down* reads as `null` too rather than as `Shutdown`, because the repository drops a
shut-down scheduler as soon as a read notices it — and a shut-down scheduler cannot be rebuilt in the same
container anyway, so "not yet" and "not any more" are the same answer here: not a name you can get a
working scheduler out of. A scheduler whose state cannot be read at all — a remote one from
`AddQuartzHttpClient`, when the other process is unreachable — is reported as `SchedulerStatus.Unknown`
rather than dropped from the listing or allowed to throw: an inventory of tenants is exactly the call
that must not fail because one of them is down.

`Origin` says where the scheduler came from: `Container` for one `AddQuartz` registered, `Runtime` for
one that is in the repository without a registration behind it — a `QuartzSchedulerBuilder` scheduler
bound by hand, or a remote scheduler from `AddQuartzHttpClient`. The default scheduler appears under its
configured `InstanceName`.

Under `AddQuartzHostedService()` every registration is *built* while the host starts, so once the host is
up the distinction matters less than it looks. It matters while the host is still starting, when you
resolve schedulers yourself, when a start failed, and whenever you want an inventory rather than a list
of live objects.

::: warning The dashboard and the HTTP API list the repository, not the registry
Both resolve a scheduler by looking it up in `ISchedulerRepository`, which holds the schedulers something
has already built. A tenant that is registered but has never been created is therefore absent from the
dashboard's scheduler list, and its API routes answer `404` — not because the name is unknown, but
because nothing has built it yet. Under `AddQuartzHostedService()` that window closes as the host starts;
outside it — a scheduler resolved lazily, or one whose start failed — the two views disagree, and
`ISchedulerRegistry` is the one that knows the tenant exists.
:::

### What is per scheduler

Almost everything. Each named scheduler gets its own keyed registration of the job factory, the
signaler, the thread pool, the job store, the driver delegate, the object serializer, the instance-id
generator, the scheduler itself and its factory — plus its own listeners, plugins, calendars, jobs and
triggers.

::: warning Listeners and plugins are not symmetrical about the unkeyed registration
A listener and a plugin are both "registered per scheduler", and they treat a plain
`IServiceCollection` registration in opposite ways.

- **An unkeyed `ISchedulerListener`, `IJobListener` or `ITriggerListener` service reaches every
  scheduler in the container.** A scheduler unions the unkeyed listener services with the ones keyed to
  it, so `services.AddSingleton<IJobListener, AuditListener>()` is a container-wide listener — not the
  default scheduler's. Keyed registrations belong to the scheduler they name.
- **An unkeyed `ISchedulerPlugin` service reaches only the default scheduler.** A named scheduler reads
  the plugins keyed to it and nothing else, and [the properties probe](#plugins-named-by-properties) has
  no unkeyed fallback either.

The asymmetry is the difference between a listener, which is told which scheduler it is hearing from on
every callback, and a plugin, which is *bound* to one when it is initialized. So several schedulers
cannot share a plugin instance; each needs its own, and
[`ConfigureAllQuartzSchedulers`](#giving-every-scheduler-the-same-thing) is how to ask for that once.
:::

Options are **named options** whose name is the scheduler's name, and the container rewrites
`IOptions<T>` for a scheduler's own components so that `.Value` means *that* scheduler's settings.
Quartz's own option types — `QuartzSchedulerOptions`, `ThreadPoolOptions`, `InMemoryJobStoreOptions`,
`AdoJobStoreOptions`, `ClusteringOptions` and `QuartzOptions` — are declared that way once, in the
container; every other options type opts in by being *declared*, which is what
`ConfigureOptions<TOptions>()` does. That is also how `JobFactoryOptions` becomes per-scheduler:
`ConfigureJobScope(…)` is a `ConfigureOptions<JobFactoryOptions>` call, so the declaration arrives with
the hook rather than being built in. `AddPlugin<T, TOptions>()` does the same for a plugin's own
options type.

A clock is per scheduler too, when you set one:

<!-- snippet: sample_tenancy_time_provider -->
```csharp
builder.Services.AddQuartz("acme", q => q.UseTimeProvider(acmeClock));
```
<!-- endSnippet -->

A scheduler with no clock of its own inherits the container's, which is what lets an application-wide
`TimeProvider` reach all of them without being told about each.

### Job types

`AddJob<T>` registers the job type with the container so that a dependency it cannot be given is
reported when the container is validated. That registration is unkeyed and `TryAdd`, which is right for
one scheduler and not enough for several: the first registration would be what every scheduler got.

`AddJobType` gives one scheduler its own:

<!-- snippet: sample_tenancy_job_types -->
```csharp
builder.Services.AddQuartz("acme", q =>
{
    q.AddJobType<ReportJob, AcmeReportJob>();            // a different implementation
    q.AddJobType<AuditJob>(ServiceLifetime.Singleton);   // a different lifetime
    q.AddJobType<ExportJob>(sp => new ExportJob(sp.GetRequiredKeyedService<IExportSink>("acme")));

    q.AddJob<ReportJob>(j => j.WithIdentity("report"));
});
```
<!-- endSnippet -->

The job factory looks for this scheduler's registration first and falls back to the container's, so a
scheduler given nothing of its own resolves what the container holds and the default scheduler — which
has no service key — resolves in one lookup as it always has.

Two things worth knowing before reaching for it:

- The lifetime the job factory is built around is **scoped**: a scope is opened per fire, the job is
  resolved from it, and the scope is disposed when the job returns. `ServiceLifetime.Singleton` means
  one instance serves every fire of that job on that scheduler, so it must be thread-safe and must not
  capture scoped dependencies.
- A job type per tenant is usually the wrong shape at more than a handful of tenants. One job type that
  reads its tenant from the firing and resolves what it needs — by key, if you like — inside `Execute`
  scales where a registration per tenant does not.

### Plugins named by properties

A `quartz.plugin.<name>.*` entry is read from the property bag of the scheduler it was configured on,
so two tenants each configuring an XML plugin get two instances with their own files:

<!-- snippet: sample_tenancy_plugin_by_properties -->
```csharp
builder.Services.AddQuartz("acme", new NameValueCollection
{
    ["quartz.plugin.xml.type"] = typeof(XmlSchedulingDataProcessorPlugin).AssemblyQualifiedName,
    ["quartz.plugin.xml.fileNames"] = "acme-jobs.xml",
});
```
<!-- endSnippet -->

Before the instance is built, the container is asked whether that type is registered as a service —
and that question is asked **under this scheduler's key**. So a named scheduler gets the registration
made for it, an unkeyed registration belongs to the default scheduler, and a scheduler with no
registration of its own gets a fresh instance built from its own properties. There is deliberately no
fallback from one scheduler's key to the unkeyed registration: a plugin is told which scheduler it
extends when it is initialized, so handing one instance to two schedulers means the second
initialization overwrites the first.

To give a named scheduler a plugin instance you built yourself, register it under that scheduler —
`q.AddPlugin<T>(provider => …)` — rather than unkeyed on `IServiceCollection`.

### Giving every scheduler the same thing

Writing the same three lines inside every `AddQuartz(tenant, …)` is what a `foreach` over the tenants
saves you, right up until the tenants come from configuration and the loop is not yours to write.
`ConfigureAllQuartzSchedulers` applies one configuration callback to every scheduler in the container:

<!-- snippet: sample_tenancy_configure_all -->
```csharp
builder.Services.AddQuartz("acme", q => q.UsePersistentStore(s => s.UseSqlServer(acme)));
builder.Services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));

// Every scheduler above, and every scheduler registered after this line
builder.Services.ConfigureAllQuartzSchedulers(q =>
{
    q.AddPlugin<TenantAuditPlugin>();
    q.AddJobListener<AuditListener>();
});
```
<!-- endSnippet -->

Four things it promises:

- **Order does not matter.** It runs after each scheduler's own configuration callback either way: a
  scheduler registered before this call is configured here, one registered after it is configured by its
  own `AddQuartz`, and both get it *after* their own callback. So a scheduler's own configuration is
  what a shared callback refines whichever order the two calls were written in — which is the point,
  since a package that adds something to every scheduler cannot know when the application registers its
  schedulers.
- **The usual precedence follows from that.** Registration is first-wins, so a job store or thread pool
  a tenant chose for itself is not replaced by one chosen here; options are last-wins, so a value set
  here overrides the same option set on one tenant, exactly as `ConfigureAll<TOptions>` overrides an
  earlier named `Configure`.
- **Every scheduler gets its own instance of whatever the callback adds.** The delegate is handed a
  builder *per scheduler*, so what it registers lands under that scheduler's key: a plugin added this
  way to three schedulers is three plugin instances, each initialized with the name of the scheduler it
  extends — which is exactly what a plugin registered unkeyed cannot be.
- **It reaches `AddQuartz()`, `AddQuartz(name, …)` and `AddQuartzSchedulers(…)` alike**, because all
  three register a builder. Remote schedulers from `AddQuartzHttpClient` are skipped: they live in
  another process, so there is no builder here to configure and nothing this callback adds could reach
  them. Calling it when no scheduler is registered at all is not an error — the delegate applies to
  nothing.

It is the options pattern's `ConfigureAll` for schedulers, and it is how `AddQuartzDashboard` gives
every scheduler the dashboard's own plugins rather than only the default one — which is what stops a
named scheduler's live view and history pages from being permanently empty. The [migration
guide](migration-guide.md#every-scheduler-in-the-container-can-be-configured-at-once) states the same
rules for a reader arriving from 3.x.

### What is not

A handful of things are container-wide, shared by every scheduler in the process:

| | |
|---|---|
| `ITypeLoader` | type loading is a container-wide concern; `UseTypeLoader<T>()` **replaces** it for everyone |
| `ISchedulerRepository` | one per container — that is what makes `GetAllSchedulers` and the dashboard see all of them |
| `ISchedulerRegistry` | one per container — it answers for every registration in it, which is what makes it an inventory rather than a scheduler's own view |
| `IJobExecutionContextAccessor` | one per container, and the firing it reports is a property of the asynchronous flow rather than of a scheduler — a flow is inside at most one firing, whichever scheduler started it |
| `SystemTextJsonSerializerRegistry` | one per container by default — the HTTP API and the HTTP client serialize triggers without knowing which scheduler they came from. A named scheduler *can* be given its own, see below |
| `Meters` | built from the container's `IMeterFactory` |
| `DataSourceOptions` | named after the **data source**, not the scheduler, so several schedulers can read through the same one |
| `QuartzHttpApiOptions` | one per process — see [honest limits](#honest-limits) |

Logging is one per container rather than one per scheduler: every scheduler's parts are injected the
container's `ILoggerFactory`, and a line says which tenant wrote it through the logging scope the
scheduling loop opens — `quartz.scheduler.name` and `quartz.scheduler.id`, the same attribute names the
traces and the measurements use. Filter or enrich on those rather than looking for a logger per tenant.

And one thing that is not a container service at all: **`LogProvider`** is process-wide static state.
`SetLogProvider(loggerFactory)` sets it for everything in the process, deliberately. It no longer has
anything to do with whether a scheduler logs; what it reaches is the types no container builds — a
listener or trigger you constructed, the static helpers, the jobs in `Quartz.Jobs`.

The serializer registry is the one row in that table with a way out. A named scheduler resolves its
registry by key and falls back to the container's, so `services.AddKeyedSingleton(schedulerName, registry)`
— or the per-store `UseSystemTextJsonSerializer(json => …)` callback, which is the same thing — gives one
tenant its own custom trigger and calendar serializers. What that changes is what *that scheduler's job
store* persists and reads. Everything that serializes without a scheduler in hand — the HTTP API's
responses, the dashboard, `Quartz.HttpClient` — still reads the container's registry, so a custom type
those must render has to be registered there as well. See
[System.Text.Json serialization](packages/system-text-json.md#making-custom-serializers-visible-outside-the-job-store).

### Health checks per tenant

`AddQuartzHealthChecks` called on a *scheduler's* builder checks that scheduler, and defaults its name
to `quartz-scheduler-<scheduler name>` so several can be registered side by side:

<!-- snippet: sample_tenancy_health_checks -->
```csharp
builder.Services.AddQuartz("acme", q => q.AddQuartzHealthChecks(o => o.Tags.Add("tenant:acme")));
```
<!-- endSnippet -->

Called on `IServiceCollection` instead, it checks the default scheduler only.

## Group per tenant

One scheduler; the tenant is the group half of every key.

<!-- snippet: sample_tenancy_group_keys -->
```csharp
JobKey job = new("nightly-report", tenantId);
TriggerKey trigger = new("nightly", tenantId);
```
<!-- endSnippet -->

Everything that takes a matcher then becomes tenant-scoped:

<!-- snippet: sample_tenancy_group_matchers -->
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
<!-- endSnippet -->

Pause state is real and queryable for both trigger groups and job groups — the ADO store persists a
paused job group in `QRTZ_PAUSED_JOB_GRPS`, so `QueryJobGroups(new JobGroupQuery { Name = tenantId,
Take = 1 })` answers for a tenant partitioned by job group just as well. Still suspend by **trigger**
group where the suspension has to reach work scheduled after it: a paused trigger group starts a later
trigger paused on either store, where a paused job group does so only in the in-memory one.

Listeners take matchers too, so a per-tenant listener is one registration:

<!-- snippet: sample_tenancy_group_listener -->
```csharp
q.AddJobListener<AuditListener>(Matchers.Group<JobKey>(StringOperator.Equality, tenantId));
```
<!-- endSnippet -->

### Per-tenant concurrency quotas

Execution groups cap how many threads a category of work may use. When the schedule already partitions
work by trigger group — a tenant per group — the trigger group can stand in for the execution group, so
a quota is one line per tenant and no change to any trigger:

<!-- snippet: sample_tenancy_execution_limits -->
```csharp
q.UseExecutionLimits(limits => limits
    .UseTriggerGroupWhenUnset()
    .ForGroup("acme", 8, ExecutionLimitScope.Cluster)          // a big tenant
    .ForGroup("initech", 2, ExecutionLimitScope.Cluster)
    .ForOtherGroups(1, ExecutionLimitScope.Cluster));          // everyone else gets one thread each
```
<!-- endSnippet -->

`ExecutionLimitScope.Cluster` is what makes these quotas rather than capacity settings: the number is
what every node sharing the job store may run **between them**, counted from the reservations the store
itself is holding. Leave the scope off and each limit is per node instead, which on a three-node cluster
means `ForGroup("acme", 8)` allows 24 concurrent Acme jobs. Both scopes are legitimate — node-scoped for
"this machine can stand eight", cluster-scoped for "this tenant is entitled to eight" — and one set of
limits can hold both.

`UseTriggerGroupWhenUnset()` changes nothing about the data — the trigger still carries no execution
group, and the store still persists none. It changes only how a limit is looked up. Three consequences:

- An explicit `ExecutionGroup` on a trigger always wins.
- `ForDefaultGroup` stops catching anything, because with this on nothing is ungrouped.
  Unlisted tenants fall under `ForOtherGroups`.
- Each unlisted group gets **its own** allowance from `ForOtherGroups`, not a shared one. Three unlisted
  tenants under `ForOtherGroups(1)` can run three jobs, one each.

`Unlimited(group)` is not the same as leaving a group out: an unlisted group falls back to
`ForOtherGroups`, an explicitly unlimited one does not.

Limits can also be changed at runtime with `SetExecutionLimits` / `GetExecutionLimits` — they take
effect on the next acquisition cycle, and `null` clears them. The call is per node whichever scope the
limits use: it replaces what *this* scheduler enforces, so a cluster-scoped quota you mean every node to
honour has to be set on every node, or configured rather than set.

::: warning What a cluster-scoped quota does and does not promise
The ceiling holds **within one acquisition round**, and by default acquisition takes no cluster lock, so
a brief overshoot is possible while several nodes acquire at once — at most `limit + (nodes − 1)`, until
the losers notice. The lock-free path exists only when a round acquires a single trigger: the ADO store
takes the `TRIGGER_ACCESS` lock whenever it is asked for more than one, so a node acquiring without the
lock can add at most one over the ceiling. `AcquireTriggersWithinLock = true` makes it exact and
serializes acquisition cluster-wide.

It **fails closed**: the quota ledger and the work queue are the same database, so a node that cannot
reach the store fires nothing at all rather than firing unmetered. Plan for a database outage stopping
work, not for it removing the ceiling.

A group held at its ceiling for longer than `MisfireThreshold` (one minute by default) feeds its backlog
into misfire handling. Pair a tight quota with `MisfireInstruction.IgnoreMisfirePolicy` or a larger
threshold. See [Execution Groups](tutorial/execution-groups.md#clustering-considerations) for the
full statement.
:::

## Shared database

Every Quartz table has `SCHED_NAME` as the first column of its primary key, and every statement filters
on it. Two schedulers with different names therefore share tables without seeing each other's rows, and
that is a property of the schema rather than of the code paths — there is no query that forgets.

Table prefix is the other axis. `AdoJobStoreOptions.TablePrefix` (default `QRTZ_`) is a per-scheduler
option, so two tenants can have entirely separate table *sets* in one database:

<!-- snippet: sample_tenancy_table_prefix -->
```csharp
builder.Services.AddQuartz("acme", q => q.UsePersistentStore(s =>
{
    s.UseSqlServer(sharedConnectionString);
    s.Configure(o => o.TablePrefix = "ACME_QRTZ_");
}));
```
<!-- endSnippet -->

Four rules:

- **Different scheduler name is enough.** Prefixes are for keeping tenants in separate *tables*, which
  is a backup-and-restore or a permissions decision, not an isolation one.
- **The prefix has to match the DDL.** Nothing derives one from the other; you run the DDL with the
  prefix substituted.
- **A prefix pointing at tables that do not exist is caught at startup.**
  `PerformSchemaValidation` is on by default, and a missing or mis-prefixed table is reported once, by
  name, with a message telling you to run the schema scripts — rather than surfacing as the first failing
  operation an hour later.
- **A prefix pointing at the *wrong* tables is reported too, as a warning.** Schema validation cannot
  catch that one: the tables exist, they are simply somebody else's, and the scheduler starts, reports
  healthy and never sees its own data. So creating a scheduler records its database and its table prefix,
  and a scheduler that shares a database with one already created but disagrees about the prefix produces
  a `Warning` naming both schedulers and both prefixes. Prefixes are compared **ignoring case**, because
  every database Quartz supports folds an unquoted identifier to one case — `qrtz_` and `QRTZ_` are one
  table set, and two tenants told apart only by the casing of their prefix are one tenant.

::: tip Why that one is a warning and not an error
Separate table sets in one database are legal, and the arrangement above is exactly how you ask for them.
Nothing Quartz can see tells a deliberate `ACME_QRTZ_` apart from a mistyped `QRTZ2_`, and an error that
fires on a legitimate arrangement is worse than the silence it replaces. If the two prefixes are meant to
differ, the warning is expected and can be filtered out on the
`Quartz.Configuration.SharedDatabaseValidator` category.

It also only sees what one container can see. Two processes — or two containers in one process — sharing
a database are invisible to each other, and so is a database reached through a provider that reports
neither a connection string nor a `DbDataSource`. Being wrong in that direction is deliberate: a check
that stays quiet when it cannot tell costs you nothing.
:::

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

<!-- snippet: sample_tenancy_ambient_tenant -->
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
<!-- endSnippet -->

<!-- snippet: sample_tenancy_configure_job_scope -->
```csharp
q.ConfigureJobScope((scope, bundle, scheduler) =>
{
    TenantContext.Current = bundle.Trigger.Key.Group;
});
```
<!-- endSnippet -->

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
The context does not exist yet when the hook runs — it takes the job instance, and the job has not been
built. So the two patterns *for seeding construction* are the `AsyncLocal` above, and resolving a scoped
holder object from `scope.ServiceProvider` and populating it. The second is easier to test and does not
depend on execution context flow. For everything that reads the tenant when it is *used* rather than
when it is constructed, `IJobExecutionContextAccessor` below is neither.
:::

### Which scheduler's parts a job is built from

Two jobs on the same named scheduler can be handed different collaborators, and which one you get turns
on whether the *container* knows the job type.

- **A job type the container holds is built by the container.** `AddJob<T>` registers the type with
  `TryAddScoped`, so the job is resolved as an ordinary scoped service and its constructor dependencies
  are resolved **unkeyed**. A job on scheduler `acme` that injects `ISchedulerFactory`, `IJobStore`,
  `IThreadPool` or `IOptions<QuartzSchedulerOptions>` therefore gets the *default* scheduler's — and in
  a container holding only named schedulers there is no unkeyed registration to get, so the job cannot
  be constructed at all: reported when the container is validated, or at the first fire in a container
  that does not validate.
- **A job type the container does not hold is activated by the job factory**, through the
  scheduler-scoped provider, and its dependencies resolve to *its own* scheduler's parts. That is any
  job the container was never told about: one scheduled at runtime with `ScheduleJob(jobDetail, …)`,
  and one named only by an XML or JSON schedule, which nothing describes to the container.

That the two differ is the point worth carrying away, because nothing about the job says which path it
is on. **Do not inject a scheduler's own parts into a job.** A job that needs the scheduler running it
takes `IJobExecutionContext.Scheduler`, which is that scheduler under either path:

<!-- snippet: sample_tenancy_job_reads_its_scheduler -->
```csharp
public sealed class RotateTenantKeysJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // The scheduler running this fire, whichever tenant it belongs to. An injected
        // ISchedulerFactory would have been the default scheduler's.
        IScheduler mine = context.Scheduler;

        await mine.PauseTriggers(
            GroupMatcher<TriggerKey>.GroupEquals(context.Trigger.Key.Group),
            cancellationToken);
    }
}
```
<!-- endSnippet -->

and a service the job calls into reads the firing from [`IJobExecutionContextAccessor`](#reading-the-firing-from-anywhere-in-it).
Where the job genuinely has to be *constructed* with something of its scheduler's, register it with
`AddJobType<T>(factory)` and resolve that by key inside the factory.

### Reading the firing from anywhere in it

A great deal of code that wants the tenant is not the job and cannot be handed the context: a scoped
service, a logging enricher, a repository three calls below `Execute`. `IJobExecutionContextAccessor` is
the firing that code is part of:

<!-- snippet: sample_tenancy_execution_context_accessor -->
```csharp
public sealed class TenantConnectionFactory(
    IJobExecutionContextAccessor accessor,
    IReadOnlyDictionary<string, string> connectionStrings)
{
    public string ConnectionString =>
        connectionStrings[accessor.Current?.Trigger.Key.Group
            ?? throw new InvalidOperationException("no job is running on this flow")];
}
```
<!-- endSnippet -->

It is registered by `AddQuartz` as a singleton, and it exposes the whole `IJobExecutionContext` rather
than a tenant-shaped projection of it — Quartz has no tenant concept, so a narrower type here would be
inventing one, and it would have to grow a member every time somebody needed one more fact the context
already carries.

Four things about the window it is set for, all of them worth knowing before relying on it:

- **It is set from the moment the context exists** — before the trigger and job listeners are notified —
  **until the job has been returned to the job factory.** Outside that it is `null`: on the scheduling
  thread, in application code that merely calls `ScheduleJob`, and in an `ISchedulerListener` reacting to
  a scheduling call rather than to a firing. Treat `null` as a real answer rather than an impossible one.
- **It is never another firing's.** The value travels with the `ExecutionContext`, so it belongs to the
  logical flow and not to the thread; a pooled thread picking up unrelated work inherits nothing.
- **It survives `await` and `Task.Run`, and stops at the end of the firing.** Work started inside the job
  and left running past `Execute` — a detached `Task.Run`, a continuation nobody awaits — reads `null`
  from the moment the execution ends, not the finished context. That is deliberate: by then the job's DI
  scope has been disposed and the context's cancellation handle is going. `ExecutionContext.SuppressFlow`
  hides it, as it hides every ambient value.
- **There is no setter.** An ambient context anyone can assign is one that can be left pointing at a
  firing that is over, which would be worse than having no accessor at all. Substitute the interface in
  a test instead.

It does **not** replace `ConfigureJobScope`: the context does not exist while the job is being
constructed, so anything that needs the tenant *at construction time* — a `DbContext` given a
tenant connection string in its constructor — still gets it from the hook.

For anything more involved — resolving jobs from a tenant-owned container, say — implement `IJobFactory`
and register it with `UseJobFactory<T>()`, or derive from `MicrosoftDependencyInjectionJobFactory` and
override `protected virtual void ConfigureScope(...)`. An override that does not call `base` takes the
delegate's place.

Per-fire options are a snapshot: read what the tenant's configuration says *inside* the hook or the job,
not once at startup, if tenants can be reconfigured while the process runs.

## Honest limits

Things multi-tenant deployments ask Quartz for and do not get:

**A cluster-wide concurrency ceiling is approximate, not exact, unless you pay for exactness.**
`ExecutionLimitScope.Cluster` counts a group's in-flight work from `QRTZ_FIRED_TRIGGERS`, which is
transactional and cluster-wide, but the default acquisition path takes no cluster lock — so the ceiling
holds within one acquisition round and can transiently overshoot by up to `nodes − 1` while several
nodes acquire at once. The overshoot is one trigger per node and no more, because the lock-free path is
only taken when a round acquires a single trigger: asking for a batch takes the `TRIGGER_ACCESS` lock,
which is the same lock `AcquireTriggersWithinLock = true` takes on every round. That setting removes the
overshoot and serializes acquisition for every group, limited or not. There is no third setting that
gives you both.

**There is no rate limiting.** Execution limits cap *concurrency*, not throughput. "This tenant may run
100 jobs an hour" is not something Quartz can express; build it in the job, or in the thing the job
calls. It is worth asking whether concurrency is what you actually meant: "at most four of this tenant's
jobs at once" is usually the real requirement behind "100 an hour", and it is the one Quartz can enforce
honestly.

**HTTP API and dashboard authorization is per process, all or nothing.** One `MapQuartzHttpApi` serves
every scheduler in the container, with the scheduler named in the route
(`{apiPath}/schedulers/{schedulerName}/…`), and `RequireAuthorization(...)` applies uniformly to all of
it. The dashboard has a single `AuthorizationPolicy` and a single `ReadOnly` flag. There is no
per-scheduler policy and no scheduler-name claim check. If tenants must reach their own scheduler and
not each other's, enforce that **outside** Quartz — a process per tenant, or a proxy or middleware that
authorizes on the `{schedulerName}` route segment.

**Tenants cannot be onboarded at runtime *through the DI path*.** Schedulers are registered against
`IServiceCollection`, which is closed once the container is built, and the hosted service enumerates
them once at start. (Enumerating what *is* registered no longer requires starting anything —
[`ISchedulerRegistry`](#listing-them) — but adding to it does still require a new container.) Nor can a
scheduler be restarted after `Shutdown()`: the container owns its parts' lifetimes, and `GetScheduler()`
throws rather than resurrecting a thread pool and a job store underneath a scheduler that can never run
again. `Standby()` / `Start()` is the pause-and-resume pair.

That is a limit of the DI path, not of the library. `QuartzSchedulerBuilder` builds a scheduler from a
container of its own, at any point in the process's life, and `ISchedulerRepository.Bind` makes the
result visible to `GetAllSchedulers`, the dashboard and the HTTP API:

<!-- snippet: sample_tenancy_runtime_onboarding -->
```csharp
StandaloneSchedulerFactory tenantFactory = QuartzSchedulerBuilder.Create()
    .ConfigureScheduler(o => o.InstanceName = tenantId)
    .UsePersistentStore(s => s.UseSqlServer(connectionStrings[tenantId]))
    .Build();

IScheduler tenant = await tenantFactory.GetScheduler();
await tenant.Start();

tenantFactories[tenantId] = tenantFactory;
app.Services.GetRequiredService<ISchedulerRepository>().Bind(tenant);
```
<!-- endSnippet -->

Keep the factory for as long as the tenant exists — `tenantFactories` above is a dictionary keyed by
tenant id — because it owns the container and is the only handle that can shut the tenant down again.
`BuildScheduler()` is the shorter spelling that drops it on the floor, which is fine for a scheduler
that lives as long as the process and wrong for one that has to be offboarded.

What you take on by doing this: the returned `StandaloneSchedulerFactory` owns the container, so *you*
start the scheduler and dispose the factory — the hosted service will not; the scheduler's jobs resolve
from its own container rather than the application's unless you give it an `IJobFactory` that bridges;
and health checks registered at startup do not cover it.

`Bind` refuses a duplicate **(name, instance id)** pair, not a duplicate name: two schedulers of one
name and different instance ids coexist by design, which is how proxies to several nodes of one cluster
are held. In practice the common case still throws, because a scheduler that has not opted into
clustering takes the default instance id `NON_CLUSTERED` — so two non-clustered tenants sharing a name
collide on the pair. Give each tenant's scheduler a distinct `InstanceName` and the question does not
arise.

Offboarding is disposing the factory, which shuts the tenant's scheduler down and then disposes its
container. Unbind it too, so the application's repository stops listing it at once rather than at its
next read:

<!-- snippet: sample_tenancy_offboarding -->
```csharp
StandaloneSchedulerFactory tenantFactory = tenantFactories[tenantId];
tenantFactories.Remove(tenantId);

// Disposal shuts the scheduler down without waiting for its jobs, so ask for the wait here.
IScheduler tenant = await tenantFactory.GetScheduler();
await tenant.Shutdown(waitForJobsToComplete: true);

await tenantFactory.DisposeAsync();
app.Services.GetRequiredService<ISchedulerRepository>().Remove(tenantId);
```
<!-- endSnippet -->

::: warning Disposal does not wait for running jobs
`StandaloneSchedulerFactory.DisposeAsync` shuts down with `waitForJobsToComplete: false`, so a tenant's
jobs are cut short — which is the same default `IScheduler.Shutdown()` and `QuartzHostedServiceOptions`
both carry, and two Quartz-owned shutdown paths that disagreed about it would be a trap. Waiting is a
call you make first, as above: `await scheduler.Shutdown(waitForJobsToComplete: true)` leaves the
factory with nothing left to shut down, so the two compose and the disposal only releases the container.
:::

::: warning Unbinding is not offboarding
`Remove` makes a tenant invisible; only the disposal stops it. That distinction used to be fatal — in
4.0.0-alpha.1 disposing the factory disposed only its container, so this recipe took the tenant out of
`GetAllSchedulers`, off the dashboard and out of the HTTP API while it went on firing its triggers for
the rest of the process's life, with nothing left able to reach it
([#3380](https://github.com/quartznet/quartznet/issues/3380)). Disposal shuts the scheduler down as of
4.0.0-alpha.2, which is what makes the steps above safe in either order.
:::

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
- [Execution Groups](tutorial/execution-groups.md) — per-node and cluster-wide thread limits in full
- [Querying Jobs and Triggers](tutorial/querying-jobs-and-triggers.md) — group-filtered listings
- [Clustering](tutorial/advanced-enterprise-features.md) — what a shared database gives you
- [Migration Guide](migration-guide.md) — including why a shut-down scheduler cannot be restarted
