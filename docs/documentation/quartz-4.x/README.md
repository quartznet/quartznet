---
title: Quartz.NET 4.x
prev: false
next: false
---

:::tip
Quartz.NET 4.0 is in prerelease. The public API is frozen — from `4.0.0-beta.1` onwards changes are
additive — while the packages on nuget.org still carry a prerelease suffix, so `--prerelease` is needed
to install them.
:::

* [Quick Start](quick-start.md) — install the package and run a first job
* [Tutorial](tutorial/) — the guided tour, from a first scheduler to clustering
* [How To's](how-tos/) — short recipes for one task each
* [Configuration Reference](configuration/reference.md) — every option, typed and legacy
* [JSON Configuration](configuration/json.md) — the schedule file format
* [Cron Expression Reference](cron-expressions.md) — the cron syntax
* [Multi-Tenancy](multi-tenancy.md) — the three ways to separate tenants, and what each one isolates

Going to production:

* [Operations](operations.md) — rolling upgrades, failover, sizing, backup, health checks
* [Database Schema](db/) — what the tables hold and which indexes matter

Coming from 3.x:

* [Migration Guide](migration-guide.md) — what changed from 3.x, and what to do about it
