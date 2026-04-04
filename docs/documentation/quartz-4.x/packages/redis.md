---
title: Redis Lock Handler
---

[Quartz.Redis](https://www.nuget.org/packages/Quartz.Redis) provides a Redis-based distributed lock handler (`ISemaphore`) that replaces database row locks in clustered Quartz.NET setups.

::: tip
Useful when database row locks (the default for clustered setups) cause deadlocks or performance issues under heavy scheduling load.
:::

::: tip
Quartz 4.0 or later required.
:::

## Installation

```shell
Install-Package Quartz.Redis
```

## Why Redis Locks?

The default `StdRowLockSemaphore` uses `SELECT ... FOR UPDATE` database row locks to coordinate trigger acquisition across cluster nodes. Under heavy scheduling load this can lead to:

- **Table deadlocks** in certain database engines
- **Connection timeouts** when obtaining locks is slow
- **Performance degradation** from lock contention on the `QRTZ_LOCKS` table

The Redis lock handler replaces these database locks with Redis `SET NX PX` distributed locks while keeping all job and trigger data in your relational database.

## Configuring

### Using SchedulerBuilder (recommended)

```csharp
var schedulerFactory = SchedulerBuilder.Create()
    .UsePersistentStore(store =>
    {
        store.UseSqlServer(connectionString);
        store.UseSystemTextJsonSerializer();
        store.UseClustering();
        store.UseRedisLockHandler(redis =>
        {
            redis.RedisConfiguration = "redis-server:6379";
        });
    })
    .Build();
```

### Using properties

```csharp
var properties = new NameValueCollection
{
    ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
    ["quartz.jobStore.clustered"] = "true",
    ["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.RedisSemaphore, Quartz.Redis",
    ["quartz.jobStore.lockHandler.redisConfiguration"] = "redis-server:6379"
};
```

## Configuration Properties

| Property | Default | Description |
|---|---|---|
| `redisConfiguration` | `localhost:6379` | StackExchange.Redis connection string |
| `keyPrefix` | `quartz:lock:` | Prefix for Redis lock keys |
| `lockTtlMilliseconds` | `30000` | Lock TTL in milliseconds (auto-expires after this duration) |
| `lockRetryIntervalMilliseconds` | `100` | Polling interval between `SET NX` retry attempts |

All properties are set under `quartz.jobStore.lockHandler.*`. The `schedName` and `tablePrefix` properties are injected automatically.

## How It Works

The lock handler uses a two-tier locking strategy:

1. **Local tier** &mdash; A `SemaphoreSlim` per lock name prevents redundant Redis round-trips when the same process already holds the lock.

2. **Redis tier** &mdash; `SET key value NX PX timeout` provides the cross-node distributed lock. The key includes the scheduler name for multi-scheduler isolation (e.g., `quartz:lock:MyScheduler:TRIGGER_ACCESS`).

Lock release uses a Lua script for atomic check-and-delete, preventing a node from accidentally releasing a lock that has already expired and been re-acquired by another node.

## Considerations

- **Lock TTL**: The default 30-second TTL provides ample margin for typical scheduling operations (milliseconds to low seconds). If your database is very slow, increase the TTL. If a node crashes, the lock auto-expires after the TTL.
- **Redis availability**: If Redis is unreachable, `ObtainLock` throws a `LockException` which the scheduler handles via its standard retry mechanism.
- **Single-instance Redis**: This implementation uses simple `SET NX` locks, not the Redlock algorithm. For most Quartz.NET deployments a single Redis instance (or replica set with Sentinel) is sufficient since the locks are advisory and short-lived.
