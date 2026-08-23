# Quartz.Extensions.Redis

[Quartz.Extensions.Redis](https://www.nuget.org/packages/Quartz.Extensions.Redis) provides a Redis-based
distributed lock handler that replaces the database row locks a clustered Quartz.NET scheduler otherwise
coordinates trigger acquisition with.

The default handler takes `SELECT ... FOR UPDATE` locks on the `QRTZ_LOCKS` table. Under heavy
scheduling load that shows up as deadlocks, lock-wait timeouts and contention on one row. This handler
uses Redis `SET NX PX` instead; job and trigger data stays where it was.

## Installation

```shell
dotnet add package Quartz.Extensions.Redis
```

## Usage

<!-- snippet: sample_readme_redis -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.UseClustering();

    // job and trigger data stays in the database; only the locks move to Redis
    store.UseRedisLockHandler(redis => redis.RedisConfiguration = "redis-server:6379");
}));
```
<!-- endSnippet -->

`UseRedisLockHandler` hangs off the same store configurator with or without a host, and the flat
`quartz.jobStore.lockHandler.*` keys configure the same handler. `RedisLockHandlerOptions` carries the
connection string, the key prefix (`quartz:lock:`), the lock time to live (30 seconds) and the retry
interval (100 milliseconds); the scheduler name that namespaces the keys comes from the job store, not
from you.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/redis.html>
