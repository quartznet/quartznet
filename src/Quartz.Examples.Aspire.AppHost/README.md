# The Aspire example's AppHost

Declares the two resources the example needs — a PostgreSQL server with a database on it, and the
worker that schedules against it — and nothing else. The prose is
[Running Quartz under Aspire](https://www.quartz-scheduler.net/documentation/quartz-4.x/how-tos/aspire.html);
the worker half is [`Quartz.Examples.Aspire.Worker`](../Quartz.Examples.Aspire.Worker/README.md).

## Running it

```shell
dotnet run --project src/Quartz.Examples.Aspire.AppHost
```

**A container runtime has to be running** — Docker Desktop or Podman — because the AppHost starts a
PostgreSQL container. The `aspire` CLI and the Aspire workload are **not** needed: `Aspire.AppHost.Sdk`
brings the dashboard and the Developer Control Plane as package payload, which is why the csproj
deliberately leaves `SkipAddAspireDefaultReferences` unset.

The console prints a dashboard URL. Open it and you will see the `postgres` resource, the `quartz`
database on it, and the `worker`, whose `/health` endpoint the AppHost polls. The worker's log shows a
heartbeat job firing every ten seconds; the database ends up with the twelve `qrtz_*` tables, created by
the worker on its first start because it is running in Development.

## The one thing that bites on a second run

`.WithDataVolume()` and `.WithLifetime(ContainerLifetime.Persistent)` keep the container and its data
between runs, which is the point — a persistent job store whose rows disappear is an in-memory store
with more moving parts. But the AppHost generates the Postgres password, and without user secrets it
generates a **new** one each time, while the surviving volume still holds the old one. The worker then
never starts, because `WaitFor(quartzDb)` never completes, and nothing says why. The AppHost does warn
about it at startup:

```text
Resource 'postgres' has a persistent lifetime but the AppHost project does not have user secrets
configured. Generated parameter values (such as passwords) may change on each restart, causing
persistent containers to be recreated.
```

Either initialize user secrets, or delete the container **and** its volume and start again:

```shell
docker rm -f postgres-<suffix>
docker volume rm quartz.examples.aspire.apphost-<hash>-postgres-data
```

Running this example leaves that container and volume behind on purpose. `docker ps -a` and
`docker volume ls` will show them.
