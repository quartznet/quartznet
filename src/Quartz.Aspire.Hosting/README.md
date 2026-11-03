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

builder.AddProject<Projects.Orders_Worker>("orders")
    .WithReference(quartzDb)
    .WithReference(dashboard);

builder.Build().Run();
```

The worker calls `UseDashboardAgent()` from
[Quartz.Dashboard.Agent](https://www.nuget.org/packages/Quartz.Dashboard.Agent), which reads what
`WithReference(dashboard)` sets. The resource's URL in the Aspire dashboard signs you in as an administrator.

- The image is `ghcr.io/quartznet/quartz-dashboard` at this package's version; `WithImageTag` runs another.
- The sign-in and agent tokens are generated secret parameters, kept in the AppHost's user secrets.
- Run one replica of the dashboard.

Every setting, and the environment variables the resource writes:
[Aspire Hosting](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/aspire-hosting.html).
