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

## Message buses

### [MassTransit.Quartz](https://www.nuget.org/packages/MassTransit.Quartz)

MassTransit's message scheduler, on Quartz.NET.

::: warning It needs Quartz.NET 3.x
As of 2026-09-26:

| Version | Depends on `Quartz` and `Quartz.Extensions.Hosting` | On Quartz.NET 4.x |
|---|---|---|
| 9.2.2 | `>= 3.22.0`, no upper bound | fails |
| 8.5.10, the latest 8.x | `>= 3.18.1`, no upper bound | fails |

The open-source `develop` branch builds against 3.18.1
([`Directory.Packages.props`](https://github.com/MassTransit/MassTransit/blob/develop/Directory.Packages.props)).
Neither range has an upper bound, so a project that references `Quartz` 4.x gets 4.x under a package
compiled against the 3.x API. Both versions fail the same way:

- **`Quartz` 4.x alone:** `AddQuartz` and `AddQuartzHostedService` fail with `CS0121`. MassTransit.Quartz
  brings the 3.x `Quartz.Extensions.Hosting` and `Quartz.Extensions.DependencyInjection` with it, so there
  is no package reference to remove.
- **With the empty 4.x `Quartz.Extensions.Hosting` too:** the build succeeds and the host fails to start
  with `FileNotFoundException: Could not load file or assembly 'Quartz.Extensions.Hosting, Version=3.22.0.0'`
  (`3.18.1.0` on 8.x).

Keep the application on Quartz.NET 3.x. Pin it below 4.0, so that a 4.x package anywhere in the graph
fails the restore with `NU1605` instead of the run:

```xml
<PackageReference Include="Quartz" Version="[3.22.0, 4.0)" />
```

:::

## Schedules

### [NaturalCron.Quartz](https://github.com/hugoj0s3/NaturalCron)

Human-readable schedules. `WithNaturalCronSchedule(...)` replaces `WithCronSchedule(...)` on a `TriggerBuilder`
and takes a sentence, such as "Every day between monday and friday at 6:00pm", or a fluent builder. Cron
expressions keep working alongside it.
