---
title: 'A Job Store of Your Own'
---

# A Job Store of Your Own

`IJobStore` is where scheduling data lives. Quartz ships two implementations — in memory, and ADO.NET
over a relational database — and the interface is public so a third can keep it somewhere else: a
document database, a key-value store, a service.

Before writing one, be clear about which of three jobs you are doing, because they have different
answers.

| You want to | Do this |
|---|---|
| Add behaviour around an existing store — logging, metrics, tenant routing, fault injection | derive from `DelegatingJobStore` |
| Support a **relational** database Quartz does not ship a dialect for | write an [`IDriverDelegate`](dialect-delegate.md), not a store |
| Keep scheduling data somewhere that is not a relational database | implement `IJobStore` directly |

## Decorating a store

`DelegatingJobStore` forwards every operation to another store, and every member is `virtual`:

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

`protected IJobStore InnerJobStore` reaches the real store through however many layers are in the way.

::: tip
The shipped stores are sealed, and decoration is why. `RAMJobStore` holds a lock while it mutates
several indexes in a fixed order and raises notifications after releasing it — none of which an
override can be asked to preserve. Wrap it, and change what you meant to change.
:::

A store that keeps scheduling data somewhere new should implement `IJobStore` directly rather than
derive from this.

## Registering a store

Four overloads, all singleton and all keyed by scheduler name for a named scheduler:

<!-- A listing of the four overloads rather than code, so it is written out here rather than
     compiled. -->

```csharp
q.UseJobStore<MyStore>();                          // container-constructed
q.UseJobStore<MyStore, MyStoreOptions>(o => …);    // plus its own options type
q.UseJobStore(existingInstance);                   // one you built
q.UseJobStore(sp => new MyStore(…));               // a factory, e.g. for a decorator
```

The generic forms construct the store with `ActivatorUtilities` through a *scheduler-scoped* view of
the container, so a store written against the scheduler's own collaborators behaves the same under a
named scheduler as under the default one. Take what you need:

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
Registration is `TryAdd`, so **first wins**. `UseInMemoryStore()` and `UsePersistentStore(…)` register a
store too — call `UseJobStore<MyStore>()` instead of them, not after them.

A `TOptions` resolved through `IOptions<TOptions>` must keep its public parameterless constructor when
the application is trimmed.
:::

## Initialize and identity

<!-- A signature listing rather than code, so it is written out here rather than compiled. -->

```csharp
ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default);
```

Nearly everything a store needs — the type loader, the signaler, the time provider — is supplied
through its constructor. What remains here is the scheduler's identity, which is not settled until the
container has built the graph, plus work that has to happen before the scheduler runs and cannot be
done during construction: verifying a schema, opening a connection, starting a background scan.

`SchedulerIdentity` carries `SchedulerName` and `InstanceId`, both required. **Record the instance id
against the firings this node owns**, so `QueryFireInstances` can say which node is running what.

It is called once, after the scheduler is built and before plugins initialize.

## The contract that is easy to get wrong

### The fire cycle

Three members run in a fixed sequence, once per acquisition batch:

1. **`AcquireNextTriggers(TriggerAcquisitionRequest request, ct)`** — reserve triggers for this node.
   Never return a trigger that would fire later than `request.NoLaterThan`, and never return more than
   `request.MaxCount`.
2. **`TriggersFired(triggers, ct)`** — the scheduler is about to run them. **The returned list must be
   the same length as the input and index-aligned with it.** The caller reads `results[i]` against
   `triggers[i]`. Return `TriggerFiredResult.NotFired` for a trigger that should not fire after all and
   `TriggerFiredResult.Failed(exception)` for one that could not be processed; both are handled, a
   ragged list is not.
3. **`TriggeredJobComplete(trigger, jobDetail, instruction, ct)`** — the firing is over. This is what
   releases a `[DisallowConcurrentExecution]` job's siblings, and the scheduler calls it even on paths
   where the job never ran. `ReleaseAcquiredTrigger` is only for a trigger that was acquired and never
   fired.

Also implement `TimeSpan GetAcquireRetryDelay(int failureCount)`, called when `AcquireNextTriggers`
fails more than once in succession. Return something between 20 milliseconds and 10 minutes.

### Trigger state

Every store keeps its triggers in one vocabulary — `StoredTriggerState`, nine members — and resolves
to the `TriggerState` callers see through one function, so two stores cannot report different states
for the same situation:

<!-- snippet: sample_custom_job_store_trigger_state_resolver -->
```csharp
TriggerState reported = TriggerStateResolver.Resolve(stored, isExecuting);
```
<!-- endSnippet -->

The precedence is **`None > Error > Paused > Executing > Blocked > Complete > Normal`**. Paused and
error outrank executing because they are the facts an operator has to act on, and both remain true
while a previously started execution finishes. Executing outranks blocked so that the trigger which
actually started the running job stays distinguishable from the siblings gated behind it.

Two more rules to inherit rather than reinvent:

- A stored value this version does not recognise reads as `Waiting`, and is reported as `Normal` —
  schedulable.
- A trigger that does not exist reads as `Deleted`, which resolves to `TriggerState.None`.

`StoredTriggerStates.ToStoredValue()` / `FromStoredValue()` map to and from the persisted strings, and
are public for exactly this.

### Queries

The six paged `Query…` members are abstract, and three rules keep them consistent with the shipped
stores:

- **Order by group, then name, ordinal.** Fire instances add fire instance id as a third key, because
  one trigger can have several firings in flight and group plus name would not order them.
- **`HasMore` is exact.** Read one row past `Take`.
- **`TotalCount` only when asked.** `Take = 0` with `IncludeTotalCount = true` must skip the row query
  entirely — that is the counting idiom.

`QueryFireInstances` answers for the whole cluster if the store keeps firings durably, and for its own
process otherwise, which is the whole of an in-memory store's world. `FireInstance.JobKey` is `null`
while a firing is only `Acquired` — the job is not loaded until it starts.

### Cluster nodes

`QueryClusterNodes(ct)` lists the scheduler nodes the store knows about, as `ClusterNode`s. It is not
paged — a cluster is a handful of nodes, not a data set — and two rules bind it:

- **The current node is always in the list, first, and is the only one with `IsCurrentNode = true`.**
  It is listed whether or not the store has a record of it yet. The rest follow by instance id, ordinal.
- **A store that keeps no membership answers with that one node**, `ClusterNodeState.Alive`, with
  `LastCheckInUtc` and `CheckInInterval` both `null`. That is the honest answer for a store that cannot
  cluster, and it means a caller never has to branch on `Clustered` before asking.

A store that *does* keep membership reports every node it has a record of, including ones that are dead
but not yet swept, and decides `State` with **the same predicate its own failover pass uses** — write
that once and call it from both, so the listing can never disagree with the recovery it predicts.
`Overdue` is a missed check-in and nothing more; `Failed` is the point at which the store takes the
node's work over.

### Bulk members

Many key-set members — `PauseJobs(keys)`, `ResumeTriggers(keys)`, `DeleteJobs(keys)` and so on — have
default interface implementations that loop the single-key member. Correct for any store, and one lock
or round trip per key. Override the ones your store can do in one pass, and keep the default for the
rest.

### Two properties that are answers, not settings

`bool Clustered` and `bool SupportsPersistence` are read-only because they describe what the store *is*.
A store that cannot cluster answers `false` and means it.

## Deriving from AdoJobStoreBase

If your storage is relational but the *transaction* model differs — you manage transactions elsewhere,
or lock differently — derive from `AdoJobStoreBase` rather than writing a store. It has exactly two
abstract members:

<!-- A signature listing rather than code, so it is written out here rather than compiled. -->

```csharp
protected abstract ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken ct = default);

protected abstract ValueTask<T> ExecuteInLock<T>(
    SchedulerLock? lockKind,
    Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
    CancellationToken ct = default);
```

`LocalTransactionJobStore` and `ExternalTransactionJobStore` are the two shipped answers. An override of
`GetLocalTransactionConnection` has to start with `GetEnlistedConnection`.

Four members are `protected virtual`, and one of them is a real extension point:

<!-- A signature listing rather than code, so it is written out here rather than compiled. -->

```csharp
protected virtual TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request);
```

It maps the store-level request onto the criteria the driver delegate reads. Start from the base and
return a `with` copy — the criteria are a record, so `with` leaves everything the base decided in
place:

<!-- snippet: sample_custom_job_store_acquisition_criteria -->
```csharp
protected override TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
{
    TriggerAcquisitionCriteria criteria = base.CreateAcquisitionCriteria(request);
    return criteria with { MaxCount = Math.Min(criteria.MaxCount, this.nodeBudget) };
}
```
<!-- endSnippet -->

::: warning The MaxCount rule
An override may **lower** `MaxCount` but must never raise it above the request's. The choice between
lock-free and locked acquisition was already made from the request before this factory runs, so a
raised count is only caught by post-acquisition validation, and the surplus is released and retried —
a performance hazard rather than corruption, but a silent one.
:::

It is called once per acquisition *attempt*, inside the store's internal retry loop, so an override runs
again for every retry rather than once per `AcquireNextTriggers` call. Anything time-derived is
recomputed, which is deliberate.

`TriggerAcquisitionCriteria` is the designated place for future acquisition filtering, so a property
added later will default to "no additional filtering" — an override that starts from `base` and adjusts
one field keeps working.

### Excluding job types from acquisition

`ExcludedJobTypeNames` is the first of those properties, and it is how a node declines whole classes
of work: names in the set are kept out of the acquisition query's result set, so an excluded job type
never occupies one of the `MaxCount` rows a post-filter would have to discard.

<!-- snippet: sample_custom_job_store_excluded_job_types -->
```csharp
// JobType.FullName is the spelling the store persists - "Namespace.TypeName, AssemblyName".
// Type.FullName carries no assembly name and would never match a stored row.
private static readonly string reportingJobTypeName = new JobType(typeof(ReportingJob)).FullName;

protected override TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
{
    // Asked again on every acquisition attempt, so a window that opens between two of them takes
    // effect on the next one without restarting anything.
    string[]? excluded = this.maintenanceWindow.IsOpen ? [reportingJobTypeName] : null;

    return base.CreateAcquisitionCriteria(request) with { ExcludedJobTypeNames = excluded };
}
```
<!-- endSnippet -->

Two things to get right:

- **Name the type the way the store persists it.** That is `JobType.FullName` —
  `Namespace.TypeName, AssemblyName`, the same string `TriggerAcquireResult.JobTypeName` carries and
  the same one the ADO schema keeps in `JOB_CLASS_NAME`. `Type.FullName` has no assembly name and will
  never match a stored row.
- **Matching is exact.** There is no prefix or wildcard form. The SQL comparison follows the
  `JOB_CLASS_NAME` column's collation, so its case sensitivity is the database's, not .NET's; the
  in-memory store compares ordinally. Rows written by Quartz 2.x or 3.x can carry an older spelling,
  and the read side never rewrites a stored name, so an exclusion will not match those.

The property is also on `TriggerAcquisitionRequest`, which every shipped store honours — set it there
when the caller knows the exclusions, and override `CreateAcquisitionCriteria` when the *store* does.
Entries must be non-blank and there may be at most 1000 of them, both checked at construction; 1000 is
Oracle's ceiling on an `IN` list.

## Rebuilding jobs and triggers

A store that reads its data back has to reconstruct `IJobDetail` and `IOperableTrigger`. The two are
not symmetric:

- **Jobs go through `JobBuilder`.** `JobDetailImpl` is internal, so `JobBuilder` is the only supported
  construction path — which is what the ADO store does too.
- **Triggers can be constructed directly.** `Quartz.Impl.Triggers.*TriggerImpl` are public, and
  `TriggerBase` is public and abstract. Note that three of the five are `sealed`
  (`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl`, `RecurrenceTriggerImpl`); only
  `SimpleTriggerImpl` and `CronTriggerImpl` can be subclassed.

## Testing one

- **Behaviour**: run a real scheduler over your store with `UseJobStore<MyStore>()` and assert through
  `IScheduler`. That is the only way to exercise the fire cycle's ordering.
- **The contract**: the query rules above — ordering, `HasMore`, the `Take = 0` count — are all
  testable against the store directly, with no scheduler.
- **Fault handling**: `DelegatingJobStore` wrapping *your* store lets a test make one member fail.

See [Testing](../tutorial/testing.md).

## See also

- [Job Stores](../tutorial/job-stores.md) — the shipped stores and what they guarantee
- [A Driver Delegate for a New Database](dialect-delegate.md) — the right seam for a relational database
- [Querying Jobs and Triggers](../tutorial/querying-jobs-and-triggers.md) — the query contract, from the caller's side
