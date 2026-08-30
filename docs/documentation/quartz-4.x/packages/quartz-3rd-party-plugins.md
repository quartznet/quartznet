---

title: 3rd Party Plugins for Quartz
---

Packages by other authors that integrate with Quartz.NET. They are listed here for convenience; they are not
maintained by the Quartz.NET project, and their compatibility with a given Quartz version is theirs to state.

## Migrations

### [AppAny.Quartz.EntityFrameworkCore.Migrations](https://github.com/appany/AppAny.Quartz.EntityFrameworkCore.Migrations)

This library handles schema creation and migrations for Quartz.NET using EntityFrameworkCore migrations toolkit with one line of configuration

Since 4.0 the supported way to have a schema created for you is
[built in](../tutorial/job-stores.md#creating-the-schema). The DDL it runs is compared object by object
with `database/tables/` in the build, and provisioned against a real database of every dialect in the
integration tests, so it cannot drift from the schema the release expects. A package that models the
tables separately tracks them separately, so which Quartz version its schema matches is its own to
state.

### [Weasel.Quartz](https://github.com/Hawxy/Weasel.Quartz)

Runtime PostgreSQL migration support for non-EF & Marten projects.

## Database Implementations

### [Quartz.NET-RavenDB](https://github.com/ravendb/quartznet-RavenDB)

JobStore implementation for Quartz.NET scheduler using RavenDB.

### [QuartzRedisJobStore](https://github.com/icyice80/QuartzRedisJobStore)

A Quartz Scheduler JobStore using Redis via C#

The project was a ported version of quartz-redis-jobstore (<https://github.com/jlinn/quartz-redis-jobstore>), currently it lacks of supporting redis-cluster. It uses StackExchange.Redis as the redis client.

### [Quartz.NET-CosmosDB](https://github.com/Oriflame/cosmosdb-quartznet)

JobStore implementation for Quartz.NET scheduler using Microsoft Azure CosmosDb.

### [Quartz.NET-MongoDB](https://github.com/glucaci/mongodb-quartz-net)

JobStore implementation for Quartz.NET scheduler using MongoDb.

## Dependency Injection

### [Autofac.Extras.Quartz](https://github.com/alphacloud/Autofac.Extras.Quartz)

Autofac integration package for Quartz.Net.

## Dashboards

### [CrystalQuartz](https://github.com/guryanovev/CrystalQuartz)

A pluggable web UI for Quartz.NET, hosted inside the application whose scheduler it watches.

### [SilkierQuartz](https://github.com/MaiKeBing/SilkierQuartz)

Web management tools for Quartz.NET, with an execution-history plugin of its own and EF Core stores to
keep that history in.

::: warning Neither has a Quartz.NET 4.0 release yet
As with everything else on this page, 4.0 support is theirs to state — and when this page was last
checked, on 2026-08-30, neither had published a release built against it: SilkierQuartz 10.0.0 depends on
`Quartz` 3.18.0, and CrystalQuartz 7.3.0's Quartz 3 adapter is compiled against the 3.x interface. Both
read `IScheduler.GetMetaData()`, `IScheduler.GetCurrentlyExecutingJobs()` and the `IsStarted` /
`InStandbyMode` / `IsShutdown` triple, every one of which 4.0 renamed or removed — the
[appendix to the migration guide](../migration-guide.md#appendix-what-happened-to-a-name) says what each
became. Both bind to whichever `Quartz` assembly the host loaded rather than to one of their own, so the
mismatch shows itself when the dashboard is served, not when the application compiles.

[`Quartz.Dashboard`](dashboard.md), which ships from this repository, is built against 4.0.
:::

## Schedules

### [NaturalCron.Quartz](https://github.com/hugoj0s3/NaturalCron)

Human-readable schedule expressions for Quartz.NET. `WithNaturalCronSchedule(...)` takes the place of
`WithCronSchedule(...)` on a `TriggerBuilder` and accepts a sentence — "Every day between monday and
friday at 6:00pm" — or the same schedule built with a fluent builder. Cron expressions keep working
alongside it. The package is maintained outside this repository.
