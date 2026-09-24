---
title: Redis Lock Handler
---

[Quartz.Extensions.Redis](https://www.nuget.org/packages/Quartz.Extensions.Redis) provides a Redis-based distributed lock handler (`ISemaphore`) that replaces database row locks in clustered Quartz.NET setups.

::: tip
Quartz 3.18 or later required.
:::

## Installation

```shell
Install-Package Quartz.Extensions.Redis
```

## Why Redis Locks?

The default `StdRowLockSemaphore` coordinates trigger acquisition across cluster nodes with `SELECT ... FOR UPDATE` database row locks. Under heavy scheduling load this can cause:

- **Table deadlocks** in certain database engines
- **Connection timeouts** when obtaining locks is slow
- **Performance degradation** from lock contention on the `QRTZ_LOCKS` table

The Redis lock handler replaces these locks with Redis `SET NX PX` distributed locks. All job and trigger data stays in your relational database.

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
    ["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.Redis.RedisSemaphore, Quartz.Extensions.Redis",
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

Two lock tiers:

1. **Local tier:** a `SemaphoreSlim` per lock name saves Redis round-trips when the same process already holds the lock.
2. **Redis tier:** `SET key value NX PX timeout` is the cross-node distributed lock. The key includes the scheduler name to isolate schedulers (e.g., `quartz:lock:MyScheduler:TRIGGER_ACCESS`).

Lock release is an atomic check-and-delete in a Lua script, so a node cannot release a lock that has expired and been re-acquired by another node.

## Considerations

- **Lock TTL:** the default 30-second TTL is ample for typical scheduling operations (milliseconds to low seconds). Increase it if your database is very slow. If a node crashes, its lock expires after the TTL.
- **Redis availability:** if Redis is unreachable, `ObtainLock` throws a `LockException`, which the scheduler handles with its standard retry mechanism.
- **Single-instance Redis:** these are simple `SET NX` locks, not the Redlock algorithm. A single Redis instance (or a replica set with Sentinel) is enough for most deployments, because the locks are advisory and short-lived.
