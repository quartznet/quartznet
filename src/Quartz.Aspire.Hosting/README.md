# Quartz.Aspire.Hosting

The Quartz.NET dashboard as an [Aspire](https://aspire.dev/) AppHost resource. One call runs the dashboard's
container image beside your workers; `WithAttachedStore` shows every scheduler in a database, and
`WithReference(dashboard)` dials a worker in as an agent.

## Installation

In the AppHost project:

```shell
dotnet add package Quartz.Aspire.Hosting
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var quartzDb = builder.AddPostgres("postgres").AddDatabase("quartz");

var dashboard = builder.AddQuartzDashboard("quartz-dashboard")
    .WithAttachedStore(quartzDb);

builder.AddDotnetProject("orders", "../Orders.Worker/Orders.Worker.csproj")
    .WithReference(quartzDb)
    .WithReference(dashboard);

builder.Build().Run();
```

The worker calls `UseDashboardAgent()` from
[Quartz.Dashboard.Agent](https://www.nuget.org/packages/Quartz.Dashboard.Agent), which reads what
`WithReference(dashboard)` sets. The resource's URL in the Aspire dashboard signs you in as an administrator.

- Aspire 13.6.5 or later, 17 included. `AddDotnetProject` is Aspire 17's, from `Aspire.Hosting.Dotnet`; on
  Aspire 13 the worker is `AddProject<Projects.Orders_Worker>("orders")`.
- The image is `ghcr.io/quartznet/quartz-dashboard` at this package's version; `WithImageTag` runs another.
- The sign-in and agent tokens are generated secret parameters, kept in the AppHost's secrets (`~/.aspire/secrets`
  on Aspire 17, user secrets before).
- Run one replica of the dashboard.

Every setting, and the environment variables the resource writes:
[Aspire Hosting](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/aspire-hosting.html).
