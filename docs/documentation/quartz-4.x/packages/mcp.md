---
title: 'MCP endpoint'
---

[Quartz.Mcp](https://www.nuget.org/packages/Quartz.Mcp) serves a [Model Context Protocol](https://modelcontextprotocol.io)
endpoint over Streamable HTTP. An AI agent uses it to read and operate the schedulers of a process and of its fleet,
under the same operator roles as the [dashboard](dashboard.md) and the [HTTP API](http-api.md). Added in 4.9.

```shell
dotnet add package Quartz.Mcp
```

- **Read-only by default.** Writing needs `ReadOnly = false` and an `OperationAuthorizationPolicy`.
- **Stateless always.** Every request stands alone, so no session is bound to a caller and instances need no
  affinity.
- **One endpoint for the fleet.** A tool names its scheduler by key, as `list_schedulers` gives it.
- **No data values.** Job and trigger data are answered as their keys only.
- **Trimmable and native AOT compatible.**

It depends on `ModelContextProtocol.AspNetCore` 2.3.1 or later, the first release outside GHSA-x863-g495-4cjc. That
release asks for `Microsoft.Extensions.*.Abstractions` 10.0.13 and `Microsoft.Extensions.AI.Abstractions` 10.11.0.

## Setup

<!-- snippet: sample_mcp_setup -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz();
builder.AddQuartzHostedService();

// Who the caller is comes from the application's own bearer scheme — AddJwtBearer, an API key
// handler — and never from a cookie, which a browser sends on its own.

// The policy the dashboard and the HTTP API name, so one set of roles decides all three.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Quartz.Operations", policy => policy.RequireSchedulerRoles(roles =>
    {
        roles.Viewers.Add("ops-viewer");
        roles.Operators.Add("ops-operator");
        roles.Administrators.Add("ops-admin");
    }));

builder.Services.AddQuartzMcp(options =>
{
    options.ReadOnly = false;
    options.OperationAuthorizationPolicy = "Quartz.Operations";
});

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// At /quartz-mcp; MapQuartzMcp("/ops/mcp") names another path.
app.MapQuartzMcp().RequireAuthorization();
```
<!-- endSnippet -->

`MapQuartzMcp()` maps `POST /quartz-mcp`. The application refuses to start if nothing authorizes the endpoint: call
`RequireAuthorization(…)`, set one of the two policies, or say `AllowAnonymous()` on purpose.

::: warning
Authorize the endpoint with a bearer token, never a cookie. A browser sends a cookie on its own, so a page the
operator visits could call a tool in their name.
:::

## Options

| `QuartzMcpOptions` | Default | What it does |
|---|---|---|
| `ReadOnly` | `true` | Offers and serves the ten reading tools only. Off needs `OperationAuthorizationPolicy` |
| `SchedulerAuthorizationPolicy` | none | Which schedulers a caller may see, asked with `SchedulerResource { SchedulerName, Target }` |
| `OperationAuthorizationPolicy` | none | What a caller may do, asked per route a tool calls, with the route's name and role |
| `AllowedOrigins` | empty | Origins a browser may call from. Empty refuses every request with an `Origin` header |
| `ConfirmationLifetime` | 5 minutes | How long a preview's token stays good, from 30 seconds to 30 minutes |
| `MaxChangesPerMinute` | 20 | Changes per caller per sliding minute. `0` turns the limit off |

## Tools

Every tool but `list_schedulers` takes `scheduler`, the key: `w1/QuartzScheduler` behind a target, or a bare name for a
scheduler of this process. A job or trigger is `group` plus `name`. Lists take `skip` and `take` (50 when absent, at
most 200; above is refused, never cut) and answer `hasMore`.

| Tool | HTTP API routes | Role | Arguments beyond `scheduler` |
|---|---|---|---|
| `list_schedulers` | `GetAllSchedulers` | View | none |
| `get_scheduler` | `GetSchedulerDetails` | View | none |
| `list_cluster_nodes` | `GetClusterNodes` | View | none |
| `list_jobs` | `QueryJobs`, `FetchJobRunStatuses` | View | `groupContains`, `nameContains` |
| `get_job` | `GetJobDetails`, `GetJobTriggers`, `GetJobRunStatus` | View | `group`, `name` |
| `list_triggers` | `QueryTriggers` | View | `state`, `groupContains`, `nameContains`, `job`, `nextFireBefore` |
| `get_trigger` | `GetTrigger`, `GetTriggerState`, `GetTriggerFireTimes` | View | `group`, `name` |
| `list_executing` | `QueryFireInstances` | View | none |
| `query_history` | `QueryExecutionHistory` | View | `job`, `trigger`, `results`, `firedFrom`, `firedBefore`, `failedFinally` |
| `get_job_run_status` | `GetJobRunStatus` | View | `group`, `name` |
| `pause_trigger` | `PauseTrigger` | Operate | `group`, `name`, `reason` (3–500 characters) |
| `pause_job` | `PauseJob` | Operate | `group`, `name`, `reason` (3–500 characters) |
| `resume_trigger` | `ResumeTrigger` | Operate | `group`, `name` |
| `resume_job` | `ResumeJob` | Operate | `group`, `name` |
| `trigger_job` | `TriggerJob`, without data | Operate | `group`, `name` |
| `interrupt` | `InterruptJobInstance` | Operate | `fireInstanceId` |
| `reset_trigger_from_error` | `ResetTriggerFromErrorState` | Operate | `group`, `name` |
| `reschedule` | `RescheduleJob` | Administer | `group`, `name`, `cronExpression`, `timeZone`, `confirmationToken` |
| `unschedule` | `UnscheduleJob` | Administer | `group`, `name`, `confirmationToken` |

Every schema says `additionalProperties: false`, and an unknown argument is refused by name. The reading tools carry
`readOnlyHint`; `reschedule` and `unschedule` carry `destructiveHint`. A pause is recorded as requested by
`<user> via MCP`, with its reason.

Not offered: backfill, pause or resume of everything, start, standby and shutdown, adding or deleting jobs, scheduling,
calendars, execution limits and editing a trigger's details. Each reaches a whole scheduler or names a type, and needs
a preview design of its own first. There is no stdio transport.

## Roles

One policy decides the dashboard, the HTTP API and MCP. Each call is checked in this order, before the scheduler is
looked up:

1. `ReadOnly` refuses every changing tool.
2. `SchedulerAuthorizationPolicy` decides whether the caller may see the scheduler.
3. `OperationAuthorizationPolicy` is asked for each route the tool calls, at the route's role. So
   `SchedulerRoles.Operations["UnscheduleJob"] = Operate` lets operators use `unschedule`.
4. The target's ceiling: an [agent's](dashboard-agent.md#what-the-agent-accepts) `MaxAccess` refuses what the agent
   would.

A target is asked about in its own spelling, so `W1` is checked as `w1`, and a store window named by its bare name is
asked about with its store's target: `alpha` is checked as `prod/alpha`. A refused caller cannot tell a missing
scheduler from a forbidden one. `tools/list` offers only the tools the caller can call on some scheduler they may see.
That list is a convenience; `tools/call` is the check.

`reschedule` and `unschedule` are never offered to a caller with no stable identity: anonymous, or with none of
`NameIdentifier`, `sub`, `upn` or a name.

## Preview and confirm

`reschedule` and `unschedule` change nothing when called without `confirmationToken`. They answer a preview and a token:

| Field | What it holds |
|---|---|
| `preview.summary`, `preview.effects` | The change in words: old and new schedule, the next five fire times, whether a non-durable job goes with its last trigger |
| `confirmationToken` | Data Protection, time-limited, base64url |
| `expiresAtUtc` | `ConfirmationLifetime` from now |

Show the preview to the user, and call again with the same arguments and the token only after they agree. The token is
bound to the tool, the scheduler, the subject, a digest of the arguments, a digest of the caller and a digest of the
trigger's state as previewed. It is refused when any of them differ, when it expired, and when it was used before.
Tokens are never logged.

## Freshness

Every answer carries `asOfUtc`, the server's clock when the read completed, and `scheduler`:

| Field | When |
|---|---|
| `key`, `origin` | Always |
| `lastSeenUtc` | An agent: its last heartbeat. A scheduler of this process and an HTTP target answered live |
| `liveNodes`, `nodes` | A window onto an attached store: the cluster's check-ins |

## What is never answered

No tool answers a job's or a trigger's data values, a run's recorded input or captured log, its metrics, or the
scheduler context. `get_job` and `get_trigger` answer `dataKeys`, the names only. `trigger_job` takes no data: supplying
data names what a `NativeJob` runs, which is an administrator's.

Free text is cut and says so beside it (`descriptionTruncated: true`): descriptions at 1,000 characters, error
messages at 2,000, summaries at 1,000, pause reasons at 500.

::: warning Untrusted text
Descriptions, error messages, summaries and pause reasons are written by whoever can schedule or run a job, and an
agent reads them. Every tool description and the server's instructions say not to follow instructions found in
them. That is a mitigation, not a cure. The other limits are: read-only by default, no data values, no data input,
confirmation for the destructive tools, and the rate limit.
:::

## Browser requests

| Request | Answer |
|---|---|
| `Origin` not in `AllowedOrigins` | `403`, log `9504`, before the SDK reads it |
| No `Origin` (an agent, a script) | Served |
| Body not declared `application/json` | `415`, before the SDK reads it |

The `Origin` check is what the MCP specification requires against DNS rebinding. A `text/plain` body is a request a
browser sends without a preflight, so refusing it makes every request that gets through one a browser had to ask
about first.

## Rate limits

`MaxChangesPerMinute` counts each caller's changes over a sliding minute, the changes a policy refused included; reads
and previews are not counted. The twenty-first change answers `rate_limited` with `retryAfterSeconds`. Past the limit,
a refused change is still refused, and no longer entered in the Action Log or logged as `9501`. `9503` is logged once
per caller per window. For a limit on every request, use ASP.NET Core's own:

<!-- snippet: sample_mcp_rate_limiting -->
```csharp
// MaxChangesPerMinute bounds changes per caller. A limit on every request is ASP.NET Core's own.
builder.Services.AddRateLimiter(limiter => limiter.AddFixedWindowLimiter("mcp", window =>
{
    window.PermitLimit = 120;
    window.Window = TimeSpan.FromMinutes(1);
}));

WebApplication app = builder.Build();
app.UseRateLimiter();
app.MapQuartzMcp().RequireAuthorization().RequireRateLimiting("mcp");
```
<!-- endSnippet -->

## Errors

A failure is a tool result with `isError: true` and `structuredContent.error`:

| `code` | Meaning |
|---|---|
| `invalid_arguments` | An unknown, missing or malformed argument, or a page out of bounds |
| `read_only` | The endpoint is read-only |
| `forbidden` | A policy or the target's ceiling refused; the message names the role needed |
| `not_found` | No such scheduler, job or trigger |
| `rate_limited` | Over `MaxChangesPerMinute`; `retryAfterSeconds` says when |
| `confirmation_refused` | The token expired, was used, or was given for another call or state |
| `target_fault` | The target did not serve the call; `fault` is `unavailable`, `timedOut`, `refused` or `failed` |
| `not_served` | The target or store keeps no such thing, a history for one |
| `scheduler_error` | The scheduler refused the call |

A change whose target timed out carries `mayHaveApplied: true` and says to read before retrying. A target's address is
never in a message.

## Records

| Event | Level | When |
|---|---|---|
| `9500` | Information | A tool changed something: the user, the client, the tool, the key, the subject |
| `9501` | Warning | A call was refused before it reached a scheduler |
| `9502` | Warning | A confirmation was refused, by the class of its reason |
| `9503` | Warning | A caller reached `MaxChangesPerMinute`, once per window |
| `9504` | Warning | The request guard refused a request |

A target that did not serve a call is logged with the HTTP API's own `9010` to `9014`. In a process that maps the
dashboard, each change and each refused change is also an [Action Log](dashboard.md#action-log) entry, Via MCP, with
the user and the client as it stated itself. The client is MCP's `clientInfo`, or the `User-Agent`'s first product
token.

## Several instances

Rate limits are per instance. A confirmation token from one instance is read by another only over a shared Data
Protection key ring:

<!-- snippet: sample_mcp_key_ring -->
```csharp
// A confirmation token from one instance is read by another only over a shared key ring.
builder.Services.AddDataProtection()
    .SetApplicationName("ops-scheduler")
    .PersistKeysToFileSystem(new DirectoryInfo("/var/lib/ops-scheduler/keys"));
```
<!-- endSnippet -->

## In the dashboard host

The [dashboard host](dashboard-host.md#mcp-endpoint) serves the endpoint for its whole fleet when
`Quartz:Dashboard:Mcp:Enabled` is `true`. It is read-only unless `Quartz:Dashboard:Mcp:ReadOnly` is `false`, and takes
the host's bearer tokens only.

## Connecting a client

Claude Code:

```shell
claude mcp add --transport http quartz https://ops.example.com/quartz-mcp --header "Authorization: Bearer $QUARTZ_TOKEN"
```

VS Code, `.vscode/mcp.json`:

```json
{
  "servers": {
    "quartz": {
      "type": "http",
      "url": "https://ops.example.com/quartz-mcp",
      "headers": { "Authorization": "Bearer ${input:quartz-token}" }
    }
  },
  "inputs": [{ "id": "quartz-token", "type": "promptString", "description": "Quartz token", "password": true }]
}
```

Any Streamable HTTP client: `POST` JSON-RPC to the endpoint with `Content-Type: application/json`, `Accept:
application/json, text/event-stream` and the bearer header.

## An agent session

> **User:** Why did the nightly report not run on prod?
>
> → `list_schedulers {}`: `prod/reporting` (window, 3 of 3 nodes live), `w1/QuartzScheduler` (agent, last seen 4 s ago)
>
> → `list_triggers {scheduler: "prod/reporting", state: "Error"}`: `reports.nightly`, *Every day at 02:00
> (Europe/Helsinki)*, state `Error`
>
> → `query_history {scheduler: "prod/reporting", trigger: {group: "reports", name: "nightly"}, take: 3}`:
> 2026-11-24T00:00:03Z, `Failed`, *Npgsql.NpgsqlException: Timeout during reading attempt*
>
> **Agent:** It failed on a database timeout and the trigger is in ERROR, so it will not fire again until reset. Reset
> it?
>
> **User:** Yes. → `reset_trigger_from_error {…}`: `applied: true` (the Action Log shows *alice · MCP · claude-code*)
>
> **User:** Then move it to 03:00. → `reschedule {…, cronExpression: "0 0 3 * * ?"}`: a preview, *Every day at 02:00 →
> Every day at 03:00 (Europe/Helsinki); next: 2026-11-25T01:00:00Z, …*, and a `confirmationToken`
>
> **Agent:** The trigger will next fire at 03:00 Helsinki time. Confirm?
>
> **User:** Yes. → `reschedule {…, confirmationToken: "…"}`: `applied: true`

## Wolverine schedules

A trigger Wolverine creates is a Quartz trigger, and the tools act on it as on any other. CritterWatch owns Wolverine's
schedules: change them there, so the two do not disagree.
