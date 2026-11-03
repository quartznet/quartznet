# Quartz.Dashboard.Host

The [Quartz.NET dashboard](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard.html)
as a program of its own: no web application to write, configured from `Quartz:Dashboard` settings alone. It
fronts schedulers over their HTTP API, reads every scheduler in a database, and accepts workers that dial
in as agents. The same program ships as the container image `ghcr.io/quartznet/quartz-dashboard`.

## Installation

```shell
dotnet tool install --global Quartz.Dashboard.Host
```

## Usage

```shell
quartz-dashboard \
  --Quartz:Dashboard:Authentication:Mode=Token \
  --Quartz:Dashboard:Authentication:Tokens:Administrator=<a long random token> \
  --Quartz:Dashboard:Targets:orders:ConnectionString="Host=db;Database=quartz;Username=quartz;Password=..."
```

Open `http://localhost:5000/quartz` and sign in with the token. Settings come from command-line arguments,
environment variables (`Quartz__Dashboard__Authentication__Mode`) or an `appsettings.json` in the working
directory.

- `Authentication:Mode` is required: `Token`, `OpenIdConnect` or `Unsecured`. The host refuses to start
  without it.
- `Targets:<name>` is a scheduler's HTTP API (`Url`, `SchedulerName`, `BearerToken`) or a database
  (`ConnectionString`, `Provider`, `TablePrefix`, `Serializer`).
- `Agents:Tokens:Primary` accepts workers that call `UseDashboardAgent()`.

Every setting, the container image and the Aspire resource:
[Dashboard Host](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard-host.html).
