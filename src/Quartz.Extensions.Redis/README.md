# Quartz.Extensions.Redis

[Quartz.Extensions.Redis](https://www.nuget.org/packages/Quartz.Extensions.Redis) provides a Redis-based distributed lock handler that replaces database row locks in clustered Quartz.NET setups.

> **Note:** Quartz 3.18 or later required. Useful when the default database row locks cause deadlocks or contention on the `QRTZ_LOCKS` table under heavy scheduling load.

## Installation

```shell
dotnet add package Quartz.Extensions.Redis
```

## Usage

Job and trigger data stays in your relational database; only the locks move to Redis (`SET NX PX`):

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

| Property | Default | Description |
|---|---|---|
| `redisConfiguration` | `localhost:6379` | StackExchange.Redis connection string |
| `keyPrefix` | `quartz:lock:` | Prefix for Redis lock keys |
| `lockTtlMilliseconds` | `30000` | Lock TTL (auto-expires after this duration) |
| `lockRetryIntervalMilliseconds` | `100` | Polling interval between `SET NX` retries |

## Documentation

📖 Full documentation, including property-based configuration and design details: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/redis.html>
