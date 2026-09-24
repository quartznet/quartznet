---
title: 'A Lock Handler of Your Own'
---

# A Lock Handler of Your Own

A clustered ADO job store serializes its work with a lock so two nodes cannot acquire the same trigger. By
default it is a row in `QRTZ_LOCKS`. Implement `ILockHandler` to use anything that grants one holder at a
time: Redis, ZooKeeper, a cloud lease.

## The contract

<!-- Quartz's own declaration of the interface, so it is written out here rather than compiled
     from the samples project: a second `ILockHandler` in that project would shadow the real one. -->

```csharp
public interface ILockHandler
{
    bool RequiresConnection { get; }

    void Initialize(LockHandlerContext context) { }

    ValueTask<bool> AcquireLock(Guid requestorId, ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind, CancellationToken cancellationToken = default);

    ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind,
        CancellationToken cancellationToken = default);
}
```

`Initialize` defaults to empty; skip it if your locks are not keyed by scheduler identity.

### There are exactly two locks

`SchedulerLock` has two members, so no caller can invent a lock that protects nothing:

| Member | Guards | Stored as |
|---|---|---|
| `TriggerAccess` | every change to jobs, triggers and calendars, and trigger acquisition | `TRIGGER_ACCESS` |
| `StateAccess` | cluster check-in and failed-node recovery, in their own transaction (no deadlock with trigger work) | `STATE_ACCESS` |

::: warning
The enum-to-string mapping is internal. A handler that needs the stored names (for key compatibility with
the row-lock handler, or across a rolling upgrade) declares its own constants, as `RedisLockHandler` does so
a mixed-version cluster contends for the same Redis key.
:::

### Re-entry returns false

The most important rule: **`AcquireLock` called again with the same `requestorId` and `lockKind` returns
`false` and takes no second lock.**

`false` is not an error. The store releases only a lock its own call took, so a nested operation re-enters
without re-locking or releasing early. Returning `true` would let the inner operation release the outer
one's lock.

That is for a handler that records *whether* a requestor holds a lock (the row-lock handlers,
`InProcessLockHandler`). A handler that *counts* holds, like the shipped `SqliteLockHandler`, may return
`true`, because the inner release only decrements. Either way: **the caller releases exactly when it was told
`true`, and never otherwise.**

`ReleaseLock` from a non-owner should warn, not throw.

### And `false` means nothing else

**An acquire that did not take the lock throws**: `LockException` when refused, `OperationCanceledException`
when the token fired.

::: danger
Never return `false` on cancellation. The store reads it as *already held*: it runs the operation unlocked
and releases nothing. Do not rely on the next step (with `RequiresConnection = false`, a connection open on
the same token) throwing first; that is statement order, not a guarantee.
:::

A handler that gives up leaves nothing behind: an abandoned wait must not consume a handover meant for the
next waiter, and anything taken before the failure (a local gate in front of a remote lock) is released
before the exception escapes.

## Deriving from DbLockHandler

For a database row lock, `DbLockHandler` handles ownership, re-entry and prefix substitution, and leaves one
method:

<!-- A signature listing rather than code, so it is written out here rather than compiled. -->

```csharp
protected abstract ValueTask ExecuteSql(
    Guid requestorId,
    ConnectionAndTransactionHolder conn,
    string lockName,
    string expandedSql,
    string expandedInsertSql,
    CancellationToken cancellationToken = default);
```

Take the row lock and **return normally on success, or throw**; ownership is recorded after it returns. Both
statements arrive prefix-expanded; the insert covers a missing row. Issue them through:

<!-- A signature listing rather than code, so it is written out here rather than compiled. -->

```csharp
protected DbCommand PrepareCommand(ConnectionAndTransactionHolder conn, string commandText);
protected void AddCommandParameter(DbCommand command, string paramName, object? paramValue);
```

No overload takes a provider-specific type or size: lock statements bind two strings.

Shipped implementations, both `public` and unsealed, waiting on the `TimeProvider` so retries are testable:

- **`UpdateRowLockHandler`** — `UPDATE {0}LOCKS SET LOCK_NAME = LOCK_NAME WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName`,
  retried `RetryCount` times (`protected virtual`, 2 by default) with `RetryPeriod` between attempts,
  inserting the row if none was updated. `SqlServerMemoryOptimizedUpdateRowLockHandler` is a two-line
  subclass raising the retry count to 5.
- **`SelectForUpdateLockHandler`** — `SELECT * FROM {0}LOCKS … FOR UPDATE`, with
  `PostgreSqlSelectForUpdateLockHandler` as its dialect variant.

`DbLockHandler` fixes `RequiresConnection` to `true`, so `conn` is never null.

## Implementing ILockHandler directly

For a lock outside the database, implement the interface with `RequiresConnection` `false`:

<!-- snippet: sample_lock_handler_custom -->
```csharp
public sealed class LeaseLockHandler : ILockHandler
{
    private string schedulerName = "";

    public bool RequiresConnection => false;

    public void Initialize(LockHandlerContext context) => schedulerName = context.SchedulerName;

    public async ValueTask<bool> AcquireLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        // ... acquire, honouring the re-entry rule ...
        return true;
    }

    public ValueTask ReleaseLock(
        Guid requestorId,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        // ...
        return default;
    }
}
```
<!-- endSnippet -->

`RequiresConnection = false` lets the store open its connection only *after* the lock is taken, which is the
point of an external lock.

::: warning
`RequiresConnection = false` with `AcceptEnlistedTransactions` logs a startup warning: the lock is released
when Quartz's work ends, *before* the application commits its ambient transaction, so it does not cover the
window it should.
:::

## LockHandlerContext

The job store calls `Initialize` once, after choosing the handler and before schema validation, on both
construction paths (otherwise a container-supplied handler would query `QRTZ_LOCKS` with a null scheduler
name):

| Member | |
|---|---|
| `SchedulerName` (required) | the scheduler whose data the lock protects |
| `InstanceId` (required) | this node |
| `TablePrefix` (required) | ignored by a handler that does not lock in the database |
| `TimeProvider` | wait on this rather than on wall time, so retry behaviour is testable |
| `CommandTimeout` | from `AdoJobStoreOptions.CommandTimeout` |
| `LockWaitWarningThreshold` | from `AdoJobStoreOptions.LockWaitWarningThreshold`; `null` in a context built by hand |

- `CommandTimeout` bounds a node stuck on `QRTZ_LOCKS` behind a peer that stopped without releasing the row.
- `LockWaitWarningThreshold`: a `DbLockHandler` subclass logs warning 3716 once per slow acquisition,
  unasked; your own handler may ignore it or report its own way.
- The store times every acquisition on `quartz.jobstore.lock.wait.duration`; handlers owe nothing for it.

## Registering it

<!-- snippet: sample_lock_handler_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(s =>
    {
        s.UseLockHandler<LeaseLockHandler>();
        s.UseSqlServer(connectionString);
        s.UseClustering();
    });
});
```
<!-- endSnippet -->

`UseLockHandler(Func<IServiceProvider, ILockHandler>)` is for a handler needing values rather than services;
it registers under the scheduler's key, unlike a registration on `Services`. `Quartz.Extensions.Redis` uses
this same public overload:

<!-- snippet: sample_lock_handler_redis -->
```csharp
s.UseRedisLockHandler(o =>
{
    o.RedisConfiguration = "localhost:6379";
    o.KeyPrefix = "quartz:";
    o.LockTimeToLive = TimeSpan.FromSeconds(30);
});
```
<!-- endSnippet -->

The legacy key is `quartz.jobStore.lockHandler.type`. Its 3.x sub-keys `.tablePrefix` and `.schedName` (from
`ITablePrefixAware`) are rejected as obsolete, since `Initialize` supplies both, and so is `.schedulerName`,
the key the 4.x property name suggests.

## A handler is always used

`AdoJobStoreOptions.UseDbLocks` picks *which* handler the store builds, not *whether* it locks:

| Situation | Handler |
|---|---|
| You registered one | yours, and `SelectWithLockSql` is ignored with a warning |
| `UseDbLocks = true` (forced on by clustering and by `AcceptEnlistedTransactions`) | `SelectForUpdateLockHandler`, or the PostgreSQL variant |
| Otherwise | `InProcessLockHandler` — an in-process monitor |

A non-clustered scheduler locks in memory, which is correct for a single node.

## Testing one

No scheduler needed:

- Call `AcquireLock` twice with the same `requestorId`; assert the second returns `false`.
- Acquire with an already-fired token; assert `OperationCanceledException`, not `false`. Then acquire from
  another `requestorId` and assert it succeeds, catching a lock left held by the abandoned attempt.
- Pass a `FakeTimeProvider` through `LockHandlerContext` and advance it to drive the retry loop.

## See also

- [Clustering](../tutorial/advanced-enterprise-features.md) — what the locks are protecting
- [Redis](../packages/redis.md) — the shipped external lock handler
- [A Driver Delegate for a New Database](dialect-delegate.md) — the other ADO seam
