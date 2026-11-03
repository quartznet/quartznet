---
title: 'Aspire Hosting'
---

# Aspire Hosting

[Quartz.Aspire.Hosting](https://www.nuget.org/packages/Quartz.Aspire.Hosting) is the Quartz.NET **hosting
integration** for [Aspire](https://aspire.dev/) (4.8): an AppHost resource that runs the
[dashboard host](dashboard-host.md)'s image beside your workers. It shows every scheduler in a database, and the
schedulers of the workers that dial in as [agents](dashboard-agent.md).

```shell
dotnet add package Quartz.Aspire.Hosting
```

In the AppHost project. The workers keep [Quartz.Aspire](aspire.md), the client integration, for their store.

## Usage

<!-- Not a compiled sample: the generated Projects class comes from an AppHost project. The same calls are
     compiled in src/Quartz.Examples.Aspire.AppHost. -->

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

The worker dials in with [`UseDashboardAgent()`](dashboard-agent.md#from-configuration), which reads what
`WithReference(dashboard)` sets:

<!-- snippet: sample_dashboard_agent_from_configuration -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddQuartz(q =>
{
    // Endpoint, Token and Target come from Quartz:DashboardAgent - an Aspire AppHost's
    // WithReference(dashboard) sets them as Quartz__DashboardAgent__* environment variables.
    q.UseDashboardAgent();
});

builder.AddQuartzHostedService();
```
<!-- endSnippet -->

Open the resource's URL in the Aspire dashboard: it signs the browser in as an administrator.

- **Choose one per scheduler.** `WithAttachedStore` shows every scheduler in the database, read from its tables;
  `WithReference(dashboard)` shows the worker's scheduler through the worker. A scheduler reached both ways is
  listed twice.
- **An agent is driven through its worker**: starting, standing by, interrupting and the live events work. A
  store-attached scheduler is read from the database; see
  [What a window can and cannot do](dashboard.md#what-a-window-can-and-cannot-do).

## The calls

| Call | What it does |
|---|---|
| `AddQuartzDashboard(name, port, adminToken, agentToken)` | Adds the dashboard container. `port` is the host port; the tokens default to generated secrets |
| `WithAttachedStore(database, target, configure)` | Shows every scheduler in `database`, under `target` (default: the database resource's name). Waits for the database |
| `WithReference(dashboard, schedulerName)` | Makes this resource's scheduler an agent of `dashboard`, under the target `{resource}` or `{resource}-{schedulerName}` |
| `WithImageTag(tag)` | Runs another version of the image (Aspire's own call) |

`QuartzAttachedStoreSettings`, the `configure` callback's argument, says what the nodes writing the database were
configured with: `Provider` (inferred when unset), `TablePrefix`, `Serializer`, `ExecutionHistory` and
`RediscoveryInterval`. Each is the [store-attached target key](dashboard-host.md#targets) of the same name.

- **A target name is one scheduler's.** A second store or agent under a name the dashboard already has throws while
  the AppHost is built.
- **`schedulerName`** is for a worker with several named schedulers: it writes that scheduler's own section,
  `Quartz__Schedulers__{schedulerName}__DashboardAgent__*`.

## Tokens and the sign-in link

| Parameter | Generated as | What it is |
|---|---|---|
| `{name}-admin-token` | 32 letters and digits, secret | The administrator's sign-in token |
| `{name}-agent-token` | 32 letters and digits, secret | The token agents present |

- Generated values are kept in the AppHost's user secrets, under `Parameters:{name}-admin-token`. Set one there, or
  pass a parameter of your own as `adminToken` or `agentToken`, to choose it.
- **The resource's URL is `/login?t=<admin token>`.** The resource sets `AllowTokenInUrl` on the dashboard; nothing
  else does. The dashboard moves the browser on to an address without the token.

## The environment it writes

These keys are the contract between this package and the two programs. The package references nothing of Quartz.

| On | Variable | Value |
|---|---|---|
| The dashboard | `Quartz__Dashboard__Authentication__Mode` | `Token` |
| | `Quartz__Dashboard__Authentication__AllowTokenInUrl` | `true` |
| | `Quartz__Dashboard__Authentication__Tokens__Administrator` | the admin token |
| | `Quartz__Dashboard__Agents__Tokens__Primary` | the agent token |
| | `Quartz__Dashboard__Targets__{target}__ConnectionString` | the database's connection string |
| | `Quartz__Dashboard__Targets__{target}__Provider`, `TablePrefix`, `Serializer`, `ExecutionHistory`, `RediscoveryInterval` | when set |
| | `OTEL_*` | Aspire's, so the dashboard's own telemetry reaches the Aspire dashboard |
| A worker | `Quartz__DashboardAgent__Endpoint` | `{dashboard URL}/quartz/agents` |
| | `Quartz__DashboardAgent__Token` | the agent token |
| | `Quartz__DashboardAgent__Target` | the worker's resource name |

## The image

The image is `ghcr.io/quartznet/quartz-dashboard` at **this package's version**, so the two are released together.
A build of the package that is not a release runs a tag that does not exist; give it one with
`.WithImageTag("4.8.0")`.

## Limitations

- **One dashboard replica.** It holds its agents' connections in memory; `WithReplicas(n)` above one splits them
  between replicas.
- **A worker with replicas is shown one replica at a time.** Every replica registers under the same target, and the
  dashboard holds a target for one scheduler instance: the others are refused until it leaves. For a replicated
  worker, cluster its store and use `WithAttachedStore`, which shows the cluster and every node in it.
- **Store-attached schedulers show Quartz's own job data types**; see
  [Dashboard Host](dashboard-host.md#targets).
- `net10.0`, like every Quartz 4 package.
