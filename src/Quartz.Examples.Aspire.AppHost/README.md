# The Aspire example's AppHost

Declares the resources the example needs — a PostgreSQL server with a database on it, the Quartz.NET
dashboard, and the worker that schedules against the database and dials in to the dashboard — and nothing
else. The prose is
[Running Quartz under Aspire](https://www.quartz-scheduler.net/documentation/quartz-4.x/how-tos/aspire.html);
the worker half is [`Quartz.Examples.Aspire.Worker`](../Quartz.Examples.Aspire.Worker/README.md).

## Running it

```shell
dotnet run --project src/Quartz.Examples.Aspire.AppHost
```

It needs **a running container runtime**, Docker Desktop, Podman or Rancher Desktop, and **the Aspire CLI**,
from [get.aspire.dev](https://get.aspire.dev). This is an Aspire 17 AppHost: `dotnet run` hands it to
`aspire run`, and the CLI brings the Aspire dashboard and the Developer Control Plane. Without an `aspire` on
`PATH`, the build and `dotnet run` fetch `aspire.cli` at the AppHost's Aspire version with `dnx`.

- **An untrusted HTTPS development certificate stops the run** while `aspire run` asks to trust it, and on
  Windows the question is a dialog. `aspire certs trust` answers it once; `aspire run --apphost
  src/Quartz.Examples.Aspire.AppHost --non-interactive` warns and runs without.
- **From a checkout of `main` the dashboard's image is not published yet.** The resource runs
  `ghcr.io/quartznet/quartz-dashboard` at the package's own version, `VersionPrefix` in `Directory.Build.props`.
  Build it into Docker first:

  ```shell
  dotnet publish src/Quartz.Dashboard.Host -t:PublishContainer -p:RuntimeIdentifier=linux-x64 -p:ContainerRuntimeIdentifier=linux-x64 -p:LocalRegistry=Docker -p:ContainerRepository=quartz-dashboard-local -p:ContainerImageTags=<version>
  docker tag quartz-dashboard-local:<version> ghcr.io/quartznet/quartz-dashboard:<version>
  ```

The console prints a dashboard URL. Open it and you will see the `postgres` resource, the `quartz` database on
it, the `quartz-dashboard` container, whose URL signs you in to the Quartz.NET dashboard, and the `worker`,
whose `/health` endpoint the AppHost polls. The worker's log shows it registering with the Quartz.NET dashboard
as `worker/orders` and a heartbeat job firing every ten seconds; the database ends up with the `qrtz_*` tables,
created by the worker on its first start because it is running in Development.

## Why the password is pinned

`.WithDataVolume()` and `.WithLifetime(ContainerLifetime.Persistent)` keep the container and its data
between runs, which is the point — a persistent job store whose rows disappear is an in-memory store
with more moving parts. But the volume also keeps the password the database was created with, and a password
Aspire *generates* lives somewhere else: in `~/.aspire/secrets/<apphost>/<environment>.json` on Aspire 17, and
before that in user secrets, or nowhere, in which case each start generated a new one. Once the two disagree,
the worker never starts, because `WaitFor(quartzDb)` never completes, and nothing says why.

So `Program.cs` pins one with `builder.AddParameter("postgres-password", "…", secret: true)`, and every
run connects. **That value is in the source, which is fine for a container on your own machine and is not
how a deployment does it**: leave the value off — `builder.AddParameter("postgres-password",
secret: true)` — and supply it from the AppHost's secrets, the environment or a key vault.

Running this example leaves the container and its volume behind on purpose. `docker ps -a` and
`docker volume ls` will show them; remove them with:

```shell
docker rm -f postgres-<suffix>
docker volume rm quartz.examples.aspire.apphost-<hash>-postgres-data
```
