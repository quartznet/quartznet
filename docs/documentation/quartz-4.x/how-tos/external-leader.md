---

title: 'Running under an External Leader Election'
---

# Running under an External Leader Election

Some applications already know which of their processes is in charge. A Kubernetes `Lease`, Wolverine's
leader-pinned agents, a Consul or etcd session, a database advisory lock, a message bus that only starts
one consumer — the election exists, it gates other singletons, and the operations team already watches it.
Running Quartz's own clustering underneath it means two elections deciding overlapping questions.

This page describes the alternative: **a persistent job store with clustering off, exactly one process
scheduling at a time, started and stopped by somebody else's election**. It is a real topology with real
edges, and the edges are the reason it needs writing down.

::: warning This is not the default answer
[Clustering](../tutorial/advanced-enterprise-features.md) is how Quartz runs on more than one node, and it
is what to use unless you can name why you are not. It gives failover recovery of a dead node's firings,
which nothing on this page does, and it lets every node do work rather than leaving all but one idle.
Choose an external election when the election already exists and must gate several components together,
or when it is the only thing your platform can offer.
:::

## Not clustering, and staying that way

Clustering is spelled by calling `UseClustering()`. **Not clustering is spelled by not calling it** —
not by calling it and turning it off:

```csharp
// Refused at startup
q.UsePersistentStore(store => store.UseClustering(c => c.Enabled = false));
```

`UseClustering` does two things: it sets `ClusteringOptions.Enabled`, and it sets
`AdoJobStoreOptions.UseDbLocks`, because clustering has never worked without database locking. Turning
`Enabled` back off inside the callback — or in a later `Configure<ClusteringOptions>` — leaves the store
with database locking on, no cluster manager and no check-in row: a configuration nobody means to write.
`ClusteringStaysEnabledValidator` refuses it, and because `ClusteringOptions` is registered with
`ValidateOnStart`, it fails as the host starts rather than at the first firing.

It is scoped to the scheduler that asked, so a sibling scheduler in the same container legitimately runs
un-clustered.

What a non-clustered persistent store does not do, and this page assumes you know:

* it writes no `QRTZ_SCHEDULER_STATE` row and runs no check-in, so `QueryClusterNodes()` returns nothing;
* it never runs the failover sweep, so no peer takes over a dead node's firings;
* it takes its `TRIGGER_ACCESS` lock **in process**, through `InProcessLockHandler`, which excludes the
  threads of one scheduler and nothing else.

That last one is what makes the election load-bearing rather than an optimisation. See
[Two leaders at once](#two-leaders-at-once).

## Building it

<!-- snippet: sample_external_leader_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store => store.UseSqlServer(connectionString));

    // No UseClustering(). Exactly one process is meant to be scheduling, and the election
    // outside Quartz is what says which one.
});

builder.Services.AddQuartzHostedService(options =>
{
    // Built, initialized and bound with the host - and then left alone. The leader starts it.
    options.AutoStart = false;

    // A leader that is stepping down because the host is stopping should finish what it began.
    options.WaitForJobsToComplete = true;
});

builder.Services.AddHealthChecks().AddQuartz();
```
<!-- endSnippet -->

`AutoStart = false` has the hosted service build, initialize and bind the scheduler with the host and then
leave it in `Created` for the application to start. It is not the same as omitting the hosted service:
shutdown still runs, so the scheduler is stopped cleanly whether or not it was ever started. The
[hosted service page](../packages/hosted-services-integration.md#a-scheduler-the-application-starts-itself)
has the rest of that setting.

## Starting and standing down

<!-- snippet: sample_external_leader_callbacks -->
```csharp
/// <summary>
/// The two callbacks every leader election has, whatever it calls them.
/// </summary>
public sealed class SchedulerLeadership(IScheduler scheduler)
{
    public ValueTask OnStartedLeading(CancellationToken cancellationToken)
    {
        // The first acquisition starts the scheduler and every later one resumes it from standby.
        // Start does both, and does nothing when the scheduler is already running.
        return scheduler.Start(cancellationToken);
    }

    public ValueTask OnStoppedLeading(CancellationToken cancellationToken)
    {
        // Standby, not Shutdown: a shut-down scheduler cannot be started again, and this process
        // may well be elected once more in a minute. Losing the lease while the host is already
        // stopping is ordinary, and Standby throws once the scheduler has shut down.
        return scheduler.Status == SchedulerStatus.Running
            ? scheduler.Standby(cancellationToken)
            : default;
    }
}
```
<!-- endSnippet -->

Wire those two to whatever your election calls them: `OnStartedLeading`/`OnStoppedLeading` on the
Kubernetes client's `LeaderElector`, the start and stop of a Wolverine agent, `PostCreate`/`PreStop` on a
MassTransit bus observer, the acquire and release callbacks of a distributed lock.

Three things decide the shape:

* **`Start` is idempotent and resumes from standby.** The first call starts the scheduler; every later
  one resumes it. Only the first runs the store's start-up recovery and starts the plugins, so a
  re-election is cheap.
* **`Standby` is not `Shutdown`.** Shutdown is terminal — a shut-down scheduler cannot be started again,
  and `Start` after it throws *"The Scheduler cannot be restarted after Shutdown() has been called."*
  Standby is reversible, which is what a leadership that can come back needs.
* **`Standby` after shutdown throws**, with `SchedulerException("The Scheduler has been Shutdown.")`.
  Losing a lease while the host is already stopping is ordinary, so read `Status` before standing down
  rather than catching the exception.

`SchedulerStatus` replaces 3.x's `IsStarted` / `InStandbyMode` / `IsShutdown` triple: `Created`,
`Running`, `Standby`, `ShuttingDown`, `Shutdown`. A scheduler that has never been started stands down to
`Created`, not `Standby`, because "never started" is the more precise answer.

### What standby does, and what it does not

Standby pauses the scheduling loop and tells the job store the scheduler is paused. It does **not**:

* **stop a firing that is already in flight.** Running jobs are untouched, by design — a job that must end
  on request watches `IJobExecutionContext.CancellationToken`.
* **release triggers this node has already acquired.** The loop asks for triggers due within the next
  `IdleWaitTime` and then waits out the first one's fire time; standing down in the middle of that wait
  does not abandon it. So a node that has just stood down can still fire up to `MaxBatchSize` triggers,
  as late as `IdleWaitTime` after it stopped leading. Only shutdown releases an acquired batch.
* **stop the misfire handler.** It is started on the scheduler's first start and stopped only by shutdown,
  and it reads no pause flag. A stood-down leader keeps scanning for misfires on
  `MisfireHandlerFrequency` — defaulting to `MisfireThreshold`, one minute for the ADO store — and keeps
  writing trigger state while it does.

The last of those is the one that surprises people: **standby means "not acquiring", not "not touching the
database"**. A process that has never been elected at all is genuinely inert, because the misfire handler
does not exist until the first `Start()`. A process that led once and stood down is not.

The health check follows the same distinction. It reports *degraded* — not *unhealthy* — both while a
scheduler sits in `Created` with `AutoStart = false` and while it is in `Standby`, because in both cases it
is doing exactly what it was configured to do. A non-leader replica therefore stays in rotation for
traffic it can still serve; see [Health checks](../packages/hosted-services-integration.md#health-checks).

## What the loop costs while it waits

Three settings decide how quickly a due trigger is noticed and how much database traffic the waiting
costs. Their defaults are tuned for a cluster of ordinary size, and an integration that also polls
something else will want to move them:

| Setting | Default | What it decides |
|---|---|---|
| `QuartzSchedulerOptions.IdleWaitTime` | 30 seconds | How long the loop waits before asking the store again when it found nothing. Must be at least one second. |
| `QuartzSchedulerOptions.MaxBatchSize` | 1 | The upper bound on triggers acquired per round. Must not exceed `ThreadPoolOptions.MaxConcurrency`. |
| `QuartzSchedulerOptions.BatchTriggerAcquisitionFireAheadTimeWindow` | `TimeSpan.Zero` | How far past the current time a trigger may fire in order to join the batch that is already forming. |

**`IdleWaitTime` is not the firing latency for anything scheduled in this process.** Every scheduling call
made through this scheduler signals the loop, which cuts the wait short immediately: schedule a trigger for
five seconds' time and it fires in five seconds, whatever `IdleWaitTime` says. What the wait bounds is the
pickup of a trigger written by **another process** — an API node inserting into the same tables, a
migration, a hand-written row. There is no cross-process wakeup: nothing signals this loop from outside it.

Two details worth knowing before choosing a number. The idle wait is randomized into
`[0.8 × IdleWaitTime, IdleWaitTime)`, so several nodes coming up together do not synchronize their polls.
And a round acquires triggers due within the next `IdleWaitTime`, so a longer wait is not simply a longer
blind spot — it is a wider look-ahead as well, and a trigger that came due during the sleep is picked up
late rather than lost.

`MaxBatchSize` and the fire-ahead window are one setting in two halves. Raising the batch size alone leaves
the effective batch at one trigger for any schedule whose fire times are spread out, because a batch stops
at the first trigger not due within the window of the one that opened it. Raising the window alone gives a
batch nothing to grow into. Move them together, or neither:

<!-- snippet: sample_external_leader_tuning -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.ConfigureScheduler(options =>
    {
        // How long a trigger written by another process may sit before this one looks again.
        options.IdleWaitTime = TimeSpan.FromSeconds(5);

        // Both halves or neither: a batch stops at the first trigger that is not due within
        // the window of the one that opened it.
        options.MaxBatchSize = 10;
        options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(2);
    });

    // MaxBatchSize may not exceed this: triggers acquired beyond the number of threads there
    // are to run them on are held by this node, unfireable by any other, until the pool drains.
    q.UseDefaultThreadPool(maxConcurrency: 10);
});
```
<!-- endSnippet -->

Each half has a cost. A batch size above one makes every acquisition round take the `TRIGGER_ACCESS` lock,
including the rounds that acquire nothing — which is why the default is the one value that needs no lock at
all. A wide fire-ahead window fires triggers early by up to that much, and widens the window in which a
batch this node has acquired but not yet fired is unavailable to anyone else. In this topology that second
cost is larger than it looks: nothing recovers an acquired batch from a process that died holding it until
that scheduler starts again, because there is no peer running the failover sweep.

## When the leader moves

A new leader starting a non-clustered ADO.NET store runs the start-up recovery pass under the trigger-access
lock, and it is worth knowing exactly what that pass does, because it is all that happens:

1. **Every trigger of this scheduler in `ACQUIRED` or `BLOCKED` goes back to `WAITING`**, and every
   `PAUSED_BLOCKED` back to `PAUSED`. This is scoped by scheduler name, not by instance, so it frees what
   the previous leader left behind whoever that was. This is what un-sticks the schedule.
2. **Every misfire is resolved, with no batch limit.** The `MaxMisfiresToHandleAtATime` cap — 20 by
   default — bounds the background handler, not this pass, so a store that was leaderless for hours does
   not have to catch up twenty triggers at a time.
3. **Jobs marked for recovery are re-scheduled**, from the fired-trigger rows carrying
   `REQUESTS_RECOVERY`. Each becomes a `recover_<instanceId>_<n>` trigger in the
   `SchedulerConstants.DefaultRecoveryGroup` group, starting at the firing's scheduled time with
   `IgnoreMisfires`, carrying the original trigger's job data plus the four `QRTZ_FAILED_JOB_ORIG_*`
   entries. The job sees `IJobExecutionContext.Recovering`.
4. **Lingering `COMPLETE` triggers are deleted, and then every fired-trigger row of this scheduler is
   deleted** — all of them, with no instance filter.

Step 3 is the one with a condition on it, and step 4 is the one that makes the condition unforgiving.

### The instance id decides whether recovery survives a leader move

Step 3 selects fired-trigger rows whose `INSTANCE_NAME` is *this store's own instance id*. Step 4 then deletes
every row regardless of instance. So a new leader that presents a **different** instance id from the one
that died finds nothing to recover, and then deletes the evidence: jobs with `RequestsRecovery` are
silently not re-run. Scheduling still resumes correctly, because steps 1 and 2 are scoped by scheduler
name — only the recovery is lost.

For a non-clustered store you cannot get this wrong by accident, and you can get it wrong on purpose:

* `InstanceId` defaults to `"NON_CLUSTERED"`, which is the same string in every process. Recovery works
  across a leader move, because both leaders present it.
* Asking for a generated id — `GenerateInstanceId`, or the flat `quartz.scheduler.instanceId = AUTO`, or
  any `UseInstanceIdGenerator(...)` — is **ignored** for a non-clustered store, which returns
  `"NON_CLUSTERED"` regardless. A generated id buys nothing for a store that shares its database with
  nobody.
* Setting a **literal** `InstanceId` — the pod name, a host name, anything per-process — is honoured, and
  it is what breaks recovery across a leader move. In this topology, do not.

That is the opposite of the advice for a cluster, where every node must have an id of its own; see
[Naming a node in a container](../operations.md#naming-a-node-in-a-container). Here the id is not
identifying a node, it is identifying *the leader*, and there is only ever meant to be one.

The jobs that want any of this have to ask:

<!-- snippet: sample_external_leader_request_recovery -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ReportingJob>(j => j
        .WithIdentity("nightly-close", "reporting")
        .RequestRecovery()
        .StoreDurably());
});
```
<!-- endSnippet -->

::: tip Recovery is a re-run, not a resume
A recovered firing starts the job again from the beginning. Whether that is safe is the job's problem, not
the scheduler's — which is the same requirement idempotency imposes below, arrived at from the other side.
:::

## Two leaders at once

Leader election gives mutual exclusion when it is working, and every election has a window in which it is
not: a lease that expired while its holder was paused by a long garbage collection or a frozen container, a
clock that drifted, a partition that healed. This is the fencing problem, it is not specific to Quartz, and
the systems that implement election say so themselves. Kubernetes' `client-go` leader election states in
its own package documentation that "this implementation does not guarantee that only one client is acting
as a leader (a.k.a. fencing)". Apache Curator's tech note walks the case in detail — a three-second session
timeout against a ten-second GC pause — and concludes, in its capitals, that "**BOTH CLIENT A AND CLIENT B
WILL BELIEVE THEY ARE THE LOCK HOLDER**".

The published remedy is a fencing token: the resource itself rejects work carrying a stale one. Some
backends can supply one — etcd's revision number, Consul's `LockIndex` sequencer — and it does not help
here, because **a token only fences if the resource validates it**. etcd's own documentation makes the
point for us: the resources to be protected "must provide the version number validation mechanism", and
its lock "cannot be used for protecting external resources". The `QRTZ_*` schema has no epoch column and no
statement that compares one, so there is nothing for a token to be checked against. And a non-clustered
store takes its `TRIGGER_ACCESS` lock in process, so two schedulers against one set of tables exclude each
other not at all.

What that costs, precisely, if a second process starts while the first still believes it leads:

* The newcomer's start-up recovery resets the incumbent's `ACQUIRED` triggers to `WAITING`, so both nodes
  can acquire and fire them.
* Both nodes carry the same `"NON_CLUSTERED"` instance id, so the newcomer's step 3 reads the incumbent's
  *in-flight* firings as its own to recover, and schedules recovery triggers for jobs that are still
  running.
* Its step 4 deletes the incumbent's fired-trigger rows, so the incumbent's own completions have nothing
  left to clean up, and its firings are no longer visible to `QueryFireInstances`.
* `[DisallowConcurrentExecution]` is honoured within a scheduler and not between two of them.

None of it corrupts a job or a trigger definition; all of it double-fires and mis-reports. So:

* **Write the jobs to tolerate running twice.** This is the mitigation. It is not a fallback for a
  badly-configured election, it is the requirement the topology carries.
* **Give the departing leader time to finish.** Standby leaves in-flight jobs running and does not release
  an acquired batch, so the drain window matters: `WaitForJobsToComplete`, `HostOptions.ShutdownTimeout`,
  and a `terminationGracePeriodSeconds` longer than the longest job. See
  [Shutdown has a budget](../packages/hosted-services-integration.md#shutdown-has-a-budget). Consul is the
  one election here that builds the same idea into itself: its `LockDelay`, fifteen seconds by default,
  refuses re-acquisition for that long "to allow the potentially still live leader to detect the
  invalidation and stop processing" — and its documentation is candid that this is "not a bulletproof
  method".
* **Prefer an election that lives in the same database as the tables.** A lock taken with
  `pg_advisory_lock` or `sp_getapplock` on the connection that also writes `QRTZ_*` is one thing failing
  or holding, rather than two systems that can disagree. Note the asymmetry if you reach for advisory
  locks specifically: PostgreSQL documents that they "relate only to the server on which they are
  acquired", and a hot standby grants them too — so after a failover an old and a new primary can both
  hold the same lock with nothing raising an error.
* **If you find yourself adding safeguards, you are rebuilding clustering.** Turning on `UseDbLocks`
  without a cluster manager serializes the two processes' store operations, which stops them mis-reading
  each other's state — but it does not stop either of them scheduling, so both still fire. One connection,
  one database, one lock the store itself takes, is what `UseClustering()` already is; at that point it is
  the smaller change and the supported one.

## See also

* [Advanced Enterprise Features](../tutorial/advanced-enterprise-features.md) — clustering, which this page
  is the alternative to
* [Operating a Cluster](../operations.md) — check-in, failover and what the tables are telling you, for
  when you change your mind
* [Hosted Services Integration](../packages/hosted-services-integration.md) — `AutoStart`, the health
  check and the shutdown budget
* [Embedding Quartz in a Library](embedding-quartz-in-a-library.md) — the other half of this, for a package
  that must fit into an application it does not own
* [Quartz.NET with Wolverine](wolverine.md#letting-wolverine-start-the-scheduler) — one concrete election: a bus's
  leader-pinned agent pressing start on the scheduler
* [Configuration Reference](../configuration/reference.md#persistent-job-store) — every setting named here,
  with its default

## Sources

Prior art surveyed in August 2026. Quartz.NET's own behaviour is stated from the source in this
repository rather than from any of these.

* Kubernetes, [`client-go/tools/leaderelection`](https://pkg.go.dev/k8s.io/client-go/tools/leaderelection)
  — the fencing caveat, in the package's own words; the C# client's
  [`LeaderElector`](https://github.com/kubernetes-client/csharp) exposes the same
  `OnStartedLeading`/`OnStoppedLeading` shape
* Apache Curator, [Tech Note 10](https://curator.apache.org/docs/tech-note-10) — the GC-pause scenario in
  which two clients both hold the lock
* etcd, [Why etcd](https://etcd.io/docs/v3.6/learning/why/) — that a lease is not mutual exclusion, and
  that a revision number only fences a resource which validates it
* HashiCorp, [Consul sessions](https://developer.hashicorp.com/consul/docs/automate/session) — `LockDelay`
  and the sequencer
* PostgreSQL, [Hot Standby](https://www.postgresql.org/docs/current/hot-standby.html) — advisory locks
  relate only to the server that granted them
* madelson, [DistributedLock — Other topics](https://github.com/madelson/DistributedLock/blob/master/docs/Other%20topics.md)
  — the renewal-timeout risk, and why a lock sharing the protected database's connection is the shape that
  holds
* Hangfire, [Running multiple server instances](https://docs.hangfire.io/en/latest/background-processing/running-multiple-server-instances.html)
  — the competing-consumers alternative to electing anybody
