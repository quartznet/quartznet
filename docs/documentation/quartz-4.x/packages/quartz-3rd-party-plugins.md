---

title: 3rd Party Plugins for Quartz
---

Packages by other authors that integrate with Quartz.NET. They are listed here for convenience; they are not
maintained by the Quartz.NET project, and their compatibility with a given Quartz version is theirs to state.

## Migrations

### [AppAny.Quartz.EntityFrameworkCore.Migrations](https://github.com/appany/AppAny.Quartz.EntityFrameworkCore.Migrations)

This library handles schema creation and migrations for Quartz.NET using EntityFrameworkCore migrations toolkit with one line of configuration

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

## Schedules

### [NaturalCron.Quartz](https://github.com/hugoj0s3/NaturalCron)

Human-readable schedule expressions for Quartz.NET. `WithNaturalCronSchedule(...)` takes the place of
`WithCronSchedule(...)` on a `TriggerBuilder` and accepts a sentence — "Every day between monday and
friday at 6:00pm" — or the same schedule built with a fluent builder. Cron expressions keep working
alongside it. The package is maintained outside this repository.
