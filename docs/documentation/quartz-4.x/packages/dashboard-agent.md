---
title: 'Dashboard Agent'
---

[Quartz.Dashboard.Agent](https://www.nuget.org/packages/Quartz.Dashboard.Agent) puts a scheduler on a
[dashboard](dashboard.md) running somewhere else, over one outbound connection the scheduler opens itself. The
dashboard shows and drives it — every page, every action, the history and the live events — with no inbound port
on the worker, no HTTP API in it, and no shared database.

```shell
dotnet add package Quartz.Dashboard.Agent
```

Choose it over an [HTTP target](dashboard.md#fronting-a-scheduler-in-another-process-over-http) when the worker
cannot be dialled: it is behind NAT or a firewall, it runs where no port can be opened, or the dashboard is on the
other side of a trust boundary. Choose it over a [store-attached target](dashboard.md#store-attached-targets)
when the dashboard must not hold the database credentials, or when the worker runs on the in-memory store.

## Setup

The dashboard accepts agents with `AcceptAgents`, which maps the agent hub at `{DashboardPath}/agents`:

<!-- snippet: sample_dashboard_accept_agents -->
```csharp
builder.Services.AddQuartzDashboard(options => options.AcceptAgents(agents =>
{
    // The token every agent presents, as an Authorization: Bearer header. Keep it in a
    // secret store: whoever holds it can register a scheduler on this dashboard.
    agents.Tokens.Primary = builder.Configuration["Dashboard:AgentToken"];
}));
```
<!-- endSnippet -->

The worker dials it:

<!-- snippet: sample_dashboard_agent_worker -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddQuartz(q =>
{
    q.UseDashboardAgent(agent =>
    {
        // The dashboard's path followed by /agents: AcceptAgents maps the hub there.
        agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");

        // Sent as an Authorization: Bearer header on every connection, never in the URL.
        agent.Token = builder.Configuration["Dashboard:AgentToken"];

        // The first half of the scheduler's key on the dashboard: worker-1/QuartzScheduler.
        // Defaults to the machine name.
        agent.Target = "worker-1";

        // Which job types a request through this agent may name. Unset, every one is refused.
        agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
    });
});

builder.AddQuartzHostedService();
```
<!-- endSnippet -->

The dashboard then lists the scheduler as `worker-1/QuartzScheduler`, reached through an `agent`. What the dashboard
side configures — tokens, rotation, liveness, the hub's authorization — is in
[Fronting a scheduler on another machine](dashboard.md#fronting-a-scheduler-on-another-machine).

## Options

`UseDashboardAgent` takes a `DashboardAgentOptions`:

| Option | Default | What it is |
|---|---|---|
| `Endpoint` | required | The dashboard's agent hub: its path followed by `/agents` |
| `Token` | — | The bearer token the dashboard accepts. Required unless `AccessTokenProvider` is set |
| `AccessTokenProvider` | — | `Func<CancellationToken, ValueTask<string?>>`: the host's own credential, asked before every connection |
| `Target` | the machine name | The first half of the scheduler's key on the dashboard. No `/` or `+` |
| `ReadOnly` | `false` | Refuse every operation that changes something |
| `IsJobTypeAllowed` | `null`: every type refused | Which job types a request may name; see [What the agent accepts](#what-the-agent-accepts) |
| `IsOperationAllowed` | `null`: every operation | Which operations the agent accepts, by route name (`Shutdown`, `PauseJob`, `ScheduleJob`, …) |
| `HeartbeatInterval` | 15 s | How often the agent says it is alive; the dashboard's own interval replaces it at registration |
| `MaxConcurrentOperations` | 4 | How many of the dashboard's requests run at once; the rest queue |
| `MaxPageSize` | 1000 | The most items one listing returns; `0` for no limit |
| `ConfigureConnection` | — | The SignalR client's `HttpConnectionOptions`: a proxy, a client certificate, the transports |

Validated at scheduler start: `Endpoint` absolute, a token or a provider, `HeartbeatInterval` positive,
`MaxConcurrentOperations` at least one, `MaxPageSize` not negative, `Target` without `/` or `+`.

`UseDashboardAgent` also calls `AddQuartzExecutionHistory()`, so a worker with an agent records what it ran in
memory, as a worker serving the HTTP API does. The dashboard's History page reads it through the connection.

### Authenticating with the host's credential

A token is a shared secret. Where the host authenticates machines itself — a managed identity, an OIDC client
credential — hand the agent a provider and hold the hub to a policy that accepts what it presents:

<!-- snippet: sample_dashboard_agent_host_auth -->
```csharp
builder.AddQuartz(q =>
{
    q.UseDashboardAgent(agent =>
    {
        agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");

        // The host's own credential instead of a shared secret: asked before every connection,
        // so an expiring token is renewed. The dashboard holds the hub to a policy that
        // accepts it: AcceptAgents(a => a.AuthorizationPolicy = "agents").
        agent.AccessTokenProvider = cancellationToken =>
            credential.GetAccessToken("api://quartz-dashboard/.default", cancellationToken);

        agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
    });
});
```
<!-- endSnippet -->

Either way the credential travels as an `Authorization: Bearer` header on every connection, never in the URL.

### The connection

<!-- snippet: sample_dashboard_agent_connection -->
```csharp
q.UseDashboardAgent(agent =>
{
    agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");
    agent.Token = "…";
    agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);

    // The connection is the SignalR client's: a proxy, a client certificate, the transports.
    agent.ConfigureConnection = connection =>
    {
        connection.Proxy = new WebProxy("http://proxy.internal:3128");
        connection.ClientCertificates = [X509CertificateLoader.LoadPkcs12FromFile("worker-1.pfx", password: null)];
    };
});
```
<!-- endSnippet -->

## What the agent accepts

An agent crosses a trust boundary by design. The narrowings below are decided in the worker's process, and nothing
the dashboard sends can widen them; the dashboard's own `ReadOnly` hides its buttons and binds no agent.

| Setting | Refuses | Unset |
|---|---|---|
| `IsJobTypeAllowed` | storing a job *by type name* whose name the predicate rejects: add-job, schedule-job with a job, schedule-jobs | **every job type** |
| `ReadOnly` | every operation that changes something | nothing |
| `IsOperationAllowed` | every operation whose route name the predicate rejects | nothing |

- A refusal is `403` with problem details. The dashboard shows it as the refusal it is; the refusal of an unset
  `IsJobTypeAllowed` names the option as the remedy. The agent logs every refusal (`9304`).
- With `IsJobTypeAllowed` unset, everything else works: triggering, pausing, rescheduling and deleting a job the
  scheduler already holds, and scheduling a trigger for one.
- The predicate sees the job type name as the request wrote it, and nothing resolves it first. One type has more
  than one spelling, so match on the namespace: `name.StartsWith("Acme.Jobs.", StringComparison.Ordinal)`.
- The default is the opposite of `QuartzHttpApiOptions.IsJobTypeAllowed`'s, because the HTTP API is on the
  application's own network and an agent is not. With `Quartz.Jobs` on the probing path, a dashboard that may name
  any `IJob` may run any executable on the worker.

<!-- snippet: sample_dashboard_agent_narrowed -->
```csharp
builder.AddQuartz(q =>
{
    q.UseDashboardAgent(agent =>
    {
        agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");
        agent.Token = builder.Configuration["Dashboard:AgentToken"];

        // Every operation that changes something is 403; the dashboard shows the refusal.
        agent.ReadOnly = true;

        // Or keep writes and refuse by route name — the operation ids the HTTP API publishes.
        agent.IsOperationAllowed = operation => operation is not ("Shutdown" or "Clear" or "DeleteJobs");

        // The job types the agent will store, matched on the name as the request wrote it.
        agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
    });
});
```
<!-- endSnippet -->

## How it runs

| | |
|---|---|
| Connection | One outbound SignalR connection to `Endpoint`, over the transports the client negotiates (WebSockets first). Opened when the scheduler starts, closed when it shuts down |
| Registration | The agent registers its target, scheduler name, instance id, Quartz version and the operations it serves. A target held by a live agent of another instance is refused; the agent retries every 30 s. The same instance reconnecting takes its own entry over |
| Heartbeat | Every `HeartbeatInterval` as the dashboard set it at registration. Three missed in a row and the dashboard lists the scheduler as `Unknown` with when it was last heard; the next heartbeat restores it |
| Operations | Each page read and each action is one request down the connection, answered with the HTTP API's status and body. The dashboard cancels one that takes longer than its `OperationTimeout` (30 s) on both sides, queued or running. Four requests per worker may wait their turn; one more is answered `503` at once |
| Page size | An answer larger than the dashboard's `MaxMessageBytes` (4 MiB) is refused by the agent with `413`, telling the page to lower `take`; the connection stays up |
| Events | Streamed up only while a Live Logs page watches the scheduler; what the broker drops under backpressure is dropped, as over HTTP |
| Dashboard down | At start, dialled again after 1 s, doubling to 30 s, until it answers; a dropped connection is redialled after 0, 2, 10 and then every 30 s, for as long as the scheduler runs |
| Shutdown | The agent says goodbye; the dashboard lists the scheduler as `Shutdown` until `ForgetAfter` (an hour) passes |

The agent never blocks or fails the scheduler's start: a dashboard that is down is logged (`9301`, once per outage)
and dialled again.

## Logging

Under the category `Quartz.Dashboard.Agent`; the dashboard's side is `9111`–`9117` under `Quartz.Dashboard`.

| Id | Level | When |
|---|---|---|
| 9300 | Information | Registered with the dashboard |
| 9301 | Warning | The dashboard is unreachable; retrying |
| 9302 | Warning | The dashboard refused the registration |
| 9303 | Information | The dashboard closed the connection; reconnecting |
| 9304 | Warning | An operation was refused: read-only, operation list, job type, page size |
| 9305 | Error | An operation failed |

Each is in [Log Events](../log-events.md).

## Trust model

- **The dashboard is trusted by every agent that dials it.** Whoever runs the dashboard can perform on the worker
  every operation the agent accepts. The agent's token or credential authenticates the dashboard to the agent as
  much as the reverse.
- **The agent's narrowings are the only ones that hold** against a dashboard that has been taken over:
  `ReadOnly`, `IsOperationAllowed`, `IsJobTypeAllowed`.
- **Tokens are shared secrets.** Keep them in a secret store, rotate them through the dashboard's
  [two slots](dashboard.md#fronting-a-scheduler-on-another-machine), and treat the dashboard host as the trust
  anchor of the fleet.
- **The transport is the SignalR client's.** TLS, certificate validation and the proxy are what
  `ConfigureConnection` sets; the agent changes none of them.

## Trimming

The package compiles with the trim, AOT and single-file analyzers on. The one warning it owns — the job type on
the wire is a string the scheduler resolves — is recorded in its `TrimAnalysisBaseline.cs`, as `Quartz.HttpClient`
records the same site. No trimmed canary publishes it in 4.5; see
[Trimming and native AOT](../how-tos/trimming-and-native-aot.md).

## Limitations

- **One dashboard instance.** Agents register with the instance they dialled, and the hub keeps that registration
  in its process. A dashboard scaled out behind a load balancer shows each agent on one instance only.
- **The scheduler listing is the agent's own.** The dashboard asks the agent about its scheduler only; another
  scheduler in the worker's container needs an agent of its own, with a target of its own.
- **Nothing is replayed.** A page open during a disconnection sees a gap in Live Logs; the History page has what
  fell into it.
