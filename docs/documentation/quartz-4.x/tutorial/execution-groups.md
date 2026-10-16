---
title: 'Execution Groups'
---

Execution groups limit how many threads a category of job can use at once, so resource-heavy jobs do not
starve lightweight ones of threads.

## Concepts

An **execution group** is an optional tag on a trigger that names its job's resource needs, such as
`"batch-jobs"`, `"high-cpu"`, `"large-ram"` or `"reports"`.

An **execution limit** says how many threads a group may use:

| Limit | Means |
|---|---|
| a positive integer (e.g. `5`) | at most that many concurrent executions |
| `0` | the group never runs |
| none configured | unlimited |

Each limit is counted against an `ExecutionLimitScope`:

| Scope | The number is | Use it for |
| -- | -- | -- |
| `Node` (the default) | what *this* node may run at once; each node enforces its own copy, so N nodes can run N times the number | different hardware: a batch node and an API node want different numbers |
| `Cluster` | what *all nodes sharing the job store* may run between them | quotas: "this tenant gets eight threads", however many nodes are up |

One set of limits can mix both scopes. A limit with no scope is node-scoped, as execution limits always
were.

A group also **routes**, from 4.6: a node can serve or refuse groups, so a `gpu` trigger runs only on a
node that has a GPU. See [Routing: which node serves a group](#routing-which-node-serves-a-group).

## Setting execution groups on triggers

Use `TriggerBuilder.WithExecutionGroup()`:

<!-- snippet: sample_execution_groups_trigger -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithExecutionGroup("batch-jobs")
    .WithCronSchedule("0 0 2 * * ?")
    .Build();
```
<!-- endSnippet -->

Triggers without an execution group (`null`) use the default behavior. All triggers of a job should share
the same execution group.

Move a stored trigger between groups without rescheduling it:

<!-- snippet: sample_execution_groups_update_trigger -->
```csharp
await scheduler.UpdateTriggerDetails(
    trigger.Key,
    new TriggerDetailsUpdate().WithExecutionGroup("batch-jobs"));

// pass null to take the trigger out of every group
await scheduler.UpdateTriggerDetails(
    trigger.Key,
    new TriggerDetailsUpdate().WithExecutionGroup(null));
```
<!-- endSnippet -->

The new group applies from the next acquisition cycle; a running job keeps counting against the group it
was acquired under.

Reserved names, which cannot be execution groups:

| Name | Used for |
|---|---|
| `*` | the "other groups" catch-all limit |
| `_` | the property-config alias for the default (ungrouped) triggers |
| `null` (case-insensitive) | same alias as `_` |

Empty or whitespace-only strings become `null` (no group). A name may carry `{key}` placeholders; see
[Per-tenant limits](#per-tenant-limits).

### Manual and startup firings

`TriggerJob` builds a trigger of its own, and so do the dashboard's **Trigger now**, the HTTP API, an agent,
a job chain's next link and `RunAtStartup`. From 4.6 that trigger takes the first group that applies:

| Order | Group | Skipped when |
|---|---|---|
| 1 | the job type's `[ExecutionGroup]`; placeholders read the job's `JobDataMap` overlaid by the firing's | no attribute, a placeholder with no value, or a job type this node cannot load |
| 2 | the one group every trigger of the job carries | no triggers, two groups, or an ungrouped trigger |
| 3 | none | — |

Limits and routing then apply as for any other trigger. A template with no value never fails the firing.

## Configuring execution limits

### Via properties

```properties
quartz.executionLimit.batch-jobs = 2
quartz.executionLimit.high-cpu = 3
quartz.executionLimit._ = 10
quartz.executionLimit.* = 5

quartz.clusterExecutionLimit.tenant-acme = 8
quartz.clusterExecutionLimit.tenant:* = 2
```

| Key | Meaning |
|-----|---------|
| `quartz.executionLimit.batch-jobs` | At most 2 concurrent "batch-jobs" triggers **on this node** |
| `quartz.executionLimit.high-cpu` | At most 3 concurrent "high-cpu" triggers on this node |
| `quartz.executionLimit._` (underscore) | At most 10 concurrent triggers with no execution group, on this node |
| `quartz.executionLimit.*` (asterisk) | Default limit of 5 for any group not explicitly listed |
| `quartz.clusterExecutionLimit.tenant-acme` | At most 8 concurrent "tenant-acme" triggers **across the whole cluster** |
| `quartz.clusterExecutionLimit.tenant:*` | At most 2 for **each** group starting with `tenant:`, across the cluster. See [Per-tenant limits](#per-tenant-limits) |

`quartz.clusterExecutionLimit.*` takes the same group keys, including `_` and `*`, and the same values as
`quartz.executionLimit.*`; only the scope differs. It is a separate prefix because every key under
`quartz.executionLimit` is a group name and every value a count, leaving no spelling for a scope.

| Value | Means |
|---|---|
| `unlimited`, `none` or `null` | no restriction, the same as not listing the group; takes no scope |
| `0` | completely forbidden |

### Via `appsettings.json`

The same keys, as sections:

```json
{
  "Quartz": {
    "ExecutionLimit": {
      "batch-jobs": 2,
      "_": 10,
      "*": 5
    },
    "ClusterExecutionLimit": {
      "tenant-acme": 8,
      "tenant:*": 2
    }
  }
}
```

* A group name keeps its case: `"Reports"` limits the group `Reports`.
* A prefix key is written as it reads. `"tenant:*": 2` and `"tenant": { "*": 2 }` are the same key.
* As an environment variable, where the platform allows `*` in a name: `Quartz__ClusterExecutionLimit__tenant__*=2`.

### Via dependency injection

<!-- snippet: sample_execution_groups_dependency_injection -->
```csharp
services.AddQuartz(q =>
{
    q.UseExecutionLimits(limits =>
    {
        limits.ForGroup("batch-jobs", maxConcurrent: 2);                        // per node
        limits.ForGroup("high-cpu", maxConcurrent: 3);                          // per node
        limits.ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster);         // per cluster
        limits.ForDefaultGroup(maxConcurrent: 10);
        limits.ForOtherGroups(maxConcurrent: 5);
    });
});
```
<!-- endSnippet -->

`ForGroup`, `ForGroupsWithPrefix`, `ForDefaultGroup` and `ForOtherGroups` take an optional trailing
`ExecutionLimitScope`, defaulting to `Node`.

For limits that come from a service, such as per-tenant quotas the application configures, use the
overload that receives the container. The callback runs when the scheduler is built, after options are
bound, post-configured and validated:

<!-- snippet: sample_execution_groups_from_options -->
```csharp
services.AddQuartz(q => q.UseExecutionLimits((serviceProvider, limits) =>
{
    TenantQuotaOptions quotas = serviceProvider.GetRequiredService<IOptions<TenantQuotaOptions>>().Value;

    foreach ((string tenant, int maxConcurrent) in quotas.PerTenant)
    {
        limits.ForGroup(tenant, maxConcurrent, ExecutionLimitScope.Cluster);
    }
}));
```
<!-- endSnippet -->

### Via scheduler API at runtime

<!-- snippet: sample_execution_groups_set_at_runtime -->
```csharp
await scheduler.SetExecutionLimits(
    ExecutionLimitsBuilder.Create()
        .ForGroup("batch-jobs", 2)
        .ForDefaultGroup(10)
        .ForOtherGroups(5)
        .Build());
```
<!-- endSnippet -->

`ExecutionLimitsBuilder` is mutable; `ExecutionLimits`, which `Build()` returns and the scheduler reads,
is not, so limits cannot change under the scheduler thread while it acquires triggers.

### Letting the trigger group stand in

When a schedule already partitions work by trigger group (a group per tenant, per subsystem), use
`UseTriggerGroupWhenUnset()` instead of tagging each trigger with an execution group of the same name:

<!-- snippet: sample_execution_groups_trigger_group_when_unset -->
```csharp
await scheduler.SetExecutionLimits(
    ExecutionLimitsBuilder.Create()
        .UseTriggerGroupWhenUnset()
        .ForGroup("tenant-a", 4)   // names a trigger group here, because none of its triggers name one
        .ForOtherGroups(2)         // every other tenant gets two
        .Build());
```
<!-- endSnippet -->

A trigger with no execution group is then limited as though its group were its `TriggerKey.Group`.

* **An explicit execution group always wins.** The derivation only fills a gap.
* **Nothing is persisted differently.** `ITrigger.ExecutionGroup` still reads `null`, and the store still
  writes `null` to `EXECUTION_GROUP`. The rule applies only where a limit is evaluated: the scheduler
  thread's in-flight counting and both job stores' acquisition filters. Turning it off changes only how
  limits are read.
* **`ForDefaultGroup` stops applying.** No trigger is ungrouped, so they fall under
  `ForGroup`/`ForOtherGroups`. Exception: a trigger whose group is spelled like a reserved name (`*`, `_`,
  `null`) stays ungrouped.
* It is code-only. There is no `quartz.executionLimit.*` property for it, because every key under that
  prefix is a group name.

Read limits back with `TryGetLimit(group, out int? maxConcurrent)`, or enumerate `Groups`. Each entry's
`Group` is an `ExecutionGroupScope`, one of four cases: `Default` (triggers with no execution group),
`OtherGroups` (the catch-all), `Named(name)` and `GroupsWithPrefix(prefix)`, so no sentinel strings are
involved. Its `Scope` says which scope the number is counted in:

<!-- snippet: sample_execution_groups_read_limits -->
```csharp
ExecutionLimits? limits = await scheduler.GetExecutionLimits();
foreach (ExecutionGroupLimit limit in limits?.Groups ?? [])
{
    string group = limit.Group.IsDefault ? "(no group)"
        : limit.Group.IsOtherGroups ? "(other groups)"
        : limit.Group.IsPrefix ? $"(each group starting with {limit.Group.Prefix})"
        : limit.Group.Name!;
    Console.WriteLine($"{group}: {limit.MaxConcurrent?.ToString() ?? "unlimited"} per {limit.Scope}");
}

limits?.TryGetLimit(ExecutionGroupScope.Named("batch-jobs"), out int? batchLimit);
```
<!-- endSnippet -->

Limits take effect on the next trigger acquisition cycle. Pass `null` to clear all limits:

<!-- snippet: sample_execution_groups_clear_limits -->
```csharp
await scheduler.SetExecutionLimits(null);
```
<!-- endSnippet -->

### The `*` in configuration keys, and the other `*`

Configuration uses `_` (or `null`) for the default group and `*` for the catch-all. Quartz's other `*`,
in preferred-node pinning, means something else:

| Where it appears | What `*` means there | Typed reading |
|---|---|---|
| `quartz.executionLimit.*` or `quartz.clusterExecutionLimit.*` key / execution-limits HTTP body | The catch-all limit applied to any *named* group without a limit of its own (never to ungrouped triggers) | `ExecutionGroupScope.OtherGroups` |
| After a prefix in the same key, `quartz.executionLimit.tenant:*` / `"tenant:*"` | The limit each group starting with `tenant:` gets | `ExecutionGroupScope.GroupsWithPrefix("tenant:")` |
| A trigger row's preferred-node column | An automatic pin no node has claimed yet — the trigger runs anywhere until one node fires it first and keeps it | `PreferredNode.Auto` |

In both places `*` is reserved: a trigger cannot have `*` as its execution group, and a node cannot have
`*` as its scheduler instance id.

## Per-tenant limits

To give every tenant its own allowance without listing the tenants, put each tenant's work in a group
named after it and limit the family by prefix:

<!-- snippet: sample_execution_groups_per_tenant_prefix -->
```csharp
q.UseExecutionLimits(limits => limits
    .ForGroupsWithPrefix("tenant:", 2, ExecutionLimitScope.Cluster) // two for each tenant
    .ForGroup("tenant:vip", 8, ExecutionLimitScope.Cluster));       // a group's own limit wins
```
<!-- endSnippet -->

Which limit governs a group, `tenant:acme` for example:

| Order | Limit | Example |
|---|---|---|
| 1 | the group's own | `ForGroup("tenant:acme", 8)` or `Unlimited("tenant:acme")` |
| 2 | the longest prefix the group starts with | `ForGroupsWithPrefix("tenant:", 2)` |
| 3 | the catch-all | `ForOtherGroups(5)` |

* **Each group gets the limit on its own.** `tenant:acme` and `tenant:initech` run two each, not two
  between them.
* **A named group beats a prefix of the same text.** With `ForGroup("tenant:", 1)` and
  `ForGroupsWithPrefix("tenant:", 2)`, the group `tenant:` runs one; `tenant:acme` runs two.
* Triggers with no execution group never match a prefix.
* Node and cluster scope count as for any limit.
* A prefix takes a count: it cannot be unlimited, and it does not end with `*`.
* `ForGroup` and `Unlimited` refuse a name ending in `*`, because that is how configuration spells a
  prefix.

| Where | Spelling |
|---|---|
| Properties | `quartz.executionLimit.tenant:* = 2`, `quartz.clusterExecutionLimit.tenant:* = 2` |
| `appsettings.json` | `"ClusterExecutionLimit": { "tenant:*": 2 }`. See [Via `appsettings.json`](#via-appsettings-json) |
| HTTP API body, `HttpScheduler` | the key `"tenant:*"` |
| Reading limits back | `ExecutionGroupScope.GroupsWithPrefix("tenant:")`: `IsPrefix`, `Prefix` |
| Dashboard | a "groups starting with 'tenant:'" row; each tenant's row says `(tenant:*)` beside the limit it inherited |

### Naming the group after the tenant

`WithExecutionGroup` accepts `{key}` placeholders. `Build()` resolves them from the trigger's own
`JobDataMap`, the moment it resolves `H` in a cron expression, and the trigger stores the resolved name:

<!-- snippet: sample_execution_groups_per_tenant_template -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .ForJob(job)
    .UsingJobData("TenantId", tenantId)
    .WithExecutionGroup("tenant:{TenantId}")   // stored as "tenant:acme"
    .StartNow()
    .Build();
```
<!-- endSnippet -->

| Rule | Behaviour |
|---|---|
| Values come from | the trigger's `JobDataMap`; not the job's, which the builder does not have |
| Missing or `null` key | `Build()` throws `FormatException` naming the key |
| `{{`, `}}` | a literal `{`, `}` |
| Unclosed `{`, lone `}`, empty `{}` | `WithExecutionGroup` throws `ArgumentException` |
| Resolves to blank or a reserved name | `Build()` throws `FormatException` |
| Formatting | invariant culture |
| `GetTriggerBuilder()` | rebuilds the stored name as written |
| Scheduling files, `TriggerDetailsUpdate`, the HTTP API | take the name as written; no placeholders |

To declare the group once for a job type, use `[ExecutionGroup]`. Its placeholders read the trigger's
`JobDataMap` too:

<!-- snippet: sample_execution_groups_per_tenant_attribute -->
```csharp
[ExecutionGroup("tenant:{TenantId}")]
public sealed class TenantReportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
```
<!-- endSnippet -->

<!-- snippet: sample_execution_groups_per_tenant_attribute_trigger -->
```csharp
ITrigger trigger = TriggerBuilder.Create<TenantReportJob>()
    .ForJob("tenant-report")
    .UsingJobData("TenantId", tenantId)   // the attribute's {TenantId}
    .StartNow()
    .Build();                             // stored as "tenant:acme"
```
<!-- endSnippet -->

| Trigger built by | `[ExecutionGroup]` |
|---|---|
| `TriggerBuilder.Create<TJob>()`, `q.AddTrigger<TJob>`, `q.ScheduleJob<TJob>`, `[CronTrigger]`, `[SimpleTrigger]` | applied; placeholders read the trigger's `JobDataMap` |
| `ScheduleJob<TJob, TInput>(input, …)` | applied when it has no placeholders; with placeholders, the call throws `FormatException` unless `OneOffJobOptions.ExecutionGroup` is set |
| `TriggerJob`, **Trigger now**, `RunAtStartup` | applied; placeholders read the job's and the firing's data. See [Manual and startup firings](#manual-and-startup-firings) |
| `TriggerBuilder.Create()`, for `IJob` | not read |

* `WithExecutionGroup` or `OneOffJobOptions.ExecutionGroup` wins over the attribute.
  `WithExecutionGroup(null)` opts one trigger out.
* The attribute is inherited from a base class, and not read from interfaces.
* A trigger built before the attribute was added keeps the group it stored.

For a one-off, name the tenant at the call site. `OneOffJobOptions.ExecutionGroup` is a name, stored as
written; a one-off trigger's job data is the input, stored whole, so there is nothing for a placeholder
to read:

<!-- snippet: sample_execution_groups_per_tenant_one_liner -->
```csharp
await scheduler.ScheduleJob<TenantExportJob, TenantExport>(
    export,
    TimeSpan.FromMinutes(5),
    new OneOffJobOptions { ExecutionGroup = $"tenant:{export.TenantId}" },
    cancellationToken);
```
<!-- endSnippet -->

### Many tenants

Each tenant is counted on its own. The scheduler thread drops a group's count when it reaches zero, so
memory follows the tenants running now, not every tenant ever seen. A cluster-scoped prefix is counted by
the same `QRTZ_FIRED_TRIGGERS` aggregate as any cluster-scoped limit.

::: tip The optional index applies here too
If you run cluster-scoped tenant limits *and* routinely hold four figures of work in flight, the covering
index from [Cluster-scoped limits](#cluster-scoped-limits) speeds up the aggregate. It is not in the
standard schema, for the write cost it adds to every firing:

```sql
CREATE INDEX IDX_QRTZ_FT_EG_TG ON QRTZ_FIRED_TRIGGERS (SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP);
```

:::

## Rate limiting

Execution limits cap how many firings run *at once*, not how many *start* per hour. To cap starts per
window, register a [job middleware](job-execution-middleware.md) that waits for a
`System.Threading.RateLimiting` permit before it calls the job:

<!-- snippet: sample_execution_groups_rate_limit_middleware -->
```csharp
public sealed class RateLimitMiddleware(PartitionedRateLimiter<IJobExecutionContext> limiter) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // The firing's token, so interrupting the firing ends the wait and the job does not run.
        using RateLimitLease lease = await limiter.AcquireAsync(context, permitCount: 1, cancellationToken);

        if (!lease.IsAcquired)
        {
            // The limiter's queue is full. A failure, so the trigger's retry policy decides what follows.
            throw new JobExecutionException($"No start permit for execution group '{context.Trigger.ExecutionGroup}'.");
        }

        await next(context, cancellationToken);
    }
}
```
<!-- endSnippet -->

Give it a limiter partitioned by execution group, and an execution limit for the same groups:

<!-- snippet: sample_execution_groups_rate_limit_register -->
```csharp
// 100 starts an hour for each tenant's group, counted in ten-minute segments
PartitionedRateLimiter<IJobExecutionContext> limiter = PartitionedRateLimiter.Create<IJobExecutionContext, string>(context =>
    context.Trigger.ExecutionGroup is { } group && group.StartsWith("tenant:", StringComparison.Ordinal)
        ? RateLimitPartition.GetSlidingWindowLimiter(group, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 6,
            QueueLimit = int.MaxValue, // the execution limit below bounds what can wait
        })
        : RateLimitPartition.GetNoLimiter(string.Empty));

services.AddQuartz(q =>
{
    // before AddJobTimeout, so waiting for a permit does not spend the job's budget
    q.AddJobMiddleware(new RateLimitMiddleware(limiter));

    // a waiting firing holds a thread and counts here: two per tenant, waiting or running
    q.UseExecutionLimits(limits => limits.ForGroupsWithPrefix("tenant:", 2));
});
```
<!-- endSnippet -->

| Case | What happens |
|---|---|
| A firing over the limit | waits for a permit, then runs |
| Misfire | none: the trigger was acquired before the wait |
| `ScheduledFireTimeUtc`, retries, continuations | unchanged |
| Interrupted while waiting | the job does not run; the firing ends as cancelled |
| The limiter's queue is full | `JobExecutionException`, so the trigger's retry policy decides |

**The cost is a thread per waiting firing.** A waiting firing is a running one: it holds a thread-pool
thread and a slot of its group's execution limit, and `[DisallowConcurrentExecution]` counts it.

* What can wait at once is bounded by the thread pool and by the group's execution limit. Set one, as the
  sample does, or a throttled group can fill the pool.
* Past its execution limit, a group's triggers stay in the store and can misfire, like any held-back work
  (item 4 of [Cluster-scoped limits](#cluster-scoped-limits)).
* Register it before `AddJobTimeout`, or the wait spends the job's timeout.
* A shutdown that waits for its jobs waits for their permits too. Set
  [`ShutdownJobInterruption`](../configuration/reference.md#scheduler) to cancel them instead.
* The wait counts in the firing's execution span and duration.

**When this is not enough.** The limiter lives in one process: `N` nodes allow `N` times the rate, and a
restart starts a new window. A cluster-wide "N starts per window" needs the job store to count starts.
That is [#3347](https://github.com/quartznet/quartznet/issues/3347), declined for now.

## How it works

On each trigger acquisition cycle, the scheduler thread:

1. Computes the free slots per group by subtracting running counts from the **node-scoped** limits.
   Cluster-scoped limits are passed as configured: this node's firings are already reservations the store
   holds, and subtracting them twice would halve the quota on the busiest node.
2. Passes these limits to the job store during trigger acquisition.
3. The job store lowers each **cluster-scoped** limit by what the cluster has in flight, then skips
   triggers whose group has no free slot.
4. Increments a group's running count when a job starts, and decrements it when the job completes. A
   completion that frees a slot of a group whose limit held a due trigger back in the last acquisition
   wakes the scheduler thread at once; any other completion does not. Before 4.5 the trigger waited for
   the next idle wait or scheduling change ([#4033](https://github.com/quartznet/quartznet/issues/4033)).

So:

* The thread pool limit (`quartz.threadPool.threadCount`) is still the global cap.
* Execution group limits add per-group caps inside it.
* At worst, a group is slightly under-used for one cycle if a slot opens between computation and
  acquisition.

## Clustering considerations

### Node-scoped limits

Each node declares and enforces its own node-scoped limits, so nodes with different hardware can differ.
For example, dedicated batch nodes and API nodes:

```properties
# batch-node.properties
quartz.executionLimit.batch-jobs = 8
quartz.executionLimit.* = 2

# api-node.properties
quartz.executionLimit.batch-jobs = 0
quartz.executionLimit.* = 10
```

Three nodes each configured `batch-jobs = 8` can run 24 batch jobs: right for hardware capacity, wrong
for a quota.

### Cluster-scoped limits

A cluster-scoped limit is one number for the whole cluster, enforced by every node:

<!-- snippet: sample_execution_groups_cluster_scope -->
```csharp
q.UseExecutionLimits(limits => limits
    .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster));
```
<!-- endSnippet -->

The count comes from `QRTZ_FIRED_TRIGGERS`, which already records every reservation: a row appears when
any node acquires a trigger, becomes the running execution, and is deleted when the job completes or
cluster recovery cleans up after its node. Acquisition aggregates it by execution group, using the
`EXECUTION_GROUP` column the 4.x schema already has, so no table, column or migration is added.

**1. It is exact since 4.6, and costs the lock.** A round that counts against a cluster-scoped limit takes
the cluster's `TRIGGER_ACCESS` lock, a round of one included, so two nodes each down to their last free
thread cannot both read "2 of 3 in flight". Every round of a node with a cluster-scoped limit is locked,
whatever group its triggers are in; the acquisition metric's `quartz.jobstore.lock.reason` says
`cluster_limit`. See [Lock-free firing rounds](../operations.md#lock-free-firing-rounds).

* `LockFreeFiringRounds = false` restores 4.5: a round of one took no lock, so the ceiling held within one
  acquisition round and the overshoot was at most `limit + (nodes − 1)`, one per node, until the losers
  noticed. A round asking for more than one was locked, so the automatic `MaxBatchSize` kept the overshoot
  to nodes down to their last free thread.
* `AcquireTriggersWithinLock = true` made the ceiling exact on 4.1–4.5 and serializes every round
  cluster-wide, limit or not. It has nothing left to buy here.

::: tip SQLite
`AcquireTriggersWithinLock` is forced to `true` for SQLite at startup, for locking reasons, so the ceiling
was exact there on every version. A SQLite test proves nothing about the 4.5 bound on SQL Server or
PostgreSQL.
:::

**2. It fails closed.** The count and the work queue are in the same database. If the store is
unreachable or a node is partitioned from it, that node's `AcquireNextTriggers` throws, the scheduler
thread raises `SchedulerError` and backs off, and the node fires **nothing**. A database outage stops
firing; it does not remove the ceiling.

**3. A dead node's slots are held until recovery.** `ClusterRecover` deletes a failed node's
fired-trigger rows on the check-in cadence (`CheckinInterval` + `CheckinMisfireThreshold`). Until then
its reservations still count, so the quota is briefly **under**-served, never over-served. The count
deliberately includes nodes that have missed a check-in: one still running jobs would otherwise stop
counting and let the cluster exceed the ceiling.

**4. Held-back work can misfire.** A trigger held back by a ceiling stays `WAITING`, and acquisition
excludes anything older than `MisfireThreshold` (one minute by default). A group at its ceiling for
longer sends its backlog to `RecoverMisfiredJobs`, which applies each trigger's misfire instruction. This
is likelier with a cluster-scoped limit: a saturated node loses the trigger to a peer, but a saturated
cluster has no peer. If the backlog matters, pair a tightly limited group with
`MisfireInstruction.IgnoreMisfirePolicy`, or raise `MisfireThreshold`.

**Cost.** One extra aggregate per acquisition attempt, not per trigger, and only when at least one limit
is cluster-scoped. A configuration without one pays nothing.

Measured against PostgreSQL 15 and SQL Server 2022 with `QRTZ_FIRED_TRIGGERS` seeded from ten to ten
thousand rows (`ExecutionCeilingBenchmark` in `src/Quartz.Benchmark`, which says how to run it). Means
with BenchmarkDotNet's error, from containers on a developer machine; read the shape, not the absolute
microseconds:

| Rows in flight, groups | Aggregate, PostgreSQL | Aggregate, SQL Server | Candidate select, PostgreSQL | Candidate select, SQL Server |
|---|---|---|---|---|
| 1,000, eight | 672 µs (± 10) | 1,311 µs (± 34) | 627 µs (± 7) | 2,992 µs (± 68) |
| 10,000, sixty-four | 2,723 µs (± 116) | 7,821 µs (± 167) | | |

* **Below about a thousand rows the aggregate is one round trip and almost no work.** It costs about the
  same on both databases (their candidate selects differ), and about the same at a thousand rows as at
  ten. The ceiling costs *one extra round trip* a round, not one extra scan. A batched round, the default,
  spreads it over the batch; at `MaxBatchSize = 1` it is one round trip a firing.
* **Above that the scan shows.** `QRTZ_FIRED_TRIGGERS` has one row per reservation or running execution,
  so ten thousand is more than a realistic cluster's thread pools hold. A cluster reaches it by losing
  nodes faster than `ClusterRecover` cleans up after them.

::: tip An index for very large clusters, deliberately not in the standard schema
A covering index on `(SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP)` speeds up the aggregate:

| Rows in flight | PostgreSQL | SQL Server |
|---|---|---|
| 1,000 | | 1,311 µs → 714 µs |
| 10,000 | 2,723 µs → 1,350 µs | 7,821 µs → 1,870 µs |

At a hundred rows and below the difference is inside the measurement error.

It is **not** in the standard schema: `QRTZ_FIRED_TRIGGERS` gets an insert and a delete on every firing,
so every deployment would pay the write cost for a statement only cluster-scoped ceilings issue, at
concurrency most clusters never reach. Add it if you run a cluster-scoped ceiling *and* routinely hold
four figures of work in flight:

```sql
CREATE INDEX IDX_QRTZ_FT_EG_TG ON QRTZ_FIRED_TRIGGERS (SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP);
```

If the extra round trip, rather than the scan, is the concern (as it is below ten thousand rows), the fix
is to fold the count into the candidate select, not to index the table.
:::

**RAMJobStore** is never clustered, so its cluster is the one process. It counts its own reservations
and running executions, and a cluster-scoped limit gives the same number as a node-scoped one. Both
stores are held to the same assertions in `JobStoreContractTest`.

## Routing: which node serves a group

A limit says how many of a group run at once. Routing says **which nodes** run it. From 4.6 a node lists
either the groups it serves or the groups it refuses, and acquires nothing else.

| Option | Flat key | Means |
|---|---|---|
| `Scheduler:ServedExecutionGroups` | `quartz.scheduler.servedExecutionGroups` | This node acquires these groups and nothing else. Empty: every group not refused |
| `Scheduler:RefusedExecutionGroups` | `quartz.scheduler.refusedExecutionGroups` | This node never acquires these groups. Empty: refuses none |

* Set one list, never both. Both set fails at startup.
* An entry is a group name, or `_` (or `null`) for triggers with no execution group — the spelling a
  limit's key uses. A serve-list that omits `_` does not serve ungrouped triggers; a refuse-list that omits
  it does.
* The flat key is comma-separated. No wildcard, no prefix: `*` and `tenant:*` are refused at startup.
* At most 1,000 groups per list. Names are compared ordinally in memory and by the `EXECUTION_GROUP`
  column's collation in SQL.

<!-- snippet: sample_execution_groups_serve -->
```csharp
// This node acquires "reports" triggers and triggers with no execution group, and nothing else.
q.ServeExecutionGroups("reports", "_");
```
<!-- endSnippet -->

<!-- snippet: sample_execution_groups_refuse -->
```csharp
// This node acquires everything except "gpu" triggers.
q.RefuseExecutionGroups("gpu");
```
<!-- endSnippet -->

```properties
# the same two nodes, in properties
quartz.scheduler.servedExecutionGroups = reports, _
quartz.scheduler.refusedExecutionGroups = gpu
```

Routing and limits compose: routing decides whether a node reads a trigger, limits decide how many it
runs. `quartz.executionLimit.gpu = 0` still means "forbidden here"; it reads the rows and refuses them
every round, where a route never reads them.

### What a routed node does

| | Served group | Unserved group |
|---|---|---|
| Acquisition | reads and fires | the SQL `WHERE` clause leaves the row out |
| Misfire sweep | applies the trigger's misfire instruction | leaves the row untouched |
| `quartz.trigger.misfire` | counts | does not tick |
| Cluster recovery | the recovery trigger keeps the firing's group, so a serving node runs it | same |
| Manual and startup firings | the trigger [carries the job's group](#manual-and-startup-firings), so a serving node runs it | same |

A trigger in a group **no live node serves** waits, unmodified, until a serving node arrives. That node
applies the trigger's misfire instruction once: `FireNow` runs it once, `DoNothing` skips to the next
occurrence. Nothing re-arms it every minute meanwhile, which is what keeps the store quiet and the trap
visible.

### Seeing an unserved group

| Signal | Where | What it says |
|---|---|---|
| Health check, `Degraded` | `UnservedExecutionGroupTolerance` (default `3`), on whenever the node routes | a `Normal` trigger in a group this node does not serve is overdue by more than 3 × the store's misfire threshold: `'gpu' (12)`, oldest due 420 s ago. [Health check](../configuration/reference.md#health-check) |
| Dashboard | Overview → Execution groups: **Served here** and **Overdue** columns; Cluster → **Serves** | a group with overdue triggers that this node does not serve is marked. [Execution groups](../packages/dashboard.md#execution-groups) |
| Log 1093 | at scheduler start | what this node serves or refuses; "this store is not clustered, so nothing else runs them" on an in-memory or unclustered store |
| Log 3609 / 3610 | the misfire handler | unserved misfires appeared (warning, then at most hourly) / are gone |

The health check infers it from the store alone: a serving node's sweep re-arms a misfired trigger within
about one threshold, so a `WAITING` trigger three thresholds late has had no serving node reach it.

### Limitations

* **An in-memory store is one node.** A refused group there never runs; event 1093 says so. Every
  acquisition round still walks the due triggers it refuses.
* **`UseTriggerGroupWhenUnset` is a limits rule, not a routing rule.** Routing reads the stored execution
  group only. Tag the triggers, or route by `_`.
* **A trigger pinned to a node that refuses its group waits while that node is live.** Both predicates
  apply. That node's health check reports it. See
  [Node affinity](node-affinity.md#requirements-and-limitations).
* **Mixed 4.5 and 4.6 nodes.** A 4.5 node serves every group and sweeps every misfire, so refusing a
  group on the 4.6 nodes does not keep a 4.5 node off it. See
  [Rolling 4.5 to 4.6](../operations.md#rolling-4-5-to-4-6).
* **No index.** The clause is a residual on the rows `IDX_QRTZ_T_NFT_ST` already returns, as the
  node-affinity clause is. Measure before adding one: an index leading with `EXECUTION_GROUP` loses the
  ordered seek for every node that routes nothing.
* **Custom job stores** see the lists as `TriggerAcquisitionRequest.ServedExecutionGroups` and
  `RefusedExecutionGroups`, and honour them or not; a custom `IDriverDelegate` answers
  `FiltersAcquisitionExecutionGroups` or the store filters the candidates for it. Changing the lists at
  runtime is not supported: restart the node.

## Interaction with DisallowConcurrentExecution

`[DisallowConcurrentExecution]` is always respected: a trigger must satisfy it and its execution group's
limit to be acquired. In the ADO job store neither is a SQL predicate. The candidate select returns each
trigger's execution group, the delegate counts slots down as it reads the rows, and
`[DisallowConcurrentExecution]` is checked afterwards in the acquisition loop. One statement serves every
limits configuration, so [letting the trigger group stand in](#letting-the-trigger-group-stand-in) needs
no dialect SQL of its own.

## Database schema

In 4.x the `EXECUTION_GROUP` column on `QRTZ_TRIGGERS` is part of the standard schema, **required** for
ADO.NET job stores, and in every table creation script. When upgrading a 3.x database, add it:

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;

-- PostgreSQL / MySQL / SQLite
ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;

-- Oracle
ALTER TABLE QRTZ_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);
```

`QRTZ_FIRED_TRIGGERS` also has an `EXECUTION_GROUP` column, recording the group a firing belongs to.
`IScheduler.QueryFireInstances` reports it from any node, and a **cluster-scoped** limit is counted from
it. Candidate triggers are still chosen from `QRTZ_TRIGGERS`. When upgrading from 3.x, add it too:

```sql
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;  -- SQL Server
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;  -- PostgreSQL/MySQL/SQLite
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);  -- Oracle
```

RAMJobStore requires no schema changes.

## Dashboard

The Quartz Dashboard shows execution groups on:

* the overview page: one row per group with its limit, its scope, what it has in flight and the headroom
  left (cluster-wide with a persistent store). Look here to see whether a ceiling is holding work back.
  On a node that routes, two more columns: whether the group is served here, and how many of its triggers
  are more than five minutes overdue. See [Dashboard](../packages/dashboard.md#execution-groups).
* the cluster page, as a **Serves** column per node;
* the trigger list page, as an "Execution Group" column;
* the trigger detail page;
* the currently executing page, for each running job.

## Common scenarios

### Preventing batch jobs from starving interactive work

<!-- snippet: sample_execution_groups_batch_versus_interactive -->
```csharp
q.UseExecutionLimits(limits =>
{
    limits.ForGroup("batch", maxConcurrent: 3);    // max 3 batch jobs
    limits.ForOtherGroups(maxConcurrent: 10);      // everything else gets up to 10
});
```
<!-- endSnippet -->

### Dedicating a node to specific workloads

```properties
# This node runs "reports" triggers and nothing else, at most ten at a time
quartz.scheduler.servedExecutionGroups = reports
quartz.executionLimit.reports = 10
```

The 4.5 recipe still works, as a limit rather than a route: `quartz.executionLimit.* = 0` reads every
other group's due triggers each round and refuses them, and cannot say "serve only these" without a limit
per served group. See [Routing](#routing-which-node-serves-a-group).

### Multi-tenant isolation

A tenant quota belongs to the tenant, not the machine, so make it cluster-scoped:

<!-- snippet: sample_execution_groups_tenant_quotas -->
```csharp
limits.ForGroup("tenant-a", 5, ExecutionLimitScope.Cluster);
limits.ForGroup("tenant-b", 5, ExecutionLimitScope.Cluster);
limits.ForGroup("tenant-c", 5, ExecutionLimitScope.Cluster);
```
<!-- endSnippet -->

Node-scoped (`limits.ForGroup("tenant-a", 5)`) would give each tenant five threads *per node*: fifteen on
a three-node cluster. For tenants you cannot list, use a prefix: see
[Per-tenant limits](#per-tenant-limits). See [Multi-tenancy](../multi-tenancy.md) for the rest of
per-tenant setup.
