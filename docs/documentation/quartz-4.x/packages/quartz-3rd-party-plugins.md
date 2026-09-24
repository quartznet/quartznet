---

title: 3rd Party Plugins for Quartz
---

Packages by other authors that integrate with Quartz.NET. The Quartz.NET project does not maintain them; each
states its own Quartz version compatibility.

## Migrations

### [AppAny.Quartz.EntityFrameworkCore.Migrations](https://github.com/appany/AppAny.Quartz.EntityFrameworkCore.Migrations)

Creates and migrates the Quartz.NET schema with EF Core migrations, in one line of configuration.

Since 4.0, schema creation is [built in](../tutorial/job-stores.md#creating-the-schema). Its DDL is compared
object by object with `database/tables/` in the build and provisioned against every dialect in the integration
tests, so it matches the release. A package that models the tables separately states its own schema version.

### [Weasel.Quartz](https://github.com/Hawxy/Weasel.Quartz)

Runtime PostgreSQL migration support for non-EF and Marten projects.

## Database Implementations

### [Quartz.NET-RavenDB](https://github.com/ravendb/quartznet-RavenDB)

Job store on RavenDB.

### [QuartzRedisJobStore](https://github.com/icyice80/QuartzRedisJobStore)

Job store on Redis, using StackExchange.Redis. A port of
[quartz-redis-jobstore](https://github.com/jlinn/quartz-redis-jobstore); it does not support Redis Cluster.

### [Quartz.NET-CosmosDB](https://github.com/Oriflame/cosmosdb-quartznet)

Job store on Azure Cosmos DB.

### [Quartz.NET-MongoDB](https://github.com/glucaci/mongodb-quartz-net)

Job store on MongoDB.

## Dependency Injection

### [Autofac.Extras.Quartz](https://github.com/alphacloud/Autofac.Extras.Quartz)

Autofac integration.

## Dashboards

### [CrystalQuartz](https://github.com/guryanovev/CrystalQuartz)

A pluggable web UI, hosted inside the application whose scheduler it watches.

### [SilkierQuartz](https://github.com/MaiKeBing/SilkierQuartz)

Web management tools, with its own execution-history plugin and EF Core stores for that history.

::: warning Neither has a Quartz.NET 4.0 release yet
As of 2026-08-30, neither had published a release built against 4.0:

- SilkierQuartz 10.0.0 depends on `Quartz` 3.18.0.
- CrystalQuartz 7.3.0's Quartz 3 adapter is compiled against the 3.x interface.

Both call `IScheduler.GetMetaData()`, `IScheduler.GetCurrentlyExecutingJobs()` and `IsStarted` /
`InStandbyMode` / `IsShutdown`, all renamed or removed in 4.0; the
[migration guide appendix](../migration-guide.md#appendix-what-happened-to-a-name) lists the replacements. Both
bind to the host's `Quartz` assembly, so the mismatch fails when the dashboard is served, not at compile time.

[`Quartz.Dashboard`](dashboard.md), from this repository, is built against 4.0.
:::

## Schedules

### [NaturalCron.Quartz](https://github.com/hugoj0s3/NaturalCron)

Human-readable schedules. `WithNaturalCronSchedule(...)` replaces `WithCronSchedule(...)` on a `TriggerBuilder`
and takes a sentence, such as "Every day between monday and friday at 6:00pm", or a fluent builder. Cron
expressions keep working alongside it.
