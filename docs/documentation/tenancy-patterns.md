---
title: Tenancy Patterns
---

# Tenancy Patterns

Quartz.NET has no `Tenant` type and will not get one. You build tenancy from three separations: a
scheduler, a group and a `SCHED_NAME`. Choose between them before you have twelve tenants in
production and a migration to write. The mechanics are in the per-version guides:

- [Multi-Tenancy (Quartz 4.x)](quartz-4.x/multi-tenancy.md)
- [Multi-Tenancy (Quartz 3.x)](quartz-3.x/multi-tenancy.md)

## Isolation is not authorization, and partitioning is not isolation

- **Isolation is not authentication and authorization.** An authenticated, authorized user can still
  reach another tenant's resources. Isolation is the enforced use of tenant context to bound which
  resources a request can reach at all.
- **Partitioning is not isolation.** A tenant id in a column, a key prefix or a group name arranges
  data by tenant. It does not stop code reading across the boundary.
- **Isolation is chosen per layer.** For a scheduler the question is which of the scheduling loop,
  the thread pool, the job store and the database are shared, and which are dedicated. They can be
  answered differently, and usually should be.

### Two vocabularies for the same thing

AWS ([silo, pool and bridge](https://docs.aws.amazon.com/wellarchitected/latest/saas-lens/silo-pool-and-bridge-models.html))
and Microsoft ([tenancy models](https://learn.microsoft.com/azure/architecture/guide/multitenant/considerations/tenancy-models))
use different words for the same models:

| AWS | Microsoft | Meaning |
|---|---|---|
| Silo | Automated single-tenant deployments | Each tenant gets a dedicated stack |
| Pool | Fully multitenant deployments | Every tenant shares one set of infrastructure |
| Bridge | Vertically partitioned deployments | Some tenants siloed, some pooled; usually a premium tier |
| Bridge (applied per layer) | Horizontally partitioned deployments | Shared compute, dedicated data store per tenant |
| Cell | Deployment stamp | A whole copy of the system serving a bounded set of tenants |

A cell or stamp holds **many** tenants, not one. A silo bounds *access*; a cell bounds *blast
radius*. You can want one without the other.

## How other systems partition tenants

In almost every system the tenancy primitive is a naming, configuration and access boundary, not a
fairness boundary. Quotas and rate limits usually attach one level up (the account, the cluster or
the process), and per-tenant fairness is often a paid tier.

| System | Primitive | Enforced by | Quota attaches to | Tenant added at runtime? |
|---|---|---|---|---|
| Temporal | Namespace | Server | Namespace *and* cluster | Yes, `RegisterNamespace` |
| Cadence | Domain | Server | Domain and task list | Yes |
| Kubernetes `CronJob` | Namespace | API server + RBAC | Namespace, via `ResourceQuota` | Yes, one API call |
| AWS EventBridge Scheduler | Schedule group | IAM, via the ARN | **Account and Region, never the group** | Yes, `CreateScheduleGroup` |
| Azure Durable Functions | Task hub | Storage naming, or RBAC on the managed provider | Scheduler resource, not the hub | Yes, implicitly |
| HashiCorp Nomad | Namespace | Server + ACL | Namespace; quotas are Enterprise-only | Yes |
| Google Cloud Scheduler | Project | IAM | Project | Only by creating a project |
| Hangfire | Queue, server, storage | Convention | Server process | Storage yes, queue set no |
| Celery | Queue, broker vhost | Broker | **Worker process, not the cluster** | Yes, `add_consumer` |
| Sidekiq | Queue, Redis instance | Convention | Process, or capsule | Queue set fixed at start |
| Airflow | Team, pool, queue | Pools: the metadata database. Teams: the API layer | Pool, cluster-wide | A pool yes; a team needs new processes |
| Quartz (Java) | `instanceName`, group | `SCHED_NAME` in every statement | Scheduler, not the group | Yes, `DirectSchedulerFactory` |

### What carries over to Quartz.NET

- **Where a limit is counted matters most.** Celery's `rate_limit` is per worker, so 10/s across
  four workers is 40/s. Airflow's pools hold cluster-wide, but `[core] parallelism` is per
  scheduler. Quartz.NET's execution limits are per node by default; see
  [What Quartz.NET does not give you](#what-quartz-net-does-not-give-you).
- **A shared partition with a tenant discriminator scales further than a boundary per tenant.**
  Temporal recommends a task queue per tenant over a namespace per tenant, and suggests namespaces
  per tenant only below about 50 tenants. A group per tenant is the same trade.
- **Two users of one partition name collide.** Azure Durable Functions apps that share a task hub
  compete for messages and can get stuck, so Azure derives the default hub name from the app name.
  Two unrelated schedulers with one `SCHED_NAME` fail the same way.
- **A prefix buys naming and nothing else.** Sidekiq 7.0 removed `redis-namespace`: a key prefix
  added cost to every operation and gave no separate tuning, durability or failure domain. A
  per-tenant table prefix has the same shape.
- **Ordering that falls out of an index is not fairness.** Hangfire on SQL Server fetches queues in
  name order, so a busy `acme` can starve `zeta`. Its per-tenant throttling is in the paid Hangfire
  Ace set, best-effort, and scoped to its storage.
- **Java Quartz has no tenancy guidance.** Its groups are categories with no per-group concurrency
  limit, and its answer to different concurrency for different jobs is a second scheduler. It
  recommends splitting thousands of short jobs across several schedulers because the cluster-wide
  lock degrades beyond about three nodes.

## The axes that decide

Ordered roughly by how often each turns out to decide:

| Axis | Ask yourself | Pushes toward shared | Pushes toward dedicated |
|---|---|---|---|
| Onboarding cadence | Does a tenant appear while the process runs? | Runtime arrival | Tenants known at deploy time |
| Tenant count | How many, and how skewed? | Hundreds or thousands | Tens, or a few whales |
| Blast radius | What is the cost of one bad tenant taking everything down? | Tolerable | Unacceptable |
| Noisy neighbours | Can one tenant's work starve another's? | Workloads are small and similar | Workloads are bursty or heavy |
| Per-tenant quotas and SLAs | Do you sell tiers with different guarantees? | One tier | Contractual per-tenant limits |
| Data residency | Must a tenant's data live somewhere specific, or under its own key? | No constraint | Regulated or sovereign data |
| Cost per idle tenant | What does a tenant cost when it schedules nothing? | Must be ~zero | Amortised by the contract |
| Customisation | Do tenants differ in configuration, calendars, or schema? | Uniform | Divergent |
| Observability | Must you answer "what is tenant X doing right now?" | Group dimension is enough | Needs its own dashboards |

### Onboarding cadence decides more than anything else

Evaluate this first: it often rules an option out. With a group per tenant, onboarding is writing a
trigger. With a scheduler per tenant it is closer to a deployment. Neither version needs a
*redeploy* for it (see
[Onboarding a tenant while the process runs](#onboarding-a-tenant-while-the-process-runs)), but it
is far more work than a `ScheduleJob` call.

### Tenant count, and the shape of the distribution

A scheduler per tenant costs a scheduling loop, a thread pool and, with a persistent store, a
connection pool and a cluster check-in. Tens of schedulers in a process is ordinary; thousands is a
different program. AWS calls 20 siloed tenants manageable and a thousand a burden to operate.

For a skewed distribution, use the bridge: the long tail on one shared scheduler with a group per
tenant, and a dedicated scheduler for each large tenant. Microsoft calls this a vertically
partitioned deployment, with the dedicated tier often sold at a higher rate.

### Blast radius and noisy neighbours are different problems

| Problem | Kind | Remedy |
|---|---|---|
| Noisy neighbours | Capacity: one tenant takes a disproportionate share | Quotas, throttling, priorities |
| Blast radius | Failure: one tenant's failure takes others down | Independent failure domains: cells, stamps, separate deployments |

Moving work that is not time-sensitive to off-peak hours needs only a cron expression, and is often
cheaper than any isolation mechanism. If you will ever run more than one scheduler, run two from the start,
so no code assumes there is only one.

### Cost per idle tenant

A tenant with one nightly job is idle 99.9% of the time. A tenant that is a group costs a row. A
tenant that is a scheduler costs a scheduling loop that wakes on its own idle timer whether or not
it has work, plus its pools. Multiply by the number of dormant tenants before choosing.

### Observability

- With a group per tenant, the group is the tenant dimension. Make the group the raw tenant id, not
  a decorated string. With a scheduler per tenant, the scheduler name is the dimension.
- Watch cardinality. A tenant dimension times job and trigger names is a series per tenant per
  trigger. Drop the name tags in a view before they reach the backend, unless you need them.
- The group is a tag on execution traces on both versions, and on metrics on 4.x (3.x has no
  metrics). Neither version puts it into a logging scope; add per-tenant log correlation yourself.

## Mapping the axes onto Quartz.NET

The three separations compose, and useful designs use more than one.

- **The scheduler** is the strongest boundary in the process. Each owns its job store, thread pool,
  listeners, plugins and calendars, and with a persistent store its connection pool and cluster
  check-in. A scheduler per tenant is siloed compute.
- **The group** is the group half of every `JobKey` and `TriggerKey`. It is a logical partition,
  not an enforced one: nothing stops a job in group `acme` from touching group `initech`. It makes
  every matcher-taking API (listing, pausing, resuming, deleting) tenant-scoped, and it is already
  a tag on the execution traces. A group per tenant is pooled compute with a tenant discriminator.
- **`SCHED_NAME`** is the first column of every Quartz table's primary key, and every statement
  filters on it. Two schedulers with different names share tables without seeing each other's
  rows. This is a property of the schema, not of the code paths.
- **The table prefix** gives separate table *sets* in one database. It is a backup, restore and
  permissions decision, not an isolation one.

### Which mechanism exists on which version

| Mechanism | 3.x | 4.x |
|---|---|---|
| Multiple schedulers in one process | Yes | Yes |
| Named schedulers through Microsoft DI | Yes, `AddQuartz(name, …)` | Yes, `AddQuartz(name, …)` |
| Resolving one by name | `ISchedulerRepository` | Keyed `IScheduler`, or `ISchedulerRepository` |
| Groups and group matchers | Yes, `GroupMatcher<T>` | Yes, plus the paged query API |
| `SCHED_NAME` row separation | Yes | Yes |
| Per-scheduler table prefix | Yes | Yes |
| Startup schema validation | Yes, `PerformSchemaValidation` on by default | Yes, `SchemaProvisioning.Validate` by default |
| Shared database, mismatched table prefix | No: silent, each scheduler sees an empty table set | Yes, warns naming both schedulers and both prefixes |
| Listing tenants without starting them | No: the repository lists live schedulers only | Yes, `ISchedulerRegistry.QuerySchedulers()` |
| Execution groups and per-node limits | Yes | Yes |
| Trigger group as the execution group | No: tag every trigger explicitly | Yes, `UseTriggerGroupWhenUnset()` |
| Cluster-wide concurrency quota | No | Yes, `ExecutionLimitScope.Cluster`; approximate unless `AcquireTriggersWithinLock` |
| Rate limiting (N per window) | No | No |
| Node affinity (persisted, cluster-aware) | Yes, `WithPreferredNode` | Yes, `WithPreferredNode` |
| Per-scheduler job type registration | No: one unkeyed registration, first wins | Yes, `AddJobType<TJob, TImplementation>()` |
| Per-scheduler plugin instance from `quartz.plugin.*` | Yes for an activated type, no for a registered one | Yes; the probe is keyed by scheduler |
| Preparing the job's DI scope | Subclass and override `ConfigureScope` | `ConfigureJobScope(…)` delegate |
| Reading the current firing without being handed it | No: your own `AsyncLocal` | Yes, `IJobExecutionContextAccessor` |
| Per-scheduler health check | No: one check, on the default scheduler | Yes, `q.AddQuartzHealthChecks()` per scheduler |
| Metrics | No | Yes |
| Runtime tenant onboarding without a container | Yes, `StdSchedulerFactory` / `DirectSchedulerFactory` | Yes, `QuartzSchedulerBuilder` |
| Runtime tenant onboarding into the application's container | No | Yes, `ISchedulerRuntime.Add` / `Remove` |

### Choosing

Stop at the first that applies:

1. **Tenants' data must be physically separate, or in a particular place.** A database per tenant,
   and so a scheduler per tenant, because a job store binds to one data source. This is the silo,
   with the silo's onboarding cost.
2. **A few tenants need isolation and the rest do not.** The bridge: one shared scheduler with a
   group per tenant for the long tail, and a dedicated scheduler for each tenant that bought
   isolation. The most common shape for SaaS, and the default when 3 and 4 do not clearly apply.
3. **Tenants arrive while the process runs, and there are many.** A group per tenant. Onboarding is
   a `ScheduleJob` call, with no registration, no restart and no per-tenant cost beyond the rows.
4. **Tenants are few, known at deployment, and differ in configuration.** A named scheduler per
   tenant, with per-scheduler options and health checks.

Then decide the database separately:

- one `SCHED_NAME` per tenant is usually enough;
- a table prefix per tenant if backup or permissions need separate tables;
- a separate database only if the data must not sit beside another tenant's.

## What Quartz.NET does not give you

These apply to both 3.x and 4.x unless noted.

**Concurrency limits are per node unless you say otherwise; on 3.x that is the only option.** By
default an execution group's running count lives in memory on the scheduler thread. Nothing is
persisted and nodes do not coordinate, so a group limited to 3 can run up to 3×N across an N-node
cluster. On 3.x the closest approximation is dividing the cap by the node count, which is wrong
whenever a node is down.

On 4.x, `ForGroup("acme", 8, ExecutionLimitScope.Cluster)` is counted from `QRTZ_FIRED_TRIGGERS`,
the cluster's reservation ledger. Know its limits:

- The ceiling holds within one acquisition round, but can overshoot by up to `nodes − 1` (one
  trigger per node). The lock-free acquisition path that allows the overshoot is taken only when a
  round acquires a single trigger.
- It fails closed: a node that cannot reach the store fires nothing rather than firing unmetered.

**There is no rate limiting.** Execution limits cap *concurrency*, not throughput. "This tenant may
run 100 jobs an hour" cannot be expressed; build it into the job, or into what the job calls.

**A starved group misfires; it does not queue.** A group at its limit has its triggers skipped at
acquisition. They keep their original next fire time, so if the starvation outlasts the misfire
threshold they misfire, and the trigger's misfire instruction, not the limit, decides whether the
occurrence is skipped or rescheduled. Choose misfire instructions for triggers in limited groups
with that in mind.

**Pausing by group prefix does not catch groups added later.** Quartz.NET records the groups a
prefix *matched*, and those stay paused; a group that held nothing when the prefix ran was never
matched. Paused *job* groups have a second caveat: 3.x's ADO store does not persist them, and 4.x
persists them but does not impose the pause on jobs added afterwards. Suspend a tenant by *trigger*
group.

**On 3.x, job types are not keyed by scheduler.** Jobs resolve from the one container by type, with
no scheduler key. Two 3.x schedulers in one container cannot have different implementations or
lifetimes of the same job type: whatever the application registered is what every scheduler gets.
4.x keeps the unkeyed registration as the default (`AddJob<T>` still registers the type with
`TryAdd` semantics), but `AddJobType<TJob, TImplementation>()`, `AddJobType<TJob>(lifetime)` and
`AddJobType<TJob>(factory)` register under one scheduler's key, and the job factory reads that key
first. On either version, a job type per tenant stops scaling long before groups do. Prefer one job
type that reads its tenant from the firing and resolves what it needs inside `Execute`, by key if
you like.

**Nothing stops a job reaching another tenant's data.** Groups are a naming partition. Quartz.NET
gives you partitioning; isolation is your application's job: a tenant id read from the firing and
passed through every query.

**On 3.x, dashboard authorization is per process, all or nothing.** One policy, one read-only flag,
no per-scheduler policy and no scheduler-name claim check. If 3.x tenants must reach only their own
scheduler, enforce it outside Quartz.NET: a process per tenant, or middleware that authorizes on the
scheduler-name route segment.

4.x holds each caller to its own scheduler on both surfaces.
`QuartzDashboardOptions.SchedulerAuthorizationPolicy` and
`QuartzHttpApiOptions.SchedulerAuthorizationPolicy` name a policy evaluated per request against a
`SchedulerResource` carrying the scheduler's name, so one
`AuthorizationHandler<TRequirement, SchedulerResource>` covers every route and page. What a caller
may *do* to the scheduler it reaches is still process-wide (the dashboard's read-only flag), so
"this tenant may look, that one may act" stays outside Quartz.NET on either version.

**A shut-down scheduler is not restarted in place.** `Standby()` / `Start()` pause and resume.
Shutdown is terminal: the scheduler refuses to start again and every other operation throws. You can
build a *new* scheduler with the same name. On 4.1 and later,
[`ISchedulerRuntime.Restart`](quartz-4.x/multi-tenancy.md#restarting-a-scheduler) does that: it
replays the recipe the scheduler was registered with, waits for the outgoing one's jobs, and binds
the result under the same name.

**The tenant does not reach your logs by itself.** The job and trigger group are tags on execution
traces, and on 4.x's metrics, but neither version puts them into a logging scope. A tenant carried
only in an *execution* group is invisible to those signals, which is one more reason to make the
trigger group the tenant.

### Onboarding a tenant while the process runs

With a group per tenant, onboarding is an ordinary `ScheduleJob` call for a new group.

With a scheduler per tenant, the DI path closes once the container is built: `AddQuartz` changes
`IServiceCollection`, and the hosted service enumerates schedulers once, at start. Both versions can
still build a scheduler at runtime *outside* the container: 3.x through `StdSchedulerFactory` or
`DirectSchedulerFactory`, 4.x through `QuartzSchedulerBuilder`, which creates and owns a container of
its own. A scheduler built this way:

- gets no hosted-service lifetime (you start and dispose it);
- resolves its jobs from its own container, not the application's, unless you give it a job
  factory that bridges;
- is not covered by health checks registered at startup.

4.1 fixes all three with `ISchedulerRuntime.Add(name, configure)`. It builds the tenant into a
container of its own that resolves the application's services, jobs and options from the
application's. The tenant's jobs are ordinary application components, the host drains it when it
stops, and a health check registered under its name finds it. `Remove` shuts it down and releases
everything built for it. The per-version guides cover the API and the trade-offs.

## Anti-patterns

**Putting the tenant in a job name and parsing it back out.** `JobKey("nightly-report-acme")` looks
harmless until something needs every job for a tenant, and the only way is to fetch every key and
split strings. Use the group half of the key for the tenant, and the name for what the job *is*.

**One scheduler per tenant at thousands of tenants.** Each scheduler is a scheduling loop that wakes
on its own timer, a thread pool, a connection pool and a cluster check-in, whether or not the tenant
has anything to run. Fine at twenty, a serious operational burden at a thousand. Groups scale where
schedulers do not.

**A shared database with a mismatched table prefix.** Nothing derives the prefix from the DDL or the
DDL from the prefix; you run the scripts with the prefix substituted. A prefix pointing at tables
that do not exist is caught at startup (3.x `PerformSchemaValidation`, 4.x
`SchemaProvisioning.Validate`, both on by default), and the error names the missing table. A prefix
pointing at tables that *do* exist and belong to another tenant is not caught: it looks correct and
runs on the wrong data. Derive the prefix from the tenant id in code, not from per-environment
configuration.

**Two unrelated schedulers sharing a database with the same `SCHED_NAME`.** To the schema they are
two nodes of one cluster, and they will take each other's triggers. Duplicate-name checks work only
within one container; across processes, keeping names distinct is up to you. Derive the scheduler
name from the tenant id rather than from a configuration file someone can copy.

**Assuming a per-node limit is a per-cluster limit.** See
[above](#what-quartz-net-does-not-give-you). It fails only in production, after the second node is
added, and it is still the default on 4.x: a limit is cluster-wide only when it says
`ExecutionLimitScope.Cluster`.

**Letting per-tenant metrics multiply without a view.** A tenant dimension times a trigger-name
dimension is a series per tenant per trigger. Aggregate before the data leaves the process.

## See also

- [Multi-Tenancy (Quartz 4.x)](quartz-4.x/multi-tenancy.md): the 4.x mechanics in full
- [Multi-Tenancy (Quartz 3.x)](quartz-3.x/multi-tenancy.md): the 3.x mechanics in full
- [Best Practices](best-practices.md#one-name-per-cluster-one-id-per-node): why never to point two
  non-clustered schedulers at one database
- [Troubleshooting](troubleshooting.md)
