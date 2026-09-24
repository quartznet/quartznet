---
title: Redis Lock Handler
---

[Quartz.Extensions.Redis](https://www.nuget.org/packages/Quartz.Extensions.Redis) is a Redis-based distributed
lock handler (`ILockHandler`). It replaces database row locks in a clustered Quartz.NET setup; job and trigger
data stay in the relational database. Requires Quartz 4.0 or later.

## Installation

```shell
dotnet add package Quartz.Extensions.Redis
```

## Why Redis Locks?

The default `SelectForUpdateLockHandler` coordinates trigger acquisition with `SELECT ... FOR UPDATE` row locks.
Under heavy scheduling load this can cause:

- **table deadlocks** in some database engines;
- **connection timeouts** when obtaining locks is slow;
- **lock contention** on the `QRTZ_LOCKS` table.

The Redis handler uses Redis `SET NX PX` locks instead.

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

The same `UseRedisLockHandler` call works without a host, inside `QuartzSchedulerBuilder.Create(q => …)`.

## Configuration

`RedisLockHandlerOptions`:

| Option | Default | Description |
|---|---|---|
| `RedisConfiguration` | `localhost:6379` | StackExchange.Redis connection string |
| `KeyPrefix` | `quartz:lock:` | Prefix for Redis lock keys |
| `LockTimeToLive` | 30 seconds | Lock TTL; the lock expires after this |
| `LockRetryInterval` | 100 milliseconds | Wait between `SET NX` retries |

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

The scheduler name in the lock keys is not an option: the job store passes it through
`ILockHandler.Initialize(LockHandlerContext)` before first use.

### Using properties

The flat keys are under `quartz.jobStore.lockHandler.*`. A bare number in either time setting is milliseconds.

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

1. **Local tier.** A `SemaphoreSlim` per lock name avoids Redis round-trips when this process already holds
   the lock.
2. **Redis tier.** `SET key value NX PX timeout` is the cross-node lock. The key includes the scheduler name,
   for example `quartz:lock:MyScheduler:TRIGGER_ACCESS`.

Release runs a Lua check-and-delete script, so a node cannot release a lock that expired and was taken by
another node.

## Considerations

- **Lock TTL.** 30 seconds covers typical scheduling operations (milliseconds to low seconds). Increase it for a
  very slow database. If a node crashes, its lock expires after the TTL.
- **Redis unavailable.** `AcquireLock` throws a `LockException`, which the scheduler retries like any other.
- **Single-instance Redis.** The handler uses plain `SET NX` locks, not Redlock. A single Redis instance, or a
  replica set with Sentinel, is enough for most deployments: the locks are advisory and short-lived.
