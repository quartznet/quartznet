---
title: 'Dashboard Host'
---

# Dashboard Host

The [dashboard](dashboard.md) as a program of its own, configured from settings alone (4.8). It fronts schedulers
over their [HTTP API](http-api.md), reads every scheduler in a database, and accepts workers that dial in as
[agents](dashboard-agent.md). One program, two distributions:

| | Get it | Settings from |
|---|---|---|
| `dotnet tool` | `dotnet tool install --global Quartz.Dashboard.Host`, then `quartz-dashboard` | Arguments, environment variables, `appsettings.json` in the working directory |
| Container image | `ghcr.io/quartznet/quartz-dashboard` | Environment variables, `appsettings.json` mounted at `/app` |

From an Aspire AppHost, use [Aspire Hosting](aspire-hosting.md), which runs the image and writes its settings.

## Running it

```shell
quartz-dashboard --urls http://localhost:5000 \
  --Quartz:Dashboard:Authentication:Mode=Token \
  --Quartz:Dashboard:Authentication:Tokens:Administrator=<32 random characters> \
  --Quartz:Dashboard:Targets:orders:ConnectionString="Host=db;Database=quartz;Username=quartz;Password=…"
```

```shell
docker run -p 8080:8080 \
  -e Quartz__Dashboard__Authentication__Mode=Token \
  -e Quartz__Dashboard__Authentication__Tokens__Administrator=<32 random characters> \
  -e Quartz__Dashboard__Targets__orders__ConnectionString="Host=db;Database=quartz;Username=quartz;Password=…" \
  ghcr.io/quartznet/quartz-dashboard:4.8
```

Open `/quartz` and sign in with the token. Every key below is under `Quartz:Dashboard`, written
`Quartz__Dashboard__…` as an environment variable.

| Path | What it is |
|---|---|
| `/quartz` | The dashboard (`DashboardPath`). `/` redirects here |
| `/login`, `/logout` | Sign-in and sign-out (`Token` mode) |
| `/quartz/agents` | The agent hub, when an agent token is set |
| `/quartz-api` | The [HTTP API](#the-http-api) for every scheduler the dashboard shows, when `HttpApi:Enabled` (4.9) |
| `/quartz-mcp` | The [MCP endpoint](#mcp-endpoint) for every scheduler the dashboard shows, when `Mcp:Enabled` (4.9) |
| `/healthz` | Anonymous liveness: `200 Healthy` while the process answers. It asks no target and no database |

## Authentication

`Authentication:Mode` is required. **Without it the host refuses to start**: it logs event `9402` and exits with
code 1, as it does for any other mistake in its settings.

| Mode | Who signs in | Settings |
|---|---|---|
| `Token` | Whoever presents a token on the sign-in form, at that token's role | `Tokens:Viewer`, `Tokens:Operator`, `Tokens:Administrator` (at least one, 16+ characters, all different); `AllowTokenInUrl` |
| `OpenIdConnect` | Users of an identity provider, at the role `Roles:*` maps theirs to | `Authority`, `ClientId`, `ClientSecret`, `Scopes`, `NameClaimType` (`name`), `RoleClaimType` (`roles`), `RequireHttpsMetadata` (`true`) |
| `Unsecured` | Every visitor, without a credential | `UnsecuredAccess`: `View` (default) or `Operate`, never `Administer` |

- **The roles are the [operator roles](dashboard.md#operator-roles).** A viewer reads, an operator also pauses,
  resumes, triggers and interrupts, an administrator also adds, edits and deletes. The host builds the policy with
  `RequireSchedulerRoles`.
- **`Token` mode** compares tokens in constant time and signs the visitor in with an HttpOnly, SameSite=Strict
  cookie for eight hours, renewed while in use. `AllowTokenInUrl=true` also accepts `/login?t=<token>`, and moves
  the browser on to an address without it. Off by default; [Aspire Hosting](aspire-hosting.md) sets it for its
  sign-in link.
- **`OpenIdConnect` mode** needs `Roles:Viewers`, `Roles:Operators` or `Roles:Administrators`, the provider's role
  values; **without them the host refuses to start**. Its cookie is SameSite=Lax: the provider sends the browser
  back from another site. Register `{host}/signin-oidc` as the redirect URI.
- **`Unsecured` mode** logs warning `9401` at every start. Use it on a network where every visitor is already
  trusted.
- **`Roles:Operations:<operation>`** moves an operation to `Operate` or `Administer` in every mode:
  `Roles:Operations:AddCalendar=Operate`. A name the HTTP API does not publish refuses the start. In `Unsecured`
  mode the host also refuses to start when an entry lowers an operation that is `Administer` by default
  (`AddJob`, `DeleteJob`, `Shutdown`) to `Operate`: an anonymous visitor is never given an Administer operation.
  Raising an operation to `Administer` stays allowed.

The host never logs a token: the hosting layer's request lines, which carry the address and its query, are held
at `Warning` whatever `Logging` says, per-provider settings such as `Logging:Console:LogLevel` included.

## Targets

`Targets:<name>` is one target, and `<name>` is the first half of each scheduler's identity on the dashboard
(`orders/OrdersScheduler`). It cannot contain `/` or `+`. A target is a scheduler's HTTP API **or** a database;
saying both refuses the start.

| HTTP target key | Default | What it is |
|---|---|---|
| `Url` | required | The HTTP API's base address: `https://orders.internal/quartz-api/` |
| `SchedulerName` | required | The remote scheduler's name |
| `BearerToken` | — | Sent as `Authorization: Bearer` |
| `Headers:<header>` | — | Sent on every request |
| `Timeout` | 100 s | The `HttpClient` timeout |

| Store-attached target key | Default | What it is |
|---|---|---|
| `ConnectionString` | — | The database, or name one with `ConnectionStringName` |
| `ConnectionStringName` | — | A `ConnectionStrings:<name>` entry in the host's own settings |
| `Provider` | inferred | `Npgsql`, `SqlServer`, `MySqlConnector`, `OracleODPManaged`, `SQLite-Microsoft` or `Firebird` |
| `TablePrefix` | `QRTZ_` | The nodes' table prefix |
| `Serializer` | `SystemTextJson` | `SystemTextJson` or `Newtonsoft`, whichever the nodes write with |
| `ExecutionHistory` | `false` | The nodes keep their history in the database (`UseExecutionHistory()`) |
| `RediscoveryInterval` | `00:01:00` | How often the database is asked which schedulers it holds; `00:00:00` asks once |

- **The provider is inferred** from the connection string's keywords, by the rules
  [Quartz.Aspire uses](aspire.md#which-database-the-connection-string-is-for). An ambiguous string refuses the
  start; set `Provider`. `MySql` and `SQLite` are read with MySqlConnector and Microsoft.Data.Sqlite, the drivers the
  host carries.
- **A store-attached target reads the job data types Quartz writes itself.** The host does not have the
  application's types, so a job data map holding one is shown as unreadable.
- What a store-attached target can and cannot do is in [Store-attached targets](dashboard.md#store-attached-targets).

## Agents

`Agents:Tokens:Primary` makes the host accept [agents](dashboard-agent.md) at `{DashboardPath}/agents`. A worker
calls `UseDashboardAgent()` with `Quartz:DashboardAgent:Endpoint`, `Token` and `Target` set.

| Key | Default | What it is |
|---|---|---|
| `Agents:Tokens:Primary` | — | The token agents present. Set it to accept agents |
| `Agents:Tokens:Secondary` | — | A second token, while a rotation is under way |
| `Agents:HeartbeatInterval` | `00:00:15` | How often agents report |
| `Agents:OfflineAfterMissedHeartbeats` | `3` | Missed heartbeats before an agent is shown as unknown |
| `Agents:OperationTimeout` | `00:00:30` | How long one request through an agent may take |
| `Agents:MaxMessageBytes` | 4 MB | The largest answer an agent may send |
| `Agents:ForgetAfter` | `01:00:00` | How long a disconnected agent stays listed |

**Run one instance while the host accepts agents.** Their connections and the answers in flight are in that
process's memory.

## The HTTP API

From 4.9, `HttpApi:Enabled=true` serves the scheduler [HTTP API](http-api.md) for every scheduler the dashboard
shows, at one path. A request names a scheduler behind a target with `?target=`:
`GET /quartz-api/schedulers/OrdersScheduler/jobs?target=orders`. Addressing, the store-attached routes and the
error statuses are in [Through the dashboard host](http-api.md#through-the-dashboard-host-target).

| Key | Default | What it is |
|---|---|---|
| `HttpApi:Enabled` | `false` | Serve the API. Off, because a new write surface on a running deployment is asked for |
| `HttpApi:Path` | `/quartz-api` | Where. Not `/`, the dashboard's path, `/login`, `/logout` or `/healthz`, nor under them |
| `HttpApi:ReadOnly` | the host's `ReadOnly` | Refuse every change through the API |
| `AllowedOrigins:<n>` | none | Origins a browser may call the API from, as `scheme://host[:port]` |
| `Authentication:ApiAudience` | — | `OpenIdConnect` mode: the audience a bearer token must carry. Required with the API |

**Bearer only.** The API never reads the sign-in cookie, so a page of another site cannot borrow a visitor's
session, and it never reads `?t=`. An unauthenticated call is `401` with `WWW-Authenticate: Bearer`, never a
redirect.

| Mode | The bearer |
|---|---|
| `Token` | A role's token: `Authorization: Bearer <Tokens:Operator>`, at that role. A refused token is logged as `9410`, without its value |
| `OpenIdConnect` | A JWT from `Authority` whose `aud` is `ApiAudience`, its roles in `RoleClaimType` and mapped by `Roles:*`. A pipeline gets one with the client-credentials grant |
| `Unsecured` | None: every caller is the anonymous visitor, at `UnsecuredAccess` |

- The [operator roles](dashboard.md#operator-roles) apply as for the dashboard's buttons, `Roles:Operations`
  overrides included.
- **Every request with an `Origin` header is refused with `403`** unless the origin is in `AllowedOrigins`. A
  script sends none. This stops cross-origin writes, a DNS-rebinding page's included.
- **It does not stop a rebinding page's reads**: once the page's name points at the host, its `GET` is
  same-origin and carries no `Origin`. Set `AllowedHosts` (ASP.NET Core's host filtering, a top-level key) to the
  names the host is reached by, such as `AllowedHosts=quartz.example.com`; a rebound request carries the page's
  own name and is refused. In `Unsecured` mode, where no credential is needed, also bind the host to loopback
  (`--urls http://127.0.0.1:5000`).
- Each change and each refusal is an entry in the [Action Log](dashboard.md#action-log) with *Via HTTP API*, and an
  audit line, `9007` or `9009`.
- A target sees the host's credential, as it does for a visitor: see [Limitations](#limitations).

## MCP endpoint

From 4.9, `Mcp:Enabled=true` serves the [MCP endpoint](mcp.md) for every scheduler the dashboard shows. A tool names
a scheduler by its key: `orders/OrdersScheduler`.

| Key | Default | What it is |
|---|---|---|
| `Mcp:Enabled` | `false` | Serve the endpoint. Off, because a new surface for an agent is asked for |
| `Mcp:Path` | `/quartz-mcp` | Where. Not the host's own paths, nor the API's, nor under either |
| `Mcp:ReadOnly` | `true` | Refuse every change. Not the host's `ReadOnly`: an agent writes only where it is said in so many words |
| `AllowedOrigins:<n>` | none | Shared with the API: origins a browser may call from |
| `Authentication:ApiAudience` | — | `OpenIdConnect` mode: the audience a bearer token must carry. Required with the endpoint |

- **The same bearers as the API**, by mode, and the same refusals: never the sign-in cookie, never `?t=`, and `401`
  rather than a redirect. A role's token in `Token` mode is a stable identity, so an administrator's token is offered
  `reschedule` and `unschedule`; every holder of that token is one caller.
- The [operator roles](dashboard.md#operator-roles) apply, `Roles:Operations` overrides included, and an agent's
  `MaxAccess` is a ceiling.
- Every `ModelContextProtocol` category is held at `Information` whatever `Logging` says: a longer category such as
  `Logging:LogLevel:ModelContextProtocol.Server=Trace`, and a per-provider setting, included. Below `Information` the
  SDK writes whole JSON-RPC messages, and a message can carry a confirmation token.
- Each change and each refused change is an [Action Log](dashboard.md#action-log) entry with *Via MCP*, the user and
  the client's name.
- A confirmation token is protected by the host's Data Protection keys: set `DataProtection:KeysPath` so a restart, or
  a second instance, reads the tokens the first issued.

## The dashboard and the process

| Key | Default | What it is |
|---|---|---|
| `DashboardPath` | `/quartz` | Where the dashboard is served |
| `ReadOnly` | `false` | Hide and refuse every change, whatever the visitor's role |
| `ClusterDetectionInterval` | `00:01:00` | How often targets are merged into clusters; `00:00:00` never |
| `HistoryRetention` | `1.00:00:00` | How far back the dashboard's own history store keeps executions |
| `HistoryMaxEntriesPerScheduler` | `2000` | How many executions and misfires per scheduler |
| `DataProtection:KeysPath` | — | A directory for the data protection keys, so sign-ins survive a restart. Mount a volume there in a container |

- **Telemetry**: with `OTEL_EXPORTER_OTLP_ENDPOINT` set, Quartz's spans and instruments, ASP.NET Core's
  instruments and the logs go to that collector. ASP.NET Core's request spans are not exported: a span carries the
  address, and an address can carry a token.
- **Logs**: events `9400` to `9410`, listed in [Log Events](../log-events.md). `9400` says at start what the host
  serves, and `9402` each reason it will not start.
- **Behind a reverse proxy**, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the cookie and the OpenID Connect
  redirect see the original scheme and host.

## The container image

| | |
|---|---|
| Image | `ghcr.io/quartznet/quartz-dashboard` |
| Tags | A release `X.Y.Z`; and `X.Y`, `X` and `latest` when it is the newest of its line. A prerelease, its exact version only. Nothing is published from a branch |
| Architectures | `linux/amd64` and `linux/arm64`, one index |
| Base | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`: no shell, ICU and time zones included |
| User | The base image's non-root `app` user |
| Port | `8080` |
| Health | `GET /healthz`. The image has no shell, so the probe is the orchestrator's |

Every image carries a GitHub build provenance attestation. To check one:

```shell
gh attestation verify oci://ghcr.io/quartznet/quartz-dashboard:4.8 --owner quartznet
```

## Limitations

- **One instance while it accepts agents**, as above.
- **Store-attached targets read Quartz's own job data types**, as above.
- **The operator's identity is not forwarded**, from the dashboard or from the [HTTP API](#the-http-api). An HTTP
  target sees the target's `BearerToken`, and an agent the agent token, whoever is signed in. A caller's rights are
  the lower of their role and what the target allows; who did what is in the host's Action Log and audit lines.
