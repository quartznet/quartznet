---
title: Tenancy Patterns
---

# Tenancy Patterns

Quartz.NET has no `Tenant` type and is not going to get one. What it has are three separations you
can build one out of — a scheduler, a group, and a `SCHED_NAME` — and the hard part is not wiring
any of them up. It is deciding which one you need before you have twelve tenants in production and
a migration to write.

This page is about that decision. It surveys how other schedulers and job systems partition
tenants, names the axes that actually decide the model, and maps those axes onto the mechanisms
Quartz.NET really has. The mechanics live in the per-version guides:

- [Multi-Tenancy (Quartz 4.x)](quartz-4.x/multi-tenancy.md)
- [Multi-Tenancy (Quartz 3.x)](quartz-3.x/multi-tenancy.md)

## Isolation is not authorization, and partitioning is not isolation

Three points decide most arguments about tenancy, and all are worth getting straight before looking
at any mechanism.

The first is that **tenant isolation is not the same thing as authentication and authorization**.
AWS's SaaS guidance is unusually blunt about it: "the fact that a tenant user is authenticated does
not mean that your system has achieved isolation … a user could be authenticated and authorized, and
still access the resources of another tenant." Isolation is the separate, enforced use of tenant
context to bound which resources a request can reach at all.

The second is that **partitioning is not isolation**. Putting a tenant id in a column, a key prefix
or a group name arranges your data by tenant. It does not by itself stop code from reading across
the boundary. Every system in the survey below distinguishes a partition it merely *names* from a
boundary it actually *enforces*, and the ones that blur the two are the ones whose users get hurt.

The third thing worth internalising is that isolation is not one choice. Microsoft's guidance says
to treat it as a spectrum rather than a discrete property, and that "you can use different levels of
isolation for each tier". AWS says the same thing from the other direction: under its bridge model,
"your view of silo and pool will be much more granular for environments that are decomposed into a
collection of services that have varying isolation requirements", and you should be "thinking about
the tradeoffs of silo and pool models for each resource or layer of your architecture."

For a scheduler that means the interesting question is rarely "siloed or pooled?" It is "which of
the scheduling loop, the thread pool, the job store and the database is siloed, and which is
pooled?" Those four can be answered differently, and usually should be.

### Two vocabularies for the same thing

The two most-cited bodies of guidance use different words. They line up well enough to translate,
and knowing both is useful because the literature you find will use one or the other.

| AWS | Microsoft | Meaning |
|---|---|---|
| Silo | Automated single-tenant deployments | Each tenant gets a dedicated stack |
| Pool | Fully multitenant deployments | Every tenant shares one set of infrastructure |
| Bridge | Vertically partitioned deployments | Some tenants siloed, some pooled — usually a premium tier |
| Bridge (applied per layer) | Horizontally partitioned deployments | Shared compute, dedicated data store per tenant |
| Cell | Deployment stamp | A whole copy of the system serving a bounded set of tenants |

The last row is the one people conflate with silo. A cell or stamp is **not** one tenant — it holds
many. Microsoft says a stamp is "sometimes a *service unit*, *scale unit*, or *cell*", and that "each
stamp serves a predefined number of tenants". AWS frames cells through the bulkhead metaphor: "If a
workload uses 10 cells to service 100 requests, when a failure occurs in one cell, 90% of the overall
requests would be unaffected." Silo bounds *access*; a cell bounds *blast radius*. You can want one
without the other.

## How other systems partition tenants

The pattern across the field is consistent enough to state up front: **almost every system's tenancy
primitive is a naming, configuration and access boundary, and almost none of them make it a fairness
boundary.** Quotas and rate limits usually attach one level up — to the account, the cluster or the
process — and where a system does give per-tenant fairness, it is frequently the paid tier.

| System | Primitive | Enforced by | Quota attaches to | Tenant added at runtime? |
|---|---|---|---|---|
| Temporal | Namespace | Server | Namespace *and* cluster | Yes, `RegisterNamespace` |
| Cadence | Domain | Server | Domain and task list | Yes |
| Kubernetes `CronJob` | Namespace | API server + RBAC | Namespace, via `ResourceQuota` | Yes, one API call |
| AWS EventBridge Scheduler | Schedule group | IAM, via the ARN | **Account and Region, never the group** | Yes, `CreateScheduleGroup` |
| Azure Durable Functions | Task hub | Storage naming, or RBAC on the managed provider | Scheduler resource, not the hub | Yes, implicitly |
| HashiCorp Nomad | Namespace | Server + ACL | Namespace — but quotas are Enterprise-only | Yes |
| Google Cloud Scheduler | Project | IAM | Project | Only by creating a project |
| Hangfire | Queue, server, storage | Convention | Server process | Storage yes, queue set no |
| Celery | Queue, broker vhost | Broker | **Worker process, not the cluster** | Yes, `add_consumer` |
| Sidekiq | Queue, Redis instance | Convention | Process, or capsule | Queue set fixed at start |
| Airflow | Team, pool, queue | Pools: the metadata database. Teams: the API layer | Pool, cluster-wide | A pool yes; a team needs new processes |
| Quartz (Java) | `instanceName`, group | `SCHED_NAME` in every statement | Scheduler, not the group | Yes, `DirectSchedulerFactory` |

A few of these repay a closer look, because each teaches something the table cannot.

### Temporal ranks namespace-per-tenant last

Temporal's Namespace is the textbook tenancy primitive: "A Namespace is a unit of isolation within
the Temporal Platform." Workflow id uniqueness is scoped to it, task queues belong to it, and
retention, archival, replication and rate limits are configured on it. It is genuinely
server-enforced, and a tenant is one `RegisterNamespace` call away.

Temporal nevertheless tells you not to use it that way. Its own multi-tenant patterns guidance ranks
four approaches and puts namespace-per-tenant **fourth**, describing it as "Only practical for a
smaller number of high-value tenants", manageable "for fewer than 50 tenants", and "not a good fit if
you expect a very large number of tenants (10,000+)". The recommended pattern is **task queue per
tenant** — that is, a client-side naming convention — scaling to "thousands of tenants per Namespace",
with a `TenantId` search attribute for querying. Their own summary: "For most SaaS use cases, a shared
Namespace with per-tenant Task Queues is simpler and more scalable."

The reason is the part the marketing does not cover. Namespaces share the persistence store and the
history shard set; the shard is chosen by hashing workflow id *and* namespace, so namespaces are an
input to the hash rather than a partition of it. The shard count "cannot be changed" after the
database is integrated. And in the open-source server there is no built-in RBAC at all: without an
`Authorizer` of your own, Temporal "allows every API request, with no authentication or access
control", which means namespace-per-tenant in OSS is a security boundary only if you write the
enforcement yourself.

Cadence, from which Temporal forked, reaches the same conclusion from the other side: its engineering
blog states plainly that "Shards are shared across the different domains in a Cadence cluster", and it
added per-workflow-id rate limits precisely because the domain was not a fine enough unit.

### Kubernetes documents what its namespace does not isolate

The Kubernetes multi-tenancy documentation is the model for the register this page tries to hit. It
recommends namespaces and then immediately lists what they fail to cover: namespace isolation
"doesn't apply to Kubernetes resources that can't be namespaced, such as Custom Resource
Definitions, Storage Classes, and Webhooks"; a `PersistentVolume` "is a cluster-wide resource and has
a lifecycle independent of workloads and namespaces"; "By default, the Kubernetes DNS service allows
lookups across all namespaces"; and "Quotas cannot protect against all kinds of resource sharing,
such as network traffic."

Two of those leaks have direct analogues in a scheduler. The control plane is shared, so there is one
`CronJob` controller for every tenant in the cluster; and `PriorityClass` is cluster-scoped, so a
paying tenant's pods can preempt a free tenant's across the namespace boundary. Kubernetes is also
explicit that `CronJob` delivery is not exactly-once — "the Jobs that you define should be
*idempotent*" — which is the same advice Quartz.NET's [best practices](best-practices.md) give about
recoverable jobs, and for the same reason.

Where namespaces are not enough, the escalation ladder Kubernetes describes is a virtual control
plane per tenant, and past that a dedicated cluster. That ladder — shared partition, then per-tenant
quota, then per-tenant control plane, then separate deployment — is the same one this page ends on.

### EventBridge Scheduler's group is a billing boundary, not a fairness one

AWS EventBridge Scheduler has an explicit *schedule group* primitive, and the group is embedded in
every schedule's ARN, which makes it a natural IAM boundary. It is also the only thing you can tag:
"With EventBridge Scheduler, you organize schedule **groups**, instead of individual schedules, by
applying tags." So it is the unit of cost allocation and the only dimension on the service's
CloudWatch metrics.

What it is not is a quota. Every published quota — 10,000,000 schedules, 500 schedule groups, the
1,000 TPS invocation throttle — is per account and Region. A tenant that floods its own group
consumes the same invocation budget as every other tenant. The group names and authorizes; it does
not ration.

### Azure's task hub is a `SCHED_NAME` by another name

Azure Durable Functions partitions by *task hub*, and on the SQL backend the implementation is
strikingly close to Quartz's: "each table includes a `TaskHub` column as part of its primary key."
On the Azure Storage backend the hub name is a prefix on queue, table and blob names in a shared
storage account, which means RBAC granularity is the storage account and not the hub.

Microsoft's warning about sharing one is the sharpest statement any vendor in this survey makes:
"If multiple apps use the same task hub, they compete for messages, which can result in undefined
behavior — including orchestrations getting unexpectedly stuck." Quartz.NET users will recognise the
failure mode; it is exactly what happens when two unrelated schedulers are given the same
`SCHED_NAME`. Microsoft's mitigation is to make the safe thing the default: the hub name is derived
from the app name so that "accidental sharing doesn't happen."

### Airflow spent a decade discovering where the boundary belongs

Airflow is the field's longest-running attempt to retrofit tenancy onto a scheduler, and the outcome
is the single best argument for the position this page takes.

AIP-1 proposed multi-tenancy in 2016, diagnosing that "every task has full access to the Airflow
database including connection details like usernames, passwords etc". It was eventually marked
superseded by four successors. AIP-43 shipped in Airflow 2.4, separating DAG-file parsing from the
scheduler. AIP-44 aimed to keep workers off the metadata database behind an internal API, shipped
experimentally in 2.10 — and was then deleted wholesale, in favour of AIP-72's Task SDK, which landed
in Airflow 3.0 and finally established that "in Airflow 3 direct DB access from workers will not be
allowed at all".

What shipped after all that is called **multi-team**, not multi-tenant, and its own documentation is
careful about the difference: it "provides *logical isolation* for a secure perimeter around teams, not
complete isolation. All teams share the same metadata database and common Airflow infrastructure. For
absolutely strict security requirements, consider separate Airflow deployments." The giveaway detail
is that "Dag IDs, Variable keys, and Connection IDs must be unique across the entire Airflow
deployment, regardless of which team owns them" — a single flat namespace, which is a naming
convention by definition.

The two halves are worth separating, because Airflow got one of them right and it is the half
Quartz.NET users ask about. **Pools are real.** A pool's concurrency bound is enforced in the metadata
database, with a row lock over the pool table, so it holds across multiple schedulers — genuinely
cluster-wide. **Identity is not.** Airflow's security model states that "All Dag authors have access
to all Dags in the Airflow deployment" and that DAG authors "should be trusted".

Note also the trap in the neighbouring setting: a *pool* binds cluster-wide, while `[core] parallelism`
binds "per scheduler". Two knobs that read as siblings, at different scopes. That is exactly the
confusion Quartz.NET's execution limits invite, which is why this page states their scope in the first
sentence that mentions them.

### Where the limit binds: Celery, Hangfire, and the per-worker trap

The single most transferable lesson in this survey concerns *where* a concurrency or rate limit is
counted, because it is the one everybody gets wrong.

Celery's documentation is admirably direct about it: "Note that this is a *per worker instance* rate
limit, and not a global rate limit. To enforce a global rate limit … you must restrict to a given
queue." A `rate_limit` of 10/s across four workers is 40/s.

Quartz.NET's execution limits have exactly this property by default, for exactly this reason: the
running count lives in memory in the scheduler. On 4.x a limit can opt out of it — declared
`ExecutionLimitScope.Cluster`, it is counted from the fired-triggers table instead, which is the same
move Airflow's pools make and the one Hangfire charges for. See
[what Quartz.NET does not give you](#what-quartz-net-does-not-give-you).

The contrast is Hangfire. Its free tier has no per-queue or per-tenant cap either — worker count is
per server — but `Hangfire.Throttling`, part of the paid **Hangfire Ace** set, adds mutexes,
semaphores and fixed/sliding/dynamic window rate limiters, throttling by rescheduling a job rather
than blocking a worker. A throttler's scope is its *storage's* scope: on SQL Server or Redis the limit
therefore holds across every server sharing that storage, and on the in-memory storage it collapses
back to one process. Even so the docs are careful — "Everything works on a best-effort basis," because
"it's very hard to achieve it due to the complexity of distributed processing."

Hangfire's own documentation pitches one of these at tenancy explicitly, and it is worth noting that
it is the paid one: dynamic window counters give "some kind of fair processing, where one participant
can't capture all the available resources that's especially useful for multi-tenant applications."

Hangfire also shows what happens when the queue is pressed into service as the tenant boundary. A
server's queue list is fixed when it starts, so a tenant that gets its own queue costs a restart — the
request to make that dynamic has been open since 2017, filed by someone describing exactly this
scenario. And on SQL Server the fetch statement has no `ORDER BY` at all; ordering falls out of an
index on the queue name, so tenants are served alphabetically and a busy `acme` can starve `zeta`.
Ordering that emerges from an index is not a fairness policy.

BullMQ splits the same way, and its history is instructive. The open-source limiter is genuinely
global — "The rate limiter is global, so if you have for example 10 workers for one queue with the
above settings, still only 10 jobs will be processed by second" — but per-*group* limiting was
removed: "From BullMQ 3.0 and onwards, group keys support is removed to improve global rate limit."
Per-tenant fairness came back as **Groups** in the paid BullMQ Pro, motivated by precisely the SaaS
case: "one user could fill the queue with jobs and the rest of the users will need to wait."

### Upstream Quartz has no tenancy guidance, and that is informative

Java Quartz — the project Quartz.NET is a port of — has the same three separations, and says nothing
about tenancy at all. There is no page, no cookbook entry and no FAQ answer; the only issue asking how
to handle a database per tenant was closed by a stale bot without an answer. `instanceName` is
documented as a way "to distinguish schedulers when multiple instances are used within the same
program", and `tablePrefix` as a way to "have multiple sets of Quartz's tables within the same
database". Neither mentions a tenant.

Groups are documented as categorisation and nothing more — "useful for organizing your jobs and
triggers into categories such as 'reporting jobs' and 'maintenance jobs'". There is no per-group
concurrency limit anywhere in Java Quartz; the thread pool is scheduler-wide and
`@DisallowConcurrentExecution` is scoped to a job key. Upstream's answer to "different concurrency for
different sets of jobs" is a second scheduler.

The one place upstream does recommend partitioning, it is for a reason that has nothing to do with
tenants and everything to do with the shared lock: "If you need to scale out to support thousands of
short-running (e.g 1 second) jobs, consider partitioning the set of jobs by using multiple distinct
schedulers … The scheduler makes use of a cluster-wide lock, a pattern that degrades performance as
you add more nodes (when going beyond about three nodes …)." That is a throughput argument, and it
happens to point the same way as the tenancy one.

Also worth knowing if you lean on group matchers: Java Quartz warns that
`pauseTriggers(GroupMatcher)` has "a limitation that only exactly matched groups can be remembered as
paused", so pausing by prefix does not keep newly-added triggers paused. Quartz.NET's group pause
state has its own caveat — job group pause state is not persisted by the ADO store — which is why both
per-version guides tell you to suspend a tenant by *trigger* group.

### Sidekiq removed its namespaces on purpose

Sidekiq supported Redis key namespacing through the `redis-namespace` gem and removed it in 7.0.
The upgrade guide is terse — "Support for `redis-namespace` has been removed", "I have advised
against its usage for many years now" — and the reasoning is on the maintainer's blog: namespacing
"increases the size of every key by the size of the prefix", and it means "you don't get to tune
Redis for the individual needs of 'cache' and 'transactional'." His verdict on the practice was that
it suits "hobbyists only who only want to pay for a single Redis database from a SaaS; you do not
want to build a business on top of this hack." The Sidekiq wiki's current line: "Redis namespaces do
not allow for this configuration and come with many other problems, so using discrete Redis instances
is always preferred."

The transferable point is not "prefixes are bad". It is that a prefix bought naming and nothing else
— no separate tuning, no separate durability policy, no separate failure domain — while adding cost
to every operation. A partition that gives you no new *capability* is usually not worth its
complexity. This is worth holding in mind when reaching for a per-tenant table prefix, which has the
same shape.

## The axes that decide

Walk your own situation down these. They are ordered roughly by how often they turn out to be the
deciding one.

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

This is the axis to evaluate first, because on most platforms it eliminates an option outright rather
than trading off against one.

Runtime tenant arrival is cheap where the primitive is a row or an API call and expensive where it is
a deployment. Temporal registers a namespace over gRPC, though registration "takes up to 10 seconds
to complete" and the API is itself rate-limited. Kubernetes creates a namespace instantly — but a
*usable* tenant is a namespace plus RBAC plus a `ResourceQuota` plus a `LimitRange` plus a
`NetworkPolicy`, and the docs concede that this "requires configuration of several other Kubernetes
resources". Celery can tell a running worker to consume a new queue with `add_consumer`; Hangfire and
Sidekiq fix a server's queue set when it starts.

AWS's silo model is where the cost shows: onboarding a siloed tenant "will require the provisioning
of new infrastructure and, potentially, the configuration of new account limits", which makes what
was one database insert into a deployment.

In Quartz.NET the same split appears between the group-per-tenant model, where onboarding is writing
a trigger, and the scheduler-per-tenant model, where it is closer to a deployment. It need not be a
*redeploy* on either version — see
[onboarding a tenant while the process runs](#onboarding-a-tenant-while-the-process-runs) — but it is
still several orders of magnitude more work than a `ScheduleJob` call.

### Tenant count, and the shape of the distribution

Nobody in this literature will give you a threshold, because the real limit is operational rather
than technical — but two sources come close enough to be useful.

AWS on silo: "If you have 20 siloed accounts for each of your tenants, for example, that may be
manageable. However, if you have a thousand tenants, that would likely begin to impact operational
efficiency and agility." Temporal on namespace-per-tenant: "most teams find this manageable for fewer
than 50 tenants."

Both numbers describe the same thing — the point at which a per-tenant *deployment artefact* stops
being something a human can reason about. For Quartz.NET, where a per-tenant scheduler costs a
scheduling loop, a thread pool and (with a persistent store) a connection pool and a cluster check-in,
the same order of magnitude applies. Tens of schedulers in a process is ordinary; thousands is a
different program.

The distribution matters as much as the count, and this is where the bridge model earns its keep.
Microsoft's vertically partitioned model exists for exactly the common SaaS shape: most tenants on
shared infrastructure, "single-tenant infrastructures for customers who require higher performance or
data isolation", with the option to "charge customers a higher rate to use a single-tenant
deployment". A long tail on one shared scheduler and a handful of whales on their own is not a
compromise; it is the recommended answer.

### Blast radius and noisy neighbours are different problems

They are easy to conflate and have different remedies. Noisy neighbours are a *capacity* problem —
one tenant consuming a disproportionate share — and the fix is resource governance: quotas,
throttling, priorities. Blast radius is a *failure* problem, and the fix is partitioning into
independent failure domains: cells, stamps, separate deployments.

Microsoft's Noisy Neighbor guidance is that this is "a resource governance problem" to be met with
"usage quotas, throttling, and governance controls" — plus one piece of scheduling advice worth
lifting wholesale: "Consider whether you have background processes or resource-intensive workloads
that aren't time-sensitive. Run these workloads asynchronously at off-peak times to preserve your
resource capacity for time-sensitive workloads." For a scheduler, that is a cron expression, and it is
often cheaper than any isolation mechanism.

Blast radius is what cells and stamps address, and it is the reason the deployment-stamp guidance
insists you "deploy at least two stamps of your solution. If you deploy only a single stamp, you can
easily hard-code assumptions into your code or configuration that don't apply when you scale out."
The Quartz.NET equivalent: if you intend ever to run more than one scheduler, run two from the start.

### Cost per idle tenant

Worth checking explicitly, because scheduling workloads are unusually sparse — a tenant with one
nightly job is idle 99.9% of the time, and a model that charges for existence rather than execution
scales badly against that shape.

The managed services divide cleanly. EventBridge Scheduler charges per invocation, so a dormant
schedule is free. Google Cloud Scheduler charges "$0.10 per job per month" for the job's existence,
and "A paused job is counted as a job" — with a free tier of three jobs *per billing account*, not per
project, so it does not scale with tenant count. Azure's Durable Task Scheduler is free when idle on
Consumption and charges per capacity unit on Dedicated.

Quartz.NET has no billing model, but it has the same asymmetry in resources. A tenant that is a group
costs a row. A tenant that is a scheduler costs a scheduling loop that wakes on its own idle timer
whether or not it has work, plus its pools. Multiply by the number of dormant tenants before choosing.

### Observability

Ask whether you can answer "what is tenant X doing right now?" without a deployment, and whether
doing so will bankrupt your metrics backend.

Under a group-per-tenant model the tenant is a dimension you already have, which is a strong argument
for making the group the raw tenant id rather than a decorated string. Under a scheduler-per-tenant
model the scheduler name is that dimension.

The trap is cardinality. Job and trigger names are per job and per trigger; multiplying them by a
tenant dimension produces a series per tenant per trigger. Drop the name tags in a view before they
reach the backend unless you know you need them.

The group is also not automatically everywhere you might assume. It is a tag on Quartz.NET's execution
traces on both versions, and on metrics on 4.x — 3.x publishes no metrics at all — but neither version
puts it into a logging scope, so per-tenant log correlation is something you add.

## Mapping the axes onto Quartz.NET

Quartz.NET gives you three separations. They compose, and the useful designs use more than one.

**The scheduler** is the strongest boundary in the process. Each one owns its job store, thread pool,
listeners, plugins, calendars and — with a persistent store — its own connection pool and cluster
check-in. In silo/pool terms, a scheduler per tenant is siloed compute.

**The group** is the group half of every `JobKey` and `TriggerKey`. It is a logical partition, not an
enforced one: nothing stops a job in group `acme` from touching group `initech`. What it buys is that
every matcher-taking API becomes tenant-scoped — listing, pausing, resuming, deleting — and that the
group is already a tag on the execution traces. In silo/pool terms, a group per tenant is pooled
compute with a tenant discriminator, which is the same trade Temporal recommends when it steers you
to task queues over namespaces.

**`SCHED_NAME`** is the database-level separation. Every Quartz table has it as the first column of
its primary key and every statement filters on it, so two schedulers with different names share
tables without seeing each other's rows. That is a property of the schema rather than of the code
paths — the same design Azure's SQL backend uses for task hubs. The table prefix is a second, coarser
axis: separate table *sets* in one database, which is a backup, restore and permissions decision
rather than an isolation one.

### Which mechanism exists on which version

| Mechanism | 3.x | 4.x |
|---|---|---|
| Multiple schedulers in one process | Yes | Yes |
| Named schedulers through Microsoft DI | Yes, `AddQuartz(name, …)` | Yes, `AddQuartz(name, …)` |
| Resolving one by name | `ISchedulerRepository` | Keyed `IScheduler`, or `ISchedulerRepository` |
| Groups and group matchers | Yes, `GroupMatcher<T>` | Yes, plus the paged query API |
| `SCHED_NAME` row separation | Yes | Yes |
| Per-scheduler table prefix | Yes | Yes |
| Startup schema validation | Yes, `PerformSchemaValidation` on by default | Yes, `PerformSchemaValidation` on by default |
| Listing tenants without starting them | No — the repository lists live schedulers only | Yes, `ISchedulerRegistry.QuerySchedulers()` |
| Execution groups and per-node limits | Yes | Yes |
| Trigger group as the execution group | No — tag every trigger explicitly | Yes, `UseTriggerGroupWhenUnset()` |
| Cluster-wide concurrency quota | No | Yes, `ExecutionLimitScope.Cluster` — approximate unless `AcquireTriggersWithinLock` |
| Rate limiting (N per window) | No | No |
| Node affinity (persisted, cluster-aware) | Yes, `WithPreferredNode` | Yes, `WithPreferredNode` |
| Preparing the job's DI scope | Subclass and override `ConfigureScope` | `ConfigureJobScope(…)` delegate |
| Per-scheduler health check | No — one check, on the default scheduler | Yes, `AddQuartzHealthChecks` per scheduler |
| Metrics | No | Yes |
| Runtime tenant onboarding without a container | Yes, `StdSchedulerFactory` / `DirectSchedulerFactory` | Yes, `QuartzSchedulerBuilder` |

Both trees carry the mechanics in full:

- [Multi-Tenancy (Quartz 4.x)](quartz-4.x/multi-tenancy.md)
- [Multi-Tenancy (Quartz 3.x)](quartz-3.x/multi-tenancy.md)

### Choosing

Work down this list and stop at the first that applies.

1. **Tenants' data must be physically separate, or must live in a particular place.** A database per
   tenant, and therefore a scheduler per tenant, because a job store binds to one data source. This is
   the silo, with the silo's onboarding cost.
2. **A few tenants need isolation and the rest do not.** The bridge: one shared scheduler with a group
   per tenant for the long tail, and a dedicated scheduler for each tenant that bought isolation. This
   is the most common shape for a SaaS and the one to reach for by default when the answer is not
   obviously 3 or 4.
3. **Tenants arrive while the process is running, and there are many of them.** Group per tenant.
   Onboarding is a `ScheduleJob` call; there is no registration, no restart, and no per-tenant
   resource cost beyond the rows.
4. **Tenants are few, known at deployment, and differ in configuration.** Scheduler per tenant, named,
   with per-scheduler options and health checks.

Then, whichever you chose, decide the database question separately: one `SCHED_NAME` per tenant is
usually enough, a table prefix per tenant if backup or permissions demand separate tables, and a
separate database only if the data must not sit beside another tenant's.

## What Quartz.NET does not give you

A recommendation that oversells is worse than none, so these are the things multi-tenant deployments
ask Quartz.NET for and do not get. They apply to both 3.x and 4.x unless noted.

**Concurrency limits are per node unless you say otherwise, and on 3.x that is the only option.**
Execution groups cap how many threads a category of work may use, and by default the running count
lives in a dictionary in memory on the scheduler thread: nothing is persisted, nodes do not coordinate,
and a group limited to 3 can run up to 3×N across an N-node cluster. This is the same trap Celery
documents for `rate_limit`, and the same one Hangfire only escapes in a paid add-on that coordinates
through storage. On 3.x the closest approximation is dividing the cap by the node count, which is wrong
whenever a node is down.

4.x adds the real thing: `ForGroup("acme", 8, ExecutionLimitScope.Cluster)` is counted from
`QRTZ_FIRED_TRIGGERS`, which is already the cluster's reservation ledger. Read what it promises before
relying on it — the ceiling holds within one acquisition round and can transiently overshoot by
`(nodes − 1) × batchSize` unless `AcquireTriggersWithinLock` is on, and it fails closed, so a node that
cannot reach the store fires nothing rather than firing unmetered.

**There is no rate limiting.** Execution limits cap *concurrency*, not throughput. "This tenant may
run 100 jobs an hour" is not something Quartz.NET can express; build it in the job, or in the thing
the job calls.

**A starved group misfires; it does not queue.** When a group is at its limit its triggers are
skipped during acquisition and left where they are. They keep their original next fire time, so if
the starvation lasts longer than the misfire threshold the ordinary misfire machinery claims them,
and the trigger's misfire instruction — not the limit — decides whether the occurrence is skipped or
rescheduled. Set per-tenant limits with that in mind, and pick misfire instructions deliberately for
triggers in limited groups.

**Job types are not keyed by scheduler.** Jobs are resolved from the one container by type, with no
scheduler key, so two schedulers in one container cannot have different implementations of the same
job type. On 4.x, `AddJob<T>` registers the type with `TryAdd` semantics and the first registration
wins, silently and without a warning; on 3.x nothing is registered for you at all and whatever the
application registered is what every scheduler gets. Keying the *job type* does not help either — the
factory looks it up unkeyed and silently falls back to direct activation. Give each tenant its own job
type, or — far better — use one job type that reads its tenant from the firing and resolves what it
needs, by key if you like, inside `Execute`.

**Nothing stops a job reaching another tenant's data.** Groups are a naming partition. In AWS's terms
Quartz.NET gives you partitioning, and isolation is your application's job — a tenant id read from
the firing and threaded through every query, not a boundary the scheduler enforces.

**Dashboard and HTTP API authorization is per process, all or nothing.** The dashboard has a single
authorization policy and a single read-only flag on both versions, and 4.x's HTTP API serves every
scheduler in the container behind one uniformly-applied policy, with the scheduler named in the route.
There is no per-scheduler policy and no scheduler-name claim check. If tenants must reach their own
scheduler and not each other's, enforce that outside Quartz.NET — a process per tenant, or middleware
that authorizes on the scheduler-name route segment.

**A shut-down scheduler cannot be restarted.** `Standby()` / `Start()` is the pause-and-resume pair.
Shutdown is terminal: the scheduler refuses to start again and every other operation throws. You can
build a *new* scheduler with the same name, but it is a new one, not a revived one.

**The tenant does not reach your logs by itself.** The job and trigger group are tags on the execution
traces, and on 4.x's metrics, but neither version puts them into a logging scope; if you want
per-tenant log correlation you add it. Note also that a tenant carried only in an *execution* group is
invisible to those signals — one more reason to make the trigger group the tenant.

### Onboarding a tenant while the process runs

Under the group-per-tenant model this is a non-question: scheduling a job for a new group is an
ordinary API call.

Under the scheduler-per-tenant model, the DI path is closed once the container is built —
`AddQuartz` mutates `IServiceCollection`, and the hosted service enumerates schedulers once at
start. But that is a limit of the DI path, not of the library. Both versions can build a scheduler at
runtime outside the container: 3.x through `StdSchedulerFactory` or `DirectSchedulerFactory`, and 4.x
through `QuartzSchedulerBuilder`, which creates and owns a container of its own.

What that costs you is worth knowing before you build on it: a scheduler created this way gets no
hosted-service lifetime (you start and dispose it), its jobs resolve from its own container rather
than the application's unless you give it a job factory that bridges, and it is not covered by health
checks registered at startup. The per-version guides spell out the API and the trade-offs.

## Anti-patterns

**Smuggling the tenant into a job name and parsing it back out.** `JobKey("nightly-report-acme")`
looks harmless until something needs every job for a tenant, and the only way to get it is to fetch
every key and split strings. The group half of the key exists for this; use it, and keep the name for
what the job *is*. This is the same mistake as encoding a tenant in a Redis key when the system offers
a database — and Sidekiq's history is what happens next.

**One scheduler per tenant at thousands of tenants.** Each scheduler is a scheduling loop that wakes
on its own timer, a thread pool, a connection pool and a cluster check-in, whether or not that tenant
has anything to run. This is AWS's silo scaling problem in miniature, and their guidance holds: fine
at twenty, a serious operational burden at a thousand. Groups scale where schedulers do not.

**A shared database with a mismatched table prefix.** Nothing derives the prefix from the DDL or the
DDL from the prefix; you run the scripts with the prefix substituted. A prefix pointing at tables
that do not exist is caught at startup — `PerformSchemaValidation` is on by default on both versions,
and it names the missing table rather than letting the first failing operation surface an hour later.
What validation cannot catch is a prefix pointing at tables that *do* exist and belong to another
tenant: that configuration is indistinguishable from a correct one, and it will run happily on the
wrong data. Derive the prefix from the tenant id in code rather than pasting it into per-environment
configuration.

**Two unrelated schedulers sharing a database with the same `SCHED_NAME`.** By construction they are
indistinguishable from two nodes of one cluster, because that is exactly what they look like to the
schema. They will steal each other's triggers. Duplicate-name checks protect you only within one
container; across processes the name is a contract you keep. Microsoft hit the same problem with task
hubs and solved it by deriving the default name from the app name — a good idea to copy, by deriving
the scheduler name from the tenant id rather than from a configuration file someone can copy-paste.

**Assuming a per-node limit is a per-cluster limit.** Covered above, and repeated here because it is
the failure that only shows up in production, after the second node is added. It is still the default
on 4.x — a limit is cluster-wide only when it says `ExecutionLimitScope.Cluster`.

**Letting per-tenant metrics multiply without a view.** A tenant dimension times a trigger-name
dimension is a series per tenant per trigger. Aggregate before the data leaves the process.

## See also

- [Multi-Tenancy (Quartz 4.x)](quartz-4.x/multi-tenancy.md) — the 4.x mechanics in full
- [Multi-Tenancy (Quartz 3.x)](quartz-3.x/multi-tenancy.md) — the 3.x mechanics in full
- [Best Practices](best-practices.md) — including why never to point two non-clustered schedulers at one database
- [Troubleshooting](troubleshooting.md)

## Sources

The prior-art survey above is drawn from these primary sources, read in August 2026.

- AWS, [SaaS Architecture Fundamentals: Tenant isolation](https://docs.aws.amazon.com/whitepapers/latest/saas-architecture-fundamentals/tenant-isolation.html) and [SaaS Lens: Silo, Pool, and Bridge Models](https://docs.aws.amazon.com/wellarchitected/latest/saas-lens/silo-pool-and-bridge-models.html)
- AWS, [SaaS Tenant Isolation Strategies](https://docs.aws.amazon.com/whitepapers/latest/saas-tenant-isolation-strategies/silo-isolation.html) — silo, pool and bridge pros and cons
- AWS, [Reducing the Scope of Impact with Cell-Based Architecture](https://docs.aws.amazon.com/wellarchitected/latest/reducing-scope-of-impact-with-cell-based-architecture/what-is-a-cell-based-architecture.html)
- Microsoft, [Tenancy models to consider for a multitenant solution](https://learn.microsoft.com/azure/architecture/guide/multitenant/considerations/tenancy-models), [Architectural approaches for compute](https://learn.microsoft.com/azure/architecture/guide/multitenant/approaches/compute), [Deployment Stamps pattern](https://learn.microsoft.com/azure/architecture/patterns/deployment-stamp) and the [Noisy Neighbor antipattern](https://learn.microsoft.com/azure/architecture/antipatterns/noisy-neighbor/noisy-neighbor)
- Temporal, [Multi-tenant patterns](https://docs.temporal.io/production-deployment/multi-tenant-patterns), [Namespaces](https://docs.temporal.io/namespaces) and [Managing namespaces](https://docs.temporal.io/best-practices/managing-namespace)
- Cadence, [Workflow ID-based rate limits](https://cadenceworkflow.io/blog/2024/09/05/workflow-specific-rate-limits)
- Kubernetes, [Multi-tenancy](https://kubernetes.io/docs/concepts/security/multi-tenancy/) and [CronJob](https://kubernetes.io/docs/concepts/workloads/controllers/cron-jobs/)
- Apache Airflow, [Security model](https://airflow.apache.org/docs/apache-airflow/stable/security/security_model.html), [Multi-team](https://airflow.apache.org/docs/apache-airflow/stable/core-concepts/multi-team.html), [Pools](https://airflow.apache.org/docs/apache-airflow/stable/administration-and-deployment/pools.html) and [AIP-1](https://cwiki.apache.org/confluence/pages/viewpage.action?pageId=89066609), [AIP-44](https://cwiki.apache.org/confluence/display/AIRFLOW/AIP-44+Airflow+Internal+API), [AIP-72](https://cwiki.apache.org/confluence/display/AIRFLOW/AIP-72+Task+Execution+Interface+aka+Task+SDK)
- AWS, [EventBridge Scheduler quotas](https://docs.aws.amazon.com/scheduler/latest/UserGuide/scheduler-quotas.html) and [schedule groups](https://docs.aws.amazon.com/scheduler/latest/UserGuide/managing-schedule-group.html)
- Microsoft, [Durable task hubs](https://learn.microsoft.com/azure/durable-task/common/durable-task-hubs)
- Celery, [Tasks: `Task.rate_limit`](https://docs.celeryq.dev/en/stable/userguide/tasks.html)
- Hangfire, [Concurrency and rate limiting](https://docs.hangfire.io/en/latest/background-processing/throttling.html), [Configuring queues](https://docs.hangfire.io/en/latest/background-processing/configuring-queues.html), [Using the dashboard](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) and issue [#879](https://github.com/HangfireIO/Hangfire/issues/879)
- Quartz (Java), [Multiple schedulers cookbook](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/cookbook/MultipleSchedulers.html), [JDBC-JobStore clustering](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/configuration/ConfigJDBCJobStoreClustering.html) and [tutorial lesson 2](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/tutorials/tutorial-lesson-02.html)
- BullMQ, [Rate limiting](https://docs.bullmq.io/guide/rate-limiting) and [Pro: Groups](https://docs.bullmq.io/bullmq-pro/groups)
- Sidekiq, [7.0 upgrade notes](https://github.com/sidekiq/sidekiq/blob/main/docs/7.0-Upgrade.md) and [Storing data with Redis](https://www.mikeperham.com/2015/09/24/storing-data-with-redis/)
- Quartz (Java), [Configuration reference](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/configuration/ConfigMain.html)
