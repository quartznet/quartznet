---
title: 'A Job Store of Your Own'
---

# A Job Store of Your Own

`IJobStore` holds scheduling data. Quartz ships an in-memory store and an ADO.NET store; implement the public
interface to keep the data elsewhere (a document database, a key-value store, a service).

| You want to | Do this |
|---|---|
| Add behaviour around an existing store — logging, metrics, tenant routing, fault injection | derive from `DelegatingJobStore` |
| Support a **relational** database Quartz does not ship a dialect for | write an [`IDriverDelegate`](dialect-delegate.md), not a store |
| Keep scheduling data somewhere that is not a relational database | implement `IJobStore` directly |
| Manage transactions differently from either shipped ADO.NET store | implement `IJobStore` directly — see [The ADO.NET store is not a base class](#the-ado-net-store-is-not-a-base-class) |

## Decorating a store

`DelegatingJobStore` forwards every operation to another store; every member is `virtual`:

<!-- snippet: sample_custom_job_store_decorator -->
```csharp
public sealed class MetricsJobStore(IJobStore inner, IMeterFactory meters) : DelegatingJobStore(inner)
{
    private readonly Histogram<double> acquireDuration = meters
        .Create("App.Quartz")
        .CreateHistogram<double>("app.quartz.acquire.duration", "s");

    public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return await base.AcquireNextTriggers(request, cancellationToken);
        }
        finally
        {
            acquireDuration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds);
        }
    }
}
```
<!-- endSnippet -->

<!-- snippet: sample_custom_job_store_registering_a_decorator -->
```csharp
q.UseJobStore(sp => new MetricsJobStore(
    ActivatorUtilities.CreateInstance<RAMJobStore>(sp),
    sp.GetRequiredService<IMeterFactory>()));
```
<!-- endSnippet -->

`protected IJobStore InnerJobStore` reaches the real store through any number of layers. A store that keeps
data somewhere new implements `IJobStore` instead.

::: tip
The shipped stores cannot be derived from: `RAMJobStore` is sealed and the ADO.NET stores are internal.
`RAMJobStore` mutates several indexes under one lock in a fixed order and notifies after releasing it, which
no override could preserve. Wrap it instead.
:::

## Registering a store

All four overloads register a singleton, keyed by scheduler name for a named scheduler:

<!-- A listing of the four overloads rather than code, so it is written out here rather than
     compiled. -->

```csharp
q.UseJobStore<MyStore>();                          // container-constructed
q.UseJobStore<MyStore, MyStoreOptions>(o => …);    // plus its own options type
q.UseJobStore(existingInstance);                   // one you built
q.UseJobStore(sp => new MyStore(…));               // a factory, e.g. for a decorator
```

The generic forms construct the store with `ActivatorUtilities` from a *scheduler-scoped* view of the
container, so it behaves the same under a named scheduler. Take what you need:

<!-- An illustration of the constructor rather than a whole store, so it is written out here
     rather than compiled: the class as shown does not implement `IJobStore`. -->

```csharp
public sealed class DocumentJobStore(
    ISchedulerSignaler signaler,
    ITypeLoader typeLoader,
    TimeProvider timeProvider,
    IObjectSerializer serializer,
    IOptions<MyStoreOptions> options,
    ILogger<DocumentJobStore> logger) : IJobStore
{
    // ...
}
```

::: warning
Registration is `TryAdd`, so **first wins**. `UseInMemoryStore()` and `UsePersistentStore(…)` also register
a store: call `UseJobStore<MyStore>()` instead of them, not after.

A `TOptions` resolved through `IOptions<TOptions>` must keep its public parameterless constructor when the
application is trimmed.
:::

## Initialize and identity

<!-- A signature listing rather than code, so it is written out here rather than compiled. -->

```csharp
ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default);
```

Called once, after the scheduler is built and before plugins initialize. The constructor supplies the type
loader, signaler and time provider; `Initialize` supplies the identity, which is settled only after the
graph is built, and is where to verify a schema, open a connection or start a background scan.

`SchedulerIdentity` carries `SchedulerName` and `InstanceId`, both required. **Record the instance id against
the firings this node owns**, so `QueryFireInstances` can say which node runs what.

## The contract that is easy to get wrong

### The fire cycle

Once per acquisition batch, in order:

1. **`AcquireNextTriggers(TriggerAcquisitionRequest request, ct)`** — reserve triggers. Never return one
   firing later than `request.NoLaterThan`, or more than `request.MaxCount`.
2. **`TriggersFired(triggers, ct)`** — **return a list the same length as the input, index-aligned**; the
   caller reads `results[i]` against `triggers[i]`. Use `TriggerFiredResult.NotFired` for a trigger that
   should not fire after all and `TriggerFiredResult.Failed(exception)` for one that could not be processed.
3. **`TriggeredJobComplete(trigger, jobDetail, instruction, ct)`** — the firing is over. This releases a
   `[DisallowConcurrentExecution]` job's siblings, and is called even when the job never ran.
   `ReleaseAcquiredTrigger` is only for a trigger acquired and never fired.

`TimeSpan GetAcquireRetryDelay(int failureCount)` is called after repeated `AcquireNextTriggers` failures.
Return between 20 milliseconds and 10 minutes.

### Trigger state

Every store stores `StoredTriggerState` (nine members) and reports `TriggerState` through one function, so
stores agree:

<!-- snippet: sample_custom_job_store_trigger_state_resolver -->
```csharp
TriggerState reported = TriggerStateResolver.Resolve(stored, isExecuting);
```
<!-- endSnippet -->

* Precedence: **`None > Error > Paused > Executing > Blocked > Complete > Normal`**. Paused and error win
  because an operator must act on them; executing beats blocked to tell the running trigger from siblings
  gated behind it.
* An unrecognised stored value reads as `Waiting`, reported as `Normal`.
* A missing trigger reads as `Deleted`, reported as `TriggerState.None`.
* `StoredTriggerStates.ToStoredValue()` / `FromStoredValue()` map to and from the persisted strings.

### Queries

The six paged `Query…` members are abstract:

* **Order by group, then name, ordinal.** Fire instances add fire instance id as a third key, since one
  trigger can have several in flight.
* **`HasMore` is exact.** Read one row past `Take`.
* **`TotalCount` only when asked.** `Take = 0` with `IncludeTotalCount = true` skips the row query.

`QueryFireInstances` covers the cluster if firings are stored durably, otherwise this process.
`FireInstance.JobKey` is `null` while a firing is only `Acquired`.

### Cluster nodes

`QueryClusterNodes(ct)` returns `ClusterNode`s, unpaged:

* **The current node is always first, and the only one with `IsCurrentNode = true`**, whether or not the
  store has a record of it. The rest follow by instance id, ordinal.
* **A store without membership returns only that node**, `ClusterNodeState.Alive`, with `LastCheckInUtc` and
  `CheckInInterval` `null`, so callers need not check `Clustered` first.
* A store with membership lists every recorded node, including dead ones not yet swept, and decides
  `State` with **the same predicate as its failover pass**. `Overdue` is a missed check-in; `Failed` is
  when the store takes over the node's work.

### Bulk members

Key-set members such as `PauseJobs(keys)`, `ResumeTriggers(keys)` and `DeleteJobs(keys)` default to looping
the single-key member (one lock or round trip per key). Override those your store can do in one pass.

### Two properties that are answers, not settings

`bool Clustered` and `bool SupportsPersistence` are read-only: they describe what the store *is*. A store
that cannot cluster returns `false`.

## Narrowing what a node picks up

`TriggerAcquisitionRequest` is a record, so a `DelegatingJobStore` can rewrite it with `with`, for example to
take at most five triggers at a time:

<!-- snippet: sample_custom_job_store_acquisition_budget -->
```csharp
public sealed class BudgetedJobStore(IJobStore inner, int nodeBudget) : DelegatingJobStore(inner)
{
    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        return base.AcquireNextTriggers(
            request with { MaxCount = Math.Min(request.MaxCount, nodeBudget) },
            cancellationToken);
    }
}
```
<!-- endSnippet -->

::: warning The MaxCount rule
**Lower** `MaxCount`, never raise it. Lock-free or locked acquisition is chosen from the original request,
so a raised count is caught only afterwards and the surplus released and retried: a silent performance
cost, not corruption.
:::

The decorator runs on every acquisition attempt, so time-based rules (like the maintenance window below)
take effect without a restart.

### Excluding job types from acquisition

Set `ExcludedJobTypeNames` to decline whole job types. **Every shipped store honours it** before rows count
against `MaxCount`: the ADO.NET store in SQL, `RAMJobStore` by skipping candidates.

<!-- snippet: sample_custom_job_store_excluded_job_types -->
```csharp
public sealed class MaintenanceWindowJobStore(IJobStore inner, IMaintenanceWindow window)
    : DelegatingJobStore(inner)
{
    // JobType.FullName is the spelling the store persists - "Namespace.TypeName, AssemblyName".
    // Type.FullName carries no assembly name and would never match a stored row.
    private static readonly string reportingJobTypeName = new JobType(typeof(ReportingJob)).FullName;

    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Asked again on every acquisition, so a window that opens between two of them takes effect on
        // the next one without restarting anything.
        if (!window.IsOpen)
        {
            return base.AcquireNextTriggers(request, cancellationToken);
        }

        return base.AcquireNextTriggers(
            request with { ExcludedJobTypeNames = [reportingJobTypeName] },
            cancellationToken);
    }
}
```
<!-- endSnippet -->

* **Use `JobType.FullName`** (`Namespace.TypeName, AssemblyName`), the string in
  `TriggerAcquireResult.JobTypeName` and `JOB_CLASS_NAME`. `Type.FullName` lacks the assembly and never
  matches.
* **Matching is exact**, with no prefix or wildcard. In SQL it follows the `JOB_CLASS_NAME` collation; in
  memory it is ordinal.
* Rows written by Quartz 2.x or 3.x may use an older spelling, which is never rewritten and will not match.
* At most 1000 non-blank entries (Oracle's `IN` list limit), checked when the request is built.

## The ADO.NET store is not a base class

`AdoJobStoreBase`, `LocalTransactionJobStore` and `ExternalTransactionJobStore` are internal.
`quartz.jobStore.type` still names them, so configuration files need no change. In code, `UsePersistentStore()`
builds the local-transaction store and `store.UseAmbientTransactions()` inside its callback the other.

Deriving never worked well: each `protected` member below the two abstract ones is the connection-taking twin
of a public member (`AddJob(conn, …)` beside `AddJob(job, …)`); the public one locks and the twin does the
work, so overriding one changes half an operation.

| What you were overriding for | What to do |
|---|---|
| Narrowing acquisition | `DelegatingJobStore`, rewriting the request — see [Narrowing what a node picks up](#narrowing-what-a-node-picks-up) |
| Logging, metrics, tenant routing, fault injection | `DelegatingJobStore` — see [Decorating a store](#decorating-a-store) |
| A relational database Quartz ships no dialect for | [A Driver Delegate for a New Database](dialect-delegate.md) |
| Classifying one more of your driver's failures as retryable | `AdoJobStoreOptions.IsTransient` — see [What counts as transient](../operations.md#what-counts-as-transient) |
| A different transaction model from either shipped store | implement `IJobStore`; if the shipped stores nearly fit, [open an issue](https://github.com/quartznet/quartznet/issues) |

## Rebuilding jobs and triggers

A store that reads data back reconstructs `IJobDetail` and `IOperableTrigger`:

* **Jobs go through `JobBuilder`**, the only supported path (`JobDetailImpl` is internal), as in the ADO
  store.
* **Triggers can be constructed directly.** `Quartz.Impl.Triggers.*TriggerImpl` and the abstract
  `TriggerBase` are public, and all five are subclassable. Pair a subclassed trigger with a serializer
  derived from its public, unsealed built-in serializer; `BuiltInTriggerSerializerDerivationTest` guards
  both, in both JSON packages. See [Persisting a Custom Trigger Type](trigger-persistence-delegate.md).

## Testing one

* **Behaviour**: run a real scheduler over your store with `UseJobStore<MyStore>()` and assert through
  `IScheduler`; only this exercises the fire cycle's ordering.
* **The contract**: ordering, `HasMore` and the `Take = 0` count are testable on the store alone.
* **Fault handling**: wrap *your* store in a `DelegatingJobStore` to make one member fail.

See [Testing](../tutorial/testing.md).

## See also

* [Job Stores](../tutorial/job-stores.md) — the shipped stores and what they guarantee
* [A Driver Delegate for a New Database](dialect-delegate.md) — the right seam for a relational database
* [Querying Jobs and Triggers](../tutorial/querying-jobs-and-triggers.md) — the query contract, from the caller's side
