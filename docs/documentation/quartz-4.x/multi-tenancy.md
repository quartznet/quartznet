---
title: 'Multi-Tenancy'
---

# Multi-Tenancy

Quartz has no `Tenant` concept and will not get one. Build tenancy from three separations: a scheduler,
a group, or a `SCHED_NAME` (with an optional table prefix). [Tenancy Patterns](../tenancy-patterns.md)
compares how other schedulers partition tenants, and which factors decide between the models.

## Choosing a model

| | Scheduler per tenant | Group per tenant | Database or prefix per tenant |
|---|---|---|---|
| **Isolation** | strongest: separate job store, thread pool, clock, listeners | logical only — one scheduler, one pool | strongest at rest; one process still runs them all |
| **Tenants known at** | startup or runtime | any time | startup |
| **Add a tenant at runtime** | yes — [`ISchedulerRuntime`](#adding-a-tenant-while-the-process-is-running) | yes | no |
| **Reconfigure a tenant at runtime** | yes — [`Restart`](#restarting-a-scheduler) rebuilds it from its recipe | its jobs and triggers, any time | no |
| **Per-tenant concurrency limits** | yes, naturally | yes, via execution groups | yes |
| **Per-tenant dashboard and API access** | yes — [`SchedulerAuthorizationPolicy`](#authorizing-a-tenant-on-its-own-scheduler) | no: Quartz authorizes a scheduler, not a group | yes, if each is its own scheduler |
| **Cost per tenant** | a scheduling loop, a connection pool, a thread pool | ~nothing | a schema |
| **Fits** | tens of tenants, strong isolation needs | hundreds or thousands of tenants | regulatory separation of data |

The models compose. A common SaaS shape: *one* scheduler with a group per tenant and one database, plus a
second scheduler for the few tenants that bought isolation.

**Cost per tenant usually decides.** A scheduler per tenant gives each one a scheduling loop, a thread
pool and, with a persistent store, a connection pool and a cluster check-in: fine for tens of tenants, not
for thousands. Whether tenants appear at runtime no longer decides it:
[`ISchedulerRuntime`](#adding-a-tenant-while-the-process-is-running) adds one to a built container.

## Scheduler per tenant

`AddQuartz(name, …)` registers a named scheduler. The name is its instance name, the key its components
are registered under, and the name of its options, so registrations and configuration always agree.

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

One `AddQuartzHostedService` starts them all. The named overload, `AddQuartzHostedService(tenant, o => …)`,
configures *that* scheduler's start options and still registers only one hosted service; two would each
start every scheduler in the container.

### Starting and stopping all of them

The hosted service builds every scheduler while the host starts, then starts them: immediately under
`AwaitApplicationStarted = false`, otherwise (the default) once `ApplicationStarted` fires.

- **A failed start stops the tenants already created.** Schedulers built before the failure are already
  bound to the repository, so the hosted service shuts them down before it rethrows.
- **Shutdown is one deadline for all tenants.** Schedulers shut down concurrently, so
  `HostOptions.ShutdownTimeout` bounds the whole set. With `WaitForJobsToComplete = true` the host waits
  for the slowest tenant's jobs, not the sum. Each scheduler has its own thread pool, job store and
  scheduler thread, so nothing serializes them.

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
The unkeyed `IScheduler` is **the default scheduler**, registered by `AddQuartz(q => …)` with no name. A
container with only named schedulers has no unkeyed registration, and `GetRequiredService<IScheduler>()`
throws. Resolve by key, or also register a default scheduler.
:::

Giving a named scheduler and the default scheduler the same name fails at registration:
`AddQuartz(o => o.InstanceName = "acme")` beside `AddQuartz("acme", …)` throws with a message naming both
calls, not a duplicate-name `ArgumentException` during host start.

::: warning A scheduler name is compared two different ways

- **Case-insensitive**: the duplicate-name check and every `ISchedulerRepository` lookup. `AddQuartz("Acme", …)`
  beside `AddQuartz("acme", …)` is refused as one name, and `repository.Lookup("acme")` finds `Acme`, as
  does the HTTP API route `…/schedulers/acme/…`, which resolves through the repository.
- **Ordinal**: keyed resolution and named options. For a scheduler registered as `Acme`,
  `GetRequiredKeyedService<IScheduler>("acme")` throws (service keys compare by equality, and string
  equality is ordinal), and `Configure<QuartzOptions>("acme", …)` configures nothing.

Spell the name once, in a constant, and use it everywhere.
:::

### Listing them

`ISchedulerFactory.GetAllSchedulers()` lists only schedulers already *created*, and building every tenant
to find out what exists is the cost this model avoids. `ISchedulerRegistry` reads the registrations:

<!-- snippet: sample_tenancy_scheduler_registry -->
```csharp
ISchedulerRegistry registry = provider.GetRequiredService<ISchedulerRegistry>();

foreach (SchedulerRegistration tenant in await registry.QuerySchedulers())
{
    Console.WriteLine($"{tenant.Name}: {tenant.Status?.ToString() ?? "registered, not created"}");
}
```
<!-- endSnippet -->

- `Status` is `null` when no scheduler exists under that name; asking does not build one.
- A *shut-down* scheduler also reads `null`, not `Shutdown`: the repository drops it on the next read,
  and it cannot be rebuilt in the same container.
- A scheduler whose state cannot be read, such as an unreachable remote one from `AddQuartzHttpClient`,
  is `SchedulerStatus.Unknown`; the listing neither drops it nor throws.

`Origin` says where the scheduler came from:

| `Origin` | What it is |
|---|---|
| `Container` | One `AddQuartz` registered. The default scheduler appears under its configured `InstanceName` |
| `Runtime` | In the repository with no registration: a `QuartzSchedulerBuilder` scheduler bound by hand, or one [added while the process was running](#adding-a-tenant-while-the-process-is-running) |
| `Remote` | An `HttpScheduler` from `AddQuartzHttpClient`. Runs in another process, keeps its history there, and has no live event stream here |

Before 4.1, `Remote` schedulers were reported as `Runtime`.

Under `AddQuartzHostedService()` every registration is built during host start, so registered-but-not-built
matters while the host is starting, when you resolve schedulers yourself, after a failed start, and for
an inventory.

The dashboard and the HTTP API read the same registry, so both list tenants nothing has built:

- `GET {ApiPath}/schedulers` reports one with a `null` status and no instance id.
- The dashboard's **Schedulers** page at `/quartz/schedulers` shows it as **not created**; for tenants
  that exist it shows the job store, thread pool, start time, jobs executed and node count. The header's
  scheduler picker lists it greyed out.

::: warning A tenant's own routes still resolve through the repository
Only the listing reads the registry. Routes addressed *at* a scheduler — `GET {ApiPath}/schedulers/acme`,
its jobs, its triggers — resolve through `ISchedulerRepository`, which holds built schedulers only, so
they answer `404` for a tenant nothing has created. Under `AddQuartzHostedService()` that window closes as
the host starts; for a lazily resolved scheduler or a failed start it stays open.
:::

### What is per scheduler

Almost everything. Each named scheduler has its own keyed job factory, signaler, thread pool, job store,
driver delegate, object serializer, instance-id generator, scheduler and factory, plus its own listeners,
plugins, calendars, jobs and triggers.

::: warning Listeners and plugins treat an unkeyed registration differently

- **An unkeyed `ISchedulerListener`, `IJobListener` or `ITriggerListener` reaches every scheduler.** A
  scheduler combines the unkeyed listener services with those keyed to it, so
  `services.AddSingleton<IJobListener, AuditListener>()` is container-wide, not the default scheduler's.
  Keyed registrations belong to the scheduler they name.
- **An unkeyed `ISchedulerPlugin` reaches only the default scheduler.** A named scheduler reads only the
  plugins keyed to it, and [the properties probe](#plugins-named-by-properties) has no unkeyed fallback.

A listener is told on every callback which scheduler is calling; a plugin is *bound* to one scheduler
when initialized. So schedulers cannot share a plugin instance; use
[`ConfigureAllQuartzSchedulers`](#giving-every-scheduler-the-same-thing) to give each its own.
:::

Options are **named options** under the scheduler's name, and the container rewrites `IOptions<T>` for
the scheduler's own components so `.Value` is *that* scheduler's settings.

- Quartz's option types — `QuartzSchedulerOptions`, `ThreadPoolOptions`, `InMemoryJobStoreOptions`,
  `AdoJobStoreOptions`, `ClusteringOptions` and `QuartzOptions` — are declared that way by the container.
- Any other options type opts in by being declared with `ConfigureOptions<TOptions>()`.
  `ConfigureJobScope(…)` is a `ConfigureOptions<JobFactoryOptions>` call, which makes `JobFactoryOptions`
  per scheduler. `AddPlugin<T, TOptions>()` does the same for a plugin's options type.

A clock is per scheduler when you set one:

<!-- snippet: sample_tenancy_time_provider -->
```csharp
builder.Services.AddQuartz("acme", q => q.UseTimeProvider(acmeClock));
```
<!-- endSnippet -->

A scheduler with no clock inherits the container's, so an application-wide `TimeProvider` reaches every
scheduler.

### Job types

`AddJob<T>` registers the job type with the container, unkeyed and with `TryAdd`, so a missing dependency
is reported when the container is validated. With several schedulers, the first registration is what
every scheduler gets. `AddJobType` gives one scheduler its own:

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

The job factory looks for the scheduler's registration first, then the container's. The default
scheduler has no service key and resolves in one lookup.

- The job factory is built around **scoped** lifetime: a scope per fire, the job resolved from it, the
  scope disposed when the job returns. `ServiceLifetime.Singleton` means one instance serves every fire
  of that job on that scheduler, so it must be thread-safe and must not capture scoped dependencies.
- Past a handful of tenants, prefer one job type that reads its tenant from the firing and resolves what
  it needs (by key, if you like) inside `Execute` over a job type per tenant.

### Plugins named by properties

A `quartz.plugin.<name>.*` entry is read from the property bag of its own scheduler, so two tenants each
configuring a scheduling-data plugin get two instances with their own files:

<!-- snippet: sample_tenancy_plugin_by_properties -->
```csharp
builder.Services.AddQuartz("acme", new NameValueCollection
{
    ["quartz.plugin.json.type"] = typeof(JsonSchedulingDataProcessorPlugin).AssemblyQualifiedName,
    ["quartz.plugin.json.fileNames"] = "acme-jobs.json",
});
```
<!-- endSnippet -->

Before building the instance, Quartz asks the container whether the type is registered **under this
scheduler's key**:

- a named scheduler gets the registration made for it;
- an unkeyed registration belongs to the default scheduler;
- a scheduler with no registration gets a fresh instance built from its own properties.

There is no fallback from a scheduler's key to the unkeyed registration: a plugin is told its scheduler
when initialized, so one instance given to two schedulers is overwritten by the second initialization. To
give a named scheduler a plugin you built, register it under that scheduler with
`q.AddPlugin<T>(provider => …)`, not unkeyed on `IServiceCollection`.

### Giving every scheduler the same thing

`ConfigureAllQuartzSchedulers` applies one configuration callback to every scheduler in the container —
useful when tenants come from configuration and there is no loop of yours to put the lines in:

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

- **Order does not matter.** It runs after each scheduler's own callback, whether that scheduler was
  registered before or after this call, so a package can use it without knowing when schedulers are
  registered.
- **Precedence:** registration is first-wins, so a job store or thread pool a tenant chose stays; options
  are last-wins, so a value set here overrides a tenant's, as `ConfigureAll<TOptions>` overrides a named
  `Configure`.
- **Each scheduler gets its own instance** of what the callback adds, under its key: a plugin added to
  three schedulers is three instances, each initialized with its scheduler's name.
- **It reaches `AddQuartz()`, `AddQuartz(name, …)` and `AddQuartzSchedulers(…)`**, not remote schedulers
  from `AddQuartzHttpClient`, which have no builder in this process. With no scheduler registered it
  applies to nothing, without error.

`AddQuartzDashboard` uses it to give every scheduler the dashboard's plugins, without which a named
scheduler's live view and history pages stay empty. See also the
[migration guide](migration-guide.md#every-scheduler-in-the-container-can-be-configured-at-once).

### What is not

Shared by every scheduler in the process:

| | |
|---|---|
| `ITypeLoader` | `UseTypeLoader<T>()` **replaces** it for all schedulers, and the renames `UseTypeLoader(configure)` declares apply to all |
| `ISchedulerRepository` | one per container, so `GetAllSchedulers` and the dashboard see every scheduler |
| `ISchedulerRegistry` | one per container; it answers for every registration in it |
| `IJobExecutionContextAccessor` | one per container; the firing it reports belongs to the asynchronous flow, which is inside at most one firing |
| `SystemTextJsonSerializerRegistry` | one per container by default, since the HTTP API and client serialize triggers without knowing their scheduler. A named scheduler *can* have its own — see below |
| `Meters` | built from the container's `IMeterFactory` |
| `DataSourceOptions` | named after the **data source**, not the scheduler, so several schedulers can share one |
| `QuartzHttpApiOptions`, `QuartzDashboardOptions` | one each per process. *Which* schedulers a caller reaches is per scheduler — see [Authorizing a tenant on its own scheduler](#authorizing-a-tenant-on-its-own-scheduler) — but what they may do there is not |

**Logging** is per container. Every scheduler's parts get the container's `ILoggerFactory`; a log line
names its tenant through the scope the scheduling loop opens, `quartz.scheduler.name` and
`quartz.scheduler.id` — the attribute names traces and metrics use. Filter or enrich on those.

**`LogProvider`** is process-wide static state: `SetLogProvider(loggerFactory)` sets it for the whole
process. It does not decide whether a scheduler logs; it reaches the types no container builds — a
listener or trigger you constructed, the static helpers, the jobs in `Quartz.Jobs`.

**The serializer registry** can be per scheduler. A named scheduler resolves its registry by key and
falls back to the container's, so `services.AddKeyedSingleton(schedulerName, registry)`, or the per-store
`UseSystemTextJsonSerializer(json => …)` callback, gives one tenant its own trigger and calendar
serializers. That changes what *that scheduler's job store* persists and reads. The HTTP API responses,
the dashboard and `Quartz.HttpClient` still read the container's registry, so register a custom type
they must render there too. See
[System.Text.Json serialization](packages/system-text-json.md#making-custom-serializers-visible-outside-the-job-store).

### Health checks per tenant

`AddQuartzHealthChecks` on a *scheduler's* builder checks that scheduler, named
`quartz-scheduler-<scheduler name>` by default so several can coexist:

<!-- snippet: sample_tenancy_health_checks -->
```csharp
builder.Services.AddQuartz("acme", q => q.AddQuartzHealthChecks(o => o.Tags.Add("tenant:acme")));
```
<!-- endSnippet -->

From the health-checks builder, `services.AddHealthChecks().AddQuartz("acme")` checks one named
scheduler and `AddQuartz()` checks the default one.

### Authorizing a tenant on its own scheduler

`QuartzHttpApiOptions.SchedulerAuthorizationPolicy` and `QuartzDashboardOptions.SchedulerAuthorizationPolicy`
each name a policy evaluated once per request against the scheduler the request is for. Quartz supplies
the resource, a `SchedulerResource` carrying the scheduler's name; the application writes the handler.
This is ASP.NET Core's resource-based authorization, so there is no Quartz callback type and no claim
name Quartz picks for you:

<!-- snippet: sample_tenancy_scheduler_authorization_handler -->
```csharp
// What "may act on this scheduler" means is the application's to decide, so the requirement itself
// says nothing and the handler says everything.
public sealed class SchedulerOwnerRequirement : IAuthorizationRequirement;

public sealed class SchedulerOwnerHandler : AuthorizationHandler<SchedulerOwnerRequirement, SchedulerResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SchedulerOwnerRequirement requirement,
        SchedulerResource resource)
    {
        string? tenant = context.User.FindFirst("tenant")?.Value;

        // Scheduler names are compared ignoring case everywhere else in Quartz, so compare them that
        // way here too - otherwise "Acme" and "acme" are the same scheduler and different tenants.
        if (string.Equals(tenant, resource.SchedulerName, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```
<!-- endSnippet -->

Register the policy, the handler and the two options; one handler serves both surfaces:

<!-- snippet: sample_tenancy_scheduler_authorization -->
```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SchedulerOwner", policy => policy.AddRequirements(new SchedulerOwnerRequirement()));

builder.Services.AddSingleton<IAuthorizationHandler, SchedulerOwnerHandler>();

// One policy, one handler, both surfaces: the API answers 403 for a scheduler the caller fails
// for, and the dashboard offers them only the schedulers they pass for.
builder.Services.AddQuartzHttpApi(options => options.SchedulerAuthorizationPolicy = "SchedulerOwner");
builder.Services.AddQuartzDashboard(options => options.SchedulerAuthorizationPolicy = "SchedulerOwner");
```
<!-- endSnippet -->

**On the HTTP API:**

- Every route with `{schedulerName}` is checked before the scheduler is looked up. A caller who fails gets
  `403` with problem details and cannot tell "not yours" from "no such scheduler"; `404` only answers a
  name the caller was allowed to ask about.
- `GET {ApiPath}/schedulers` names no scheduler, so it filters: a tenant sees only its own schedulers,
  registrations included.
- The check is authorization, not authentication: an anonymous caller gets whatever the policy says, a
  `403` when it refuses. Keep `RequireAuthorization()` on the mapped group if anonymous callers should
  get a `401` challenge first.

**On the dashboard:**

- The scheduler picker and the Schedulers page offer only schedulers the visitor passes for.
- A page opened on another scheduler renders a "not authorized" frame and reads nothing about it.
- The live-events hub refuses to subscribe a connection to that scheduler's group.
- `AuthorizationPolicy` decides who reaches the dashboard at all, this policy which schedulers they see,
  and `ReadOnly` what anyone may change.
- The frame is the dashboard's own layout; read
  [Standalone hosting is where this applies today](packages/dashboard.md#one-scheduler-at-a-time) before
  relying on it under a layout of your own.

`null` (the default) keeps the behaviour of earlier releases. Setting it in a container with no
authorization services fails at startup; a policy name no `IAuthorizationPolicyProvider` knows fails, as
elsewhere in ASP.NET Core, at the first request that uses it.

## Group per tenant

One scheduler; the tenant is the group half of every key.

<!-- snippet: sample_tenancy_group_keys -->
```csharp
JobKey job = new("nightly-report", tenantId);
TriggerKey trigger = new("nightly", tenantId);
```
<!-- endSnippet -->

Everything that takes a matcher is then tenant-scoped:

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
List<string> paused = await scheduler.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(tenantId));

// is a tenant suspended?
PagedResult<TriggerGroup> group = await scheduler.QueryTriggerGroups(
    new TriggerGroupQuery { Name = NameMatcher.NameEquals(tenantId), Take = 1 });
bool suspended = group.Items is [{ Paused: true }];
```
<!-- endSnippet -->

Pause state can be queried for trigger groups and job groups. The ADO store persists a paused job group in
`QRTZ_PAUSED_JOB_GRPS`, so `QueryJobGroups(new JobGroupQuery { Name = tenantId, Take = 1 })` works for a
tenant partitioned by job group. On either store, a paused trigger group or a paused job group starts a
later trigger in it paused.

Listeners take matchers too, so a per-tenant listener is one registration:

<!-- snippet: sample_tenancy_group_listener -->
```csharp
q.AddJobListener<AuditListener>(Matchers.Group<JobKey>(StringOperator.Equality, tenantId));
```
<!-- endSnippet -->

### What the group model does not partition

**Quartz authorizes a scheduler, never a group.** A policy is evaluated against a `SchedulerResource`
carrying a scheduler's name, so under this model every tenant's jobs, triggers and history are visible to
anyone who reaches the dashboard or the HTTP API. There is no group equivalent of
[`SchedulerAuthorizationPolicy`](#authorizing-a-tenant-on-its-own-scheduler), and a group matcher in a
query string is a filter a caller can change, not a boundary.

If tenants must not see each other there, give them schedulers, or put your own API in front of Quartz's
and never expose Quartz's. Scheduling, pausing, quotas and listeners are still isolated per group, and
most deployments never expose the dashboard to tenants.

### Per-tenant concurrency quotas

Execution groups cap how many threads a category of work may use. When the schedule has a tenant per
trigger group, the trigger group can stand in for the execution group, so a quota is one line per tenant
and no trigger changes:

<!-- snippet: sample_tenancy_execution_limits -->
```csharp
q.UseExecutionLimits(limits => limits
    .UseTriggerGroupWhenUnset()
    .ForGroup("acme", 8, ExecutionLimitScope.Cluster)          // a big tenant
    .ForGroup("initech", 2, ExecutionLimitScope.Cluster)
    .ForOtherGroups(1, ExecutionLimitScope.Cluster));          // everyone else gets one thread each
```
<!-- endSnippet -->

- **`ExecutionLimitScope.Cluster`** makes the number a quota: what every node sharing the job store may
  run **between them**, counted from the reservations the store holds in `QRTZ_FIRED_TRIGGERS`.
- **Without a scope**, each limit is per node: on three nodes, `ForGroup("acme", 8)` allows 24 concurrent
  Acme jobs. Use node scope for "this machine can take eight" and cluster scope for "this tenant is
  entitled to eight"; one set of limits can mix both.

`UseTriggerGroupWhenUnset()` changes only how a limit is looked up; the trigger still carries no execution
group and the store persists none.

- An explicit `ExecutionGroup` on a trigger always wins.
- `ForDefaultGroup` catches nothing, because nothing is ungrouped. Unlisted tenants fall under
  `ForOtherGroups`.
- Each unlisted group gets **its own** `ForOtherGroups` allowance: three unlisted tenants under
  `ForOtherGroups(1)` run three jobs, one each.
- `Unlimited(group)` differs from leaving a group out: an unlisted group falls back to `ForOtherGroups`,
  an unlimited one does not.

`SetExecutionLimits` / `GetExecutionLimits` change limits at runtime, from the next acquisition cycle;
`null` clears them. The call is per node whatever the scope: set a cluster-scoped quota on every node, or
configure it.

::: warning What a cluster-scoped quota does and does not promise

- The ceiling holds **within one acquisition round**. Acquisition takes no cluster lock by default, so
  several nodes acquiring at once can briefly overshoot, to at most `limit + (nodes − 1)`. The lock-free
  path is only taken when a round acquires one trigger — the ADO store takes the `TRIGGER_ACCESS` lock
  for more — so each node adds at most one. `AcquireTriggersWithinLock = true` makes it exact and
  serializes acquisition cluster-wide.
- It **fails closed**: the quota ledger and the work queue are the same database, so a node that cannot
  reach the store fires nothing. A database outage stops work; it does not remove the ceiling.
- A group held at its ceiling longer than `MisfireThreshold` (one minute by default) sends its backlog to
  misfire handling. Pair a tight quota with `MisfireInstruction.IgnoreMisfirePolicy` or a larger
  threshold. See [Execution Groups](tutorial/execution-groups.md#clustering-considerations).
:::

## Shared database

Every Quartz table has `SCHED_NAME` as the first primary-key column, and every statement filters on it.
Schedulers with different names share tables without seeing each other's rows; this is a property of the
schema, so no query can forget it.

`AdoJobStoreOptions.TablePrefix` (default `QRTZ_`) is per scheduler, so two tenants can have separate
table *sets* in one database:

<!-- snippet: sample_tenancy_table_prefix -->
```csharp
builder.Services.AddQuartz("acme", q => q.UsePersistentStore(s =>
{
    s.UseSqlServer(sharedConnectionString);
    s.ConfigureStore(o => o.TablePrefix = "ACME_QRTZ_");
}));
```
<!-- endSnippet -->

- **A different scheduler name is enough to isolate tenants.** Separate tables are a backup, restore or
  permissions decision.
- **The prefix must match the DDL.** Run the DDL with the prefix substituted; nothing derives one from
  the other.
- **A prefix naming tables that do not exist fails at startup.** `SchemaProvisioning.Validate` is the
  default and reports each missing or mis-prefixed table once, by name, telling you to run the schema
  scripts.
- **A prefix naming the *wrong* tables logs a warning.** Schema validation cannot catch it: the tables
  exist but are another scheduler's, so the scheduler starts, reports healthy and never sees its data.
  Each scheduler created records its database and table prefix, and one that shares a database with an
  existing scheduler but disagrees about the prefix logs a `Warning` naming both schedulers and both
  prefixes. Prefixes compare **ignoring case**, because every supported database folds an unquoted
  identifier to one case: `qrtz_` and `QRTZ_` are one table set.

::: tip Why that one is a warning and not an error
Separate table sets in one database are legal; the sample above asks for one. Quartz cannot tell a
deliberate `ACME_QRTZ_` from a mistyped `QRTZ2_`, and an error on a legitimate arrangement is worse than
silence. If the prefixes are meant to differ, filter the warning out on the
`Quartz.Configuration.SharedDatabaseValidator` category.

It only sees one container. Two processes, or two containers in one process, sharing a database are
invisible to each other, as is a database reached through a provider that reports neither a connection
string nor a `DbDataSource`. When it cannot tell, it stays quiet.
:::

::: warning
Two schedulers sharing a database with the **same** `SCHED_NAME` look exactly like two nodes of one
cluster to the schema. Schema validation does not catch it, and they will steal each other's triggers.
The duplicate-name check works only within one container; across processes, keeping names unique is up
to you.
:::

## Per-tenant services inside a job

A job needs *its* tenant's database, configuration and feature flags. The scheduler builds jobs from a DI
scope, and `ConfigureJobScope` prepares that scope before the job and its dependencies are constructed:

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

- **The hook is synchronous.** An asynchronous hook would be awaited, and the `ExecutionContext` restored
  afterwards would discard the `AsyncLocal<T>` values it set.
- **The job is created on the execution path**, not during initialization, so values set here flow into
  `Execute`.
- Callbacks combine rather than replace, and run in the order added.

The `TriggerFiredBundle` carries the whole firing to derive the tenant from — `Trigger.Key.Group`,
`JobDetail.Key.Group`, or a value from `Trigger.JobDataMap` — plus the `IScheduler` that fired it, which
is the tenant under the scheduler-per-tenant model.

::: tip
The context does not exist when the hook runs: it takes the job instance, which has not been built. To
seed *construction*, use the `AsyncLocal` above, or resolve a scoped holder object from
`scope.ServiceProvider` and populate it; the holder is easier to test and does not depend on execution
context flow. For code that reads the tenant when it is *used*, use `IJobExecutionContextAccessor`,
below.
:::

### Which scheduler's parts a job is built from

A **registered** job is built by the *container*, which resolves constructor parameters **unkeyed** and
does not know which scheduler is firing. A job on scheduler `acme` taking `IScheduler`,
`ISchedulerFactory`, `IJobStore`, `IThreadPool` or `IOptions<QuartzSchedulerOptions>` would get the
*default* scheduler's, or, in a container with only named schedulers, nothing.

So **a registered job may not take a scheduler's parts by constructor, and startup says so.** `AddJob<T>`,
`AddJob(type, …)`, `ScheduleJob<T>` and `AddJobType<T>` record the job type against their scheduler, and
validating that scheduler's options checks the public constructors of the type the container would build.
A [delegate job](tutorial/delegate-jobs.md)'s handler parameters are checked the same way:

```text
Job type ArchiveJob is registered on scheduler 'acme', and its constructor takes ISchedulerFactory
schedulerFactory — a part that belongs to one scheduler. A registered job type is built by the
container, which resolves constructor parameters without a scheduler's service key: what the job is
handed is the unkeyed registration — the default scheduler's — whichever scheduler the job belongs to,
and in a container holding only named schedulers there is no unkeyed registration to hand it. Read the
scheduler running the job from IJobExecutionContext.Scheduler, take IJobExecutionContextAccessor for
the firing it is part of, or register the job with AddJobType<ArchiveJob>(provider => ...) and resolve
the part by key inside that factory.
```

- **Every public constructor is checked**, not only the one chosen today: the container picks a
  constructor whose parameters it can all resolve, so an unrelated registration can change the pick.
- **`TimeProvider` is allowed**: a scheduler with no clock inherits the container's, and injecting a
  clock is what Quartz asks you to do.
- **A job type the container does not hold is not checked.** The job factory activates it through the
  scheduler-scoped provider, so its dependencies resolve to *its own* scheduler's parts. That covers a job
  scheduled at runtime with `ScheduleJob(jobDetail, …)` and one named only by an XML or JSON schedule.

Three alternatives; the first covers nearly every case. **`IJobExecutionContext.Scheduler` is the
scheduler running the fire**, however the job was built:

<!-- snippet: sample_tenancy_job_reads_its_scheduler -->
```csharp
public sealed class RotateTenantKeysJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // The scheduler running this fire, whichever tenant it belongs to. An injected
        // ISchedulerFactory would have been the default scheduler's.
        IScheduler mine = context.Scheduler;

        await mine.PauseTriggerGroups(
            GroupMatcher<TriggerKey>.GroupEquals(context.Trigger.Key.Group),
            cancellationToken);
    }
}
```
<!-- endSnippet -->

**Code that is not handed the context** — a scoped service, a repository three calls below `Execute` —
reads the firing from [`IJobExecutionContextAccessor`](#reading-the-firing-from-anywhere-in-it).

**A job that must be *constructed* with a scheduler's part** is registered with its own factory, which
resolves the part by key. Factory registrations are not checked, since the factory is where the key
goes:

<!-- snippet: sample_tenancy_job_built_by_its_scheduler -->
```csharp
builder.Services.AddQuartz("acme", q =>
{
    // A job the container builds may not take a scheduler's parts by constructor - startup
    // refuses one that does. The job that genuinely has to be given them is built here instead,
    // where the scheduler's key is known.
    q.AddJobType<ArchiveJob>(provider =>
        new ArchiveJob(provider.GetRequiredKeyedService<ISchedulerFactory>("acme")));

    q.AddJob<ArchiveJob>(j => j.WithIdentity("archive"));
});
```
<!-- endSnippet -->

### Reading the firing from anywhere in it

`IJobExecutionContextAccessor` gives code that is not the job — a scoped service, a logging enricher, a
repository — the firing it is part of:

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

`AddQuartz` registers it as a singleton. It exposes the whole `IJobExecutionContext`, not a tenant-shaped
projection, since Quartz has no tenant concept.

- **Set from when the context exists** (before trigger and job listeners are notified) **until the job
  is returned to the job factory.** Otherwise `null`: on the scheduling thread, in code that calls
  `ScheduleJob`, in an `ISchedulerListener` reacting to a scheduling call.
- **Never another firing's.** It flows with the `ExecutionContext`, not the thread; a pooled thread
  picking up other work inherits nothing.
- **Survives `await` and `Task.Run`; ends with the firing.** Work left running past `Execute` (a detached
  `Task.Run`, an unawaited continuation) reads `null` once the execution ends, when the job's DI scope is
  disposed. `ExecutionContext.SuppressFlow` hides it, as it hides every ambient value.
- **No setter**, so it cannot be left pointing at a finished firing. Substitute the interface in tests.

It does **not** replace `ConfigureJobScope`: the context does not exist while the job is constructed, so
anything needing the tenant *at construction time* — a `DbContext` given a tenant connection string in its
constructor — still gets it from the hook.

For more involved cases, such as resolving jobs from a tenant-owned container, implement `IJobFactory`
and register it with `UseJobFactory<T>()`, or derive from `MicrosoftDependencyInjectionJobFactory` and
override `protected virtual void ConfigureScope(...)`. An override that does not call `base` replaces the
delegate.

Per-fire options are a snapshot: if tenants can be reconfigured while the process runs, read the tenant's
configuration *inside* the hook or the job, not once at startup.

## Adding a tenant while the process is running

`AddQuartz(name, …)` registers against `IServiceCollection`, which is closed once the container is built.
`ISchedulerRuntime` takes a name the container never saw, builds a scheduler for it in a container of its
own (its own thread pool, job store, connection provider, plugins and listeners), binds it into the same
`ISchedulerRepository` as every other scheduler, and starts it:

<!-- snippet: sample_tenancy_runtime_onboarding -->
```csharp
ISchedulerRuntime runtime = app.Services.GetRequiredService<ISchedulerRuntime>();

IScheduler tenant = await runtime.Add(tenantId, q =>
{
    q.UsePersistentStore(s => s.UseSqlServer(connectionStrings[tenantId]));
    q.UseDefaultThreadPool(maxConcurrency: 5);
    q.AddJob<NightlyReportJob>(j => j.WithIdentity("nightly"));
    q.AddTrigger<NightlyReportJob>(t => t.WithCronSchedule("0 30 2 * * ?"));
});
```
<!-- endSnippet -->

- The callback gets the same `IQuartzBuilder` as an `AddQuartz(name, …)` callback, applied in the same
  order, and `ConfigureAllQuartzSchedulers` applies to it too.
- Everything that reads the repository sees the tenant: `GetAllSchedulers`,
  `ISchedulerFactory.LookupScheduler`, the HTTP API, and the dashboard, which lists it as
  `SchedulerOrigin.Runtime`.

Offboarding is `Remove`:

<!-- snippet: sample_tenancy_offboarding -->
```csharp
ISchedulerRuntime runtime = app.Services.GetRequiredService<ISchedulerRuntime>();

// Shuts the scheduler down, unbinds it from the repository and releases the container it was
// built in. Nothing is deleted from the store: the tenant's rows stay where they are.
bool removed = await runtime.Remove(tenantId, waitForJobsToComplete: true);
```
<!-- endSnippet -->

It shuts the scheduler down, unbinds it and releases its container. A name this runtime did not add
returns `false` instead of throwing, so removing twice, or removing a tenant something else shut down,
reports what happened.

::: warning Removing a tenant deletes none of its data
The tenant's rows under its `SCHED_NAME` stay, and its `SCHEDULER_STATE` row expires like a stopped
node's. Deleting a tenant's data is the application's decision. A tenant added again under the same name
picks up everything left behind.
:::

### What `Add` refuses

A refused name throws a `SchedulerConfigException` saying which rule and what to do. Only the caller sees
it, and nothing is left behind — no half-built scheduler in the repository or the listing.

| Refused | Because |
|---|---|
| a name `AddQuartz(name, …)` registered | its parts are already registered under that name; build it with `GetRequiredKeyedService<ISchedulerFactory>(name).GetScheduler()` |
| the default scheduler's `InstanceName` | the same, for the registration with no service key |
| a name already added at runtime | `Remove` it first; a scheduler's thread pool and job store cannot be replaced underneath it |
| a name already bound in the repository | a scheduler bound by hand, or by `AddQuartzHttpClient`, occupies the name like a registration |
| any name, once the host is stopping | it would be created after the shutdown meant to stop it |

### Restarting a scheduler

`Restart` shuts a scheduler down and builds a new one from its recipe: for a tenant this runtime added, or
one `AddQuartz(name, …)` registered (the recipe is recorded at registration). The new scheduler is a new
set of instances sharing only the name, since shut-down parts refuse work.

<!-- snippet: sample_tenancy_restart -->
```csharp
ISchedulerRuntime runtime = app.Services.GetRequiredService<ISchedulerRuntime>();

try
{
    // The new scheduler's container is built first, so a recipe that no longer works leaves the
    // running one alone; then the old one is shut down waiting for its jobs; then the new one is
    // created and started, which is when its store is initialised and its recovery runs.
    IScheduler tenant = await runtime.Restart(tenantId, new SchedulerRestartOptions
    {
        DrainTimeout = TimeSpan.FromMinutes(2)
    });
}
catch (SchedulerRestartException e)
{
    // The old scheduler is down and the new one was never built, which is deliberate: its first
    // act would have been a recovery sweep over work that is still running. Nothing has to be
    // undone - ask again once the jobs have finished, and until then the tenant is listed with
    // no status.
    logger.LogWarning(
        e,
        "Tenant {Tenant} still has {Count} job(s) running; restarting again shortly",
        tenantId,
        e.JobsStillExecuting);
}
```
<!-- endSnippet -->

1. **Build.** A recipe that no longer works — a connection string gone from configuration, a setting a
   later version rejects — fails with `SchedulerConfigException` and leaves the running scheduler alone.
   Building opens no connection and starts no thread.
2. **Drain.** The old scheduler shuts down waiting for its jobs, always. On start, a persistent scheduler
   sweeps its whole `SCHED_NAME` for crash recovery — acquired and blocked triggers back to waiting,
   `COMPLETE` triggers and every fired-trigger row deleted — with no instance-id filter, so it must not
   start beside a running job.
3. **Create.** The store is initialized, the schema checked, declared jobs and triggers applied, and the
   recovery sweep run.

`SchedulerRestartOptions.DrainTimeout` bounds the drain: thirty seconds by default, or
`Timeout.InfiniteTimeSpan` to wait until the jobs finish or the cancellation token fires. On expiry it
throws `SchedulerRestartException`: the old scheduler is **shut down**, the new one **never built**, and
the tenant is listed with `Status: null`. Nothing needs undoing. Retry once the work has finished; the
next attempt waits for the abandoned generation, and starts the new scheduler if the old one was running.

To make a drain finish, use [`ShutdownJobInterruption`](configuration/reference.md#scheduler) (asks
running jobs to stop on shutdown) or `[JobTimeout]` with `AddJobTimeout`. Without either, a job that
ignores its cancellation token outlives any deadline.

::: warning A job that outlives the drain is at-least-once
It keeps running on the old generation and the restart fails. Restart again after it finishes, and the
new generation's recovery re-fires anything marked for recovery, as after a crash: to recovery, a
restart is a node failure.
:::

What a restart changes:

- **Clustering.** With a fixed `InstanceId`, the new generation finds its own `SCHEDULER_STATE` row and
  recovers nothing. With `AUTO` it takes a new id, and the old row expires like a stopped node's, to be
  recovered by a peer or the new generation.
- **Declared content is re-applied** under the recipe's `OverwriteExistingData` (`true` by default),
  resetting a declared trigger's stored state. Set `Scheduling.IgnoreDuplicates` to avoid that.
- **`RAMJobStore` keeps only what the recipe declares**; anything scheduled at runtime is lost.
- **Configuration beside `AddQuartz` is not in the recipe.** A
  `services.Configure<QuartzSchedulerOptions>("acme", …)`, a keyed registration on the application's
  collection, or an `AddQuartzHostedService("acme", …)` is not replayed. Move such lines into the
  `AddQuartz("acme", …)` callback.

| Refused | Because |
|---|---|
| the default scheduler | its parts are the container's unkeyed registrations, so there is no recipe. Register it with `AddQuartz("name", …)` to make it restartable; `Standby()`/`Start()` pause and resume it |
| a recipe that supplies a part as an instance | `UseJobStore(IJobStore)`, `UseThreadPool(IThreadPool)`, `UseJobFactory(instance)` and `UseInstanceIdGenerator(instance)` would reuse the part being shut down. Register a type or factory, e.g. `UseJobStore<T>()` or `UseJobStore(provider => new …)`. Refused before anything is built |
| a factory that returns the running scheduler's part | e.g. `UseJobStore(provider => sharedInstance)`, detected by building the next generation. **A factory must return a new instance per generation**; a refused generation's container is released with what the factory returned |

- An unknown name throws `SchedulerNotFoundException`.
- A known name whose scheduler is already shut down (by hand, by the host, or by a failed drain) is
  started again.
- `Remove` offboards a tenant; `Restart` keeps it and replaces its instances.

### Runtime tenants and the application's container

- **`IQuartzBuilder.Services` is the tenant's own collection**; what the callback registers there goes
  away with the tenant. It may not call `AddJobType`: jobs are built by the *application's* container,
  where their dependencies are, so a recipe that tries is refused. Register the job type in the
  application's container and schedule it with `AddJob<T>(…)`.
- **`[FromKeyedServices("acme")] IScheduler` cannot reach a runtime tenant**; the container never saw the
  name. Use the scheduler `Add` returned, `ISchedulerFactory.LookupScheduler(name)` or
  `ISchedulerRepository.Lookup(name)`.
- **Register a tenant's health check at build time** with `services.AddHealthChecks().AddQuartz("acme")`;
  a built container takes no new checks. It reports unhealthy until the tenant is added, then finds it
  through the repository. `AddQuartzHostedService("acme", o => o.WaitForJobsToComplete = true)` works the
  same way, applied when the host stops to whichever tenant runs under that name — which shuts a runtime
  tenant down inside the graceful shutdown window rather than at container disposal, after it.

### Without an application container

`QuartzSchedulerBuilder` builds a scheduler from its own container at any time, and
`ISchedulerRepository.Bind` makes it visible to `GetAllSchedulers`, the dashboard and the HTTP API. This
is for a process with no application container; `ISchedulerRuntime` is built on it.

<!-- snippet: sample_tenancy_standalone_onboarding -->
```csharp
StandaloneSchedulerFactory tenantFactory = QuartzSchedulerBuilder
    .Create(q => q
        .ConfigureScheduler(o => o.InstanceName = tenantId)
        .UsePersistentStore(s => s.UseSqlServer(connectionStrings[tenantId])))
    .Build();

IScheduler tenant = await tenantFactory.GetScheduler();
await tenant.Start();

// Keep the factory: it owns the container, and it is the only handle that can shut the tenant
// down again.
tenantFactories[tenantId] = tenantFactory;
app.Services.GetRequiredService<ISchedulerRepository>().Bind(tenant);
```
<!-- endSnippet -->

- Keep the factory as long as the tenant exists (`tenantFactories` is a dictionary by tenant id).
  `BuildScheduler()` discards it, which suits only a scheduler that lives as long as the process.
- *You* start the scheduler and dispose the factory; the hosted service does not.
- Its jobs resolve from its own container, unless you give it an `IJobFactory` that bridges to the
  application's.
- Health checks registered at startup do not cover it.

`ISchedulerRuntime.Add` handles the last three, so prefer it wherever there is an application container.

`Bind` refuses a duplicate **(name, instance id)** pair, not a duplicate name, so proxies to several
nodes of one cluster can coexist. Two non-clustered tenants with one name still collide, since both have
the instance id `NON_CLUSTERED`; give each tenant a distinct `InstanceName`.

To offboard, dispose the factory (which shuts the scheduler down and disposes its container) and unbind
it, so the repository stops listing it at once:

<!-- snippet: sample_tenancy_offboarding_standalone -->
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
`StandaloneSchedulerFactory.DisposeAsync` shuts down with `waitForJobsToComplete: false`, cutting a
tenant's jobs short — the same default as `IScheduler.Shutdown()` and `QuartzHostedServiceOptions`. To
wait, call `await scheduler.Shutdown(waitForJobsToComplete: true)` first, as above; the disposal then
only releases the container.
:::

::: warning Unbinding is not offboarding
`Remove` makes a tenant invisible; only disposal stops it. Unbinding without disposing removes the tenant
from `GetAllSchedulers`, the dashboard and the HTTP API while it keeps firing for the rest of the
process's life, with nothing able to reach it. Because disposal shuts the scheduler down, the two steps
above are safe in either order.
:::

Under the group-per-tenant model, onboarding is a `ScheduleJob` call and none of this applies.

## Limits

What multi-tenant deployments ask for and Quartz does not provide. [Tenancy Patterns](../tenancy-patterns.md#what-quartz-net-does-not-give-you)
covers the same list for both 3.x and 4.x.

- **A cluster-wide concurrency ceiling is exact only with `AcquireTriggersWithinLock = true`**, which
  serializes acquisition for every group, limited or not. Otherwise it can briefly overshoot by up to
  `nodes − 1`. No setting gives both — see
  [What a cluster-scoped quota does and does not promise](#per-tenant-concurrency-quotas).
- **No rate limiting.** Execution limits cap *concurrency*, not throughput. "100 jobs an hour" cannot be
  expressed; build it into the job or what it calls. Often "at most four at once" is the real
  requirement, and Quartz can enforce that.
- **No per-operation policy.** A tenant can be held to its own scheduler on both surfaces, but what it may
  *do* there is one process-wide setting, the dashboard's `ReadOnly` flag. "acme may look, globex may
  act" or "may pause but not delete" is not expressible. An operation requirement could later join the
  `SchedulerResource` policy without a new option; it is not built.
- **A scheduler is never restarted in place.** `GetScheduler()` throws rather than reviving a shut-down
  thread pool and job store, because the container owns their lifetimes. `Standby()` / `Start()` pause
  and resume; [`ISchedulerRuntime.Restart`](#restarting-a-scheduler) builds new parts from the recipe.
- **A scheduler per tenant costs** a scheduling loop on its own idle timer, a thread pool, and with a
  persistent store a connection pool and a cluster check-in — see [Choosing a model](#choosing-a-model).

## Observability

Traces and metrics carry the scheduler name *and* id, so per-tenant dashboards work under the
scheduler-per-tenant model with no extra instrumentation, and a clustered tenant can be read one node at a
time.

- **Traces.** `quartz.scheduler.name` and `quartz.scheduler.id` are on every span. The execution span adds
  `quartz.job.group`, `quartz.job.name`, `quartz.trigger.group`, `quartz.trigger.name` and
  `quartz.fire.instance.id`.
- **Metrics.** `quartz.scheduler.name` and `quartz.scheduler.id` are on **every** measurement of every
  instrument, so a per-tenant dashboard is a `group by`. `quartz.job.execution.active` and
  `quartz.job.execution.duration` add `quartz.trigger.group`, `quartz.trigger.name`, `quartz.job.group`
  and `quartz.job.name`. All instruments and their attributes are on the
  [OpenTelemetry page](packages/opentelemetry-integration.md#metrics).

Under the group-per-tenant model, `quartz.trigger.group` and `quartz.job.group` **are** the tenant; group
by those instead. Use the raw tenant id as the group, not a decorated string.

::: warning Cardinality
`quartz.job.name` and `quartz.trigger.name` are per job and per trigger. Multiplied by tenants, a metrics
backend can get a series per tenant per trigger. Drop the name tags in a view unless you need them.
:::

The tag names are public constants — `ActivityTags.SchedulerName`, `ActivityTags.TriggerGroup` and the
rest — for views and filters.

## See also

- [Multiple Schedulers](packages/multiple-schedulers.md) — naming and keying schedulers
- [Execution Groups](tutorial/execution-groups.md) — per-node and cluster-wide thread limits in full
- [Querying Jobs and Triggers](tutorial/querying-jobs-and-triggers.md) — group-filtered listings
- [Clustering](tutorial/advanced-enterprise-features.md) — what a shared database gives you
- [Dashboard](packages/dashboard.md) — the Schedulers page, read-only mode, and how the policy above gates the UI
- [HTTP API](packages/http-api.md#authorizing-per-scheduler) — the same policy on the other surface, and why it answers 403 before 404
- [Migration Guide](migration-guide.md) — including why a shut-down scheduler is replaced rather than revived
