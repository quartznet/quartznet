---

title : 3rd party Plugins for Quartz
---

# 3rd party packages that have integration with the Quartz.Net Library

## Database

### [EntityframeworkCore migrations for Quartz.Net](https://github.com/appany/AppAny.Quartz.EntityFrameworkCore.Migrations)

This library handles schema creation and migrations for Quartz.NET using EntityFrameworkCore migrations toolkit with one line of configuration

### [Quartz.NET-RavenDB](https://github.com/ravendb/quartznet-RavenDB)

JobStore implementation for Quartz.NET scheduler using RavenDB.

### [QuartzRedisJobStore](https://github.com/icyice80/QuartzRedisJobStore)

A Quartz Scheduler JobStore using Redis via C#

The project was a ported version of quartz-redis-jobstore (<https://github.com/jlinn/quartz-redis-jobstore>), currently it lacks of supporting redis-cluster. It uses StackExchange.Redis as the redis client.

## Dependency Injection

### [Autofac.Extras.Quartz](https://github.com/alphacloud/Autofac.Extras.Quartz)

Autofac integration package for Quartz.Net.
