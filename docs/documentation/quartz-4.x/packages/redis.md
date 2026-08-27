---
title: Redis Lock Handler
---

[Quartz.Extensions.Redis](https://www.nuget.org/packages/Quartz.Extensions.Redis) provides a Redis-based distributed lock handler (`ILockHandler`) that replaces database row locks in clustered Quartz.NET setups.

::: tip
Useful when database row locks (the default for clustered setups) cause deadlocks or performance issues under heavy scheduling load.
:::

::: tip
Quartz 4.0 or later required.
:::

## Installation

```shell
dotnet add package Quartz.Extensions.Redis
```

## Why Redis Locks?

The default `SelectForUpdateLockHandler` uses `SELECT ... FOR UPDATE` database row locks to coordinate trigger acquisition across cluster nodes. Under heavy scheduling load this can lead to:

- **Table deadlocks** in certain database engines
- **Connection timeouts** when obtaining locks is slow
- **Performance degradation** from lock contention on the `QRTZ_LOCKS` table

The Redis lock handler replaces these database locks with Redis `SET NX PX` distributed locks while keeping all job and trigger data in your relational database.

## Configuring

### Using the builder (recommended)

<!-- snippet: sample_redis_lock_handler -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.UseSystemTextJsonSerializer();
    store.UseClustering();
    store.UseRedisLockHandler(redis =>
    {
        redis.RedisConfiguration = "redis-server:6379";
    });
}));
```
<!-- endSnippet -->

The same `UseRedisLockHandler` call works without a host, on `QuartzSchedulerBuilder.Create()`.

## Configuration

`RedisLockHandlerOptions`:

| Option | Default | Description |
|---|---|---|
| `RedisConfiguration` | `localhost:6379` | StackExchange.Redis connection string |
| `KeyPrefix` | `quartz:lock:` | Prefix for Redis lock keys |
| `LockTimeToLive` | 30 seconds | Lock TTL &mdash; the lock auto-expires after this duration |
| `LockRetryInterval` | 100 milliseconds | Polling interval between `SET NX` retry attempts |

<!-- snippet: sample_redis_lock_handler_options -->
```csharp
store.UseRedisLockHandler(redis =>
{
    redis.RedisConfiguration = "redis-server:6379";
    redis.LockTimeToLive = TimeSpan.FromSeconds(30);
    redis.LockRetryInterval = TimeSpan.FromMilliseconds(100);
});
```
<!-- endSnippet -->

The scheduler name that namespaces the lock keys is not configured here: the job store tells the handler
which scheduler it locks for, through `ILockHandler.Initialize(LockHandlerContext)`, before the handler is used.

### Using properties

The same handler chosen with flat keys, under `quartz.jobStore.lockHandler.*`. A bare number in one of the
two time settings is read as milliseconds:

<!-- snippet: sample_redis_properties -->
```csharp
NameValueCollection properties = new()
{
    ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
    ["quartz.jobStore.clustered"] = "true",
    ["quartz.jobStore.lockHandler.type"] = "Quartz.Extensions.Redis.RedisLockHandler, Quartz.Extensions.Redis",
    ["quartz.jobStore.lockHandler.redisConfiguration"] = "redis-server:6379",
    ["quartz.jobStore.lockHandler.lockTimeToLive"] = "30000"
};

await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```
<!-- endSnippet -->

## How It Works

The lock handler uses a two-tier locking strategy:

1. **Local tier** &mdash; A `SemaphoreSlim` per lock name prevents redundant Redis round-trips when the same process already holds the lock.

2. **Redis tier** &mdash; `SET key value NX PX timeout` provides the cross-node distributed lock. The key includes the scheduler name for multi-scheduler isolation (e.g., `quartz:lock:MyScheduler:TRIGGER_ACCESS`).

Lock release uses a Lua script for atomic check-and-delete, preventing a node from accidentally releasing a lock that has already expired and been re-acquired by another node.

## Considerations

- **Lock TTL**: The default 30-second TTL provides ample margin for typical scheduling operations (milliseconds to low seconds). If your database is very slow, increase the TTL. If a node crashes, the lock auto-expires after the TTL.
- **Redis availability**: If Redis is unreachable, `AcquireLock` throws a `LockException` which the scheduler handles via its standard retry mechanism.
- **Single-instance Redis**: This implementation uses simple `SET NX` locks, not the Redlock algorithm. For most Quartz.NET deployments a single Redis instance (or replica set with Sentinel) is sufficient since the locks are advisory and short-lived.
