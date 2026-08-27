---
title: 'A Lock Handler of Your Own'
---

# A Lock Handler of Your Own

A clustered ADO job store serializes its work with a lock, so that two nodes cannot acquire the same
trigger. By default that lock is a row in `QRTZ_LOCKS`. `ILockHandler` is the seam for making it
something else — Redis, ZooKeeper, a cloud lease, anything that can grant one holder at a time.

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

`Initialize` has a default (empty) implementation, so a handler that does not key its locks by
scheduler identity can skip it.

### There are exactly two locks

`SchedulerLock` is an enum with two members, and there have only ever been two:

| Member | Guards | Stored as |
|---|---|---|
| `TriggerAccess` | every change to jobs, triggers and calendars, and trigger acquisition | `TRIGGER_ACCESS` |
| `StateAccess` | cluster check-in and failed-node recovery, on their own transaction so they cannot deadlock against trigger work | `STATE_ACCESS` |

Saying so in the type means a caller cannot invent a third lock that silently protects nothing.

::: warning
The enum-to-string mapping is internal. A handler in your own assembly that needs the stored names —
for key compatibility with the row-lock handler, or across a rolling upgrade — declares its own
constants. `RedisLockHandler` does exactly that, with the comment that the Redis key keeps the *stored*
lock names so that a mixed-version cluster keeps contending for the same key.
:::

### Re-entry returns false

The single most important rule: **`AcquireLock` called again with the same `requestorId` and the same
`lockKind` must return `false`, and must not take a second lock.**

That is not an error signal. The store stores the result and releases the lock only when it was the
call that took it, so a nested operation on the same caller re-enters without re-locking and without
prematurely releasing. Returning `true` from a re-entrant call means the inner operation releases the
lock the outer one is still relying on.

`ReleaseLock` from a non-owner should warn, not throw — that is what the shipped handlers do.

## Deriving from DbLockHandler

When the lock *is* a database row, `DbLockHandler` does the plumbing — ownership tracking, re-entry,
prefix substitution — and leaves one method:

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

It must take the row lock and **return normally on success, or throw on failure**; the base only
records ownership after it returns. Both statements arrive already prefix-expanded, and the insert is
there for the missing-row case.

Two protected helpers are what you issue it through:

<!-- A signature listing rather than code, so it is written out here rather than compiled. -->

```csharp
protected DbCommand PrepareCommand(ConnectionAndTransactionHolder conn, string commandText);
protected void AddCommandParameter(DbCommand command, string paramName, object? paramValue);
```

There is no overload taking a provider-specific data type or a size, because a lock statement binds a
scheduler name and a lock name and both are strings.

Two shipped implementations to read:

- **`UpdateRowLockHandler`** — `UPDATE {0}LOCKS SET LOCK_NAME = LOCK_NAME WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName`,
  retried `RetryCount` times (a `protected virtual` property, 2 by default) with `RetryPeriod` between
  attempts, inserting the row if the update affected none. `SqlServerMemoryOptimizedUpdateRowLockHandler`
  is a two-line subclass that raises the retry count to 5.
- **`SelectForUpdateLockHandler`** — `SELECT * FROM {0}LOCKS … FOR UPDATE`, with
  `PostgreSqlSelectForUpdateLockHandler` as its dialect variant.

Both are `public` and unsealed. Both wait on the `TimeProvider` between attempts rather than on wall
time, so their retry behaviour is testable.

`DbLockHandler` fixes `RequiresConnection` to `true`, so its `conn` is never null.

## Implementing ILockHandler directly

When the lock does not live in the database, implement the interface and answer `false` to
`RequiresConnection`:

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

`RequiresConnection = false` is not cosmetic: it tells the store it can delay opening a database
connection until *after* the lock has been taken, which is the whole efficiency argument for an
external lock.

::: warning
`RequiresConnection = false` combined with `AcceptEnlistedTransactions` produces a startup warning.
An in-process or external lock is released as soon as Quartz's work is done, which is *before* the
application commits its ambient transaction — so the window the lock was supposed to protect is not the
window it covers.
:::

## LockHandlerContext

`Initialize` is called once, by the job store, after it has decided which handler to use and before
schema validation:

| Member | |
|---|---|
| `SchedulerName` (required) | the scheduler whose data the lock protects |
| `InstanceId` (required) | this node |
| `TablePrefix` (required) | ignored by a handler that does not lock in the database |
| `TimeProvider` | wait on this rather than on wall time, so retry behaviour is testable |
| `CommandTimeout` | from `AdoJobStoreOptions.CommandTimeout` |

The store calls it on both construction paths, and that is why it exists: a handler the container
supplied would otherwise query `QRTZ_LOCKS` with a null scheduler name, whatever the store is actually
configured with.

`CommandTimeout` earns its keep here specifically. A node waiting on `QRTZ_LOCKS` behind a peer that
stopped without releasing the row cannot make progress until the statement gives up.

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

There is a factory overload, `UseLockHandler(Func<IServiceProvider, ILockHandler>)`, for a handler that
needs values rather than services — it registers under the scheduler's own key, which registering
against `Services` directly would not. `Quartz.Extensions.Redis` uses exactly that public overload;
nothing about it is privileged:

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

The legacy key is `quartz.jobStore.lockHandler.type`. Its `.tablePrefix` and `.schedName` sub-keys —
3.x's spelling, from the `ITablePrefixAware` properties they wrote — are rejected as obsolete, because
`Initialize` supplies both. `.schedulerName` is rejected with the same advice, since that is the key
the 4.x property name suggests.

## A handler is always used

`AdoJobStoreOptions.UseDbLocks` selects *which* handler the store builds for itself, not *whether*
locking happens:

| Situation | Handler |
|---|---|
| You registered one | yours, and `SelectWithLockSql` is ignored with a warning |
| `UseDbLocks = true` (forced on by clustering and by `AcceptEnlistedTransactions`) | `SelectForUpdateLockHandler`, or the PostgreSQL variant |
| Otherwise | `InProcessLockHandler` — an in-process monitor |

So a non-clustered scheduler still locks; it just locks in memory, which is correct when it is the only
node.

## Testing one

The re-entry rule and the retry behaviour are the two things worth a test, and neither needs a
scheduler:

- Call `AcquireLock` twice with the same `requestorId` and assert the second returns `false`.
- Give the handler a `FakeTimeProvider` through `LockHandlerContext` and advance it to drive the retry
  loop without the test waiting.

## See also

- [Clustering](../tutorial/advanced-enterprise-features.md) — what the locks are protecting
- [Redis](../packages/redis.md) — the shipped external lock handler
- [A Driver Delegate for a New Database](dialect-delegate.md) — the other ADO seam
